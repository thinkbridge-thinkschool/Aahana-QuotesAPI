# Day 19 — Azure Service Bus topics + DLQ

Deployed live against a real Service Bus namespace (`quotesbusec42e5eb`, Standard tier, Azure for Students subscription) — topic `quote-events` with two subscriptions, `audit-log` (`MaxDeliveryCount: 3`, deliberately low to force the DLQ demo quickly) and `search-index` (default `MaxDeliveryCount: 10`).

## Publisher

`Publisher.cs` — the Service Bus `MessageId` is set to the event's own `EventId`, a stable id the producer controls, not an SDK-generated one. That's the actual idempotency key downstream.

```csharp
public static class Publisher
{
    public static async Task PublishDemoBatchAsync(
        ServiceBusClient client,
        string topicName)
    {
        await using var sender = client.CreateSender(topicName);

        var quote1 = new QuoteEvent(
            Guid.NewGuid(), 1, "Ada Lovelace", "That brain of mine is something more than merely mortal.");

        var quote2 = new QuoteEvent(
            Guid.NewGuid(), 2, "Grace Hopper", "The most dangerous phrase is: we've always done it this way.");

        var poison = new QuoteEvent(
            Guid.NewGuid(), 3, "Nobody", "This one is designed to fail every time.", ForcePoison: true);

        await sender.SendMessageAsync(ToMessage(quote1));
        await sender.SendMessageAsync(ToMessage(quote2));

        // Same EventId sent twice - simulates an upstream retry/duplicate delivery.
        await sender.SendMessageAsync(ToMessage(quote1));

        await sender.SendMessageAsync(ToMessage(poison));
    }

    private static ServiceBusMessage ToMessage(QuoteEvent quoteEvent)
    {
        var body = JsonSerializer.Serialize(quoteEvent);

        return new ServiceBusMessage(body)
        {
            MessageId = quoteEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = "quote.created"
        };
    }
}
```

Publishing to the **topic** fans out to both subscriptions independently — confirmed live:

```
audit-log:    activeMessageCount = 4
search-index: activeMessageCount = 4
```

## Consumer — competing-consumer worker

`QuoteEventWorker.cs` — `ServiceBusProcessor` with `MaxConcurrentCalls: 3` against a single subscription is the competing-consumers pattern: several handler invocations run concurrently against the same subscription, each pulling the next available message, rather than one dedicated consumer per message.

```csharp
public sealed class QuoteEventWorker
{
    private readonly IIdempotencyStore _idempotencyStore;

    public async Task RunAsync(
        ServiceBusClient client, string topicName, string subscriptionName,
        TimeSpan runFor, CancellationToken cancellationToken)
    {
        await using var processor = client.CreateProcessor(
            topicName, subscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 3,
                AutoCompleteMessages = false
            });

        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(cancellationToken);
        await Task.Delay(runFor, cancellationToken);
        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var quoteEvent = JsonSerializer.Deserialize<QuoteEvent>(args.Message.Body.ToString())!;

        if (!_idempotencyStore.TryReserve(messageId))
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        try
        {
            if (quoteEvent.ForcePoison)
            {
                throw new InvalidOperationException(
                    $"Simulated processing failure for quote {quoteEvent.QuoteId} " +
                    $"(delivery attempt {args.Message.DeliveryCount})");
            }
            // ... real processing ...
        }
        catch
        {
            _idempotencyStore.Release(messageId);
            throw;
        }

        await args.CompleteMessageAsync(args.Message);
    }
}
```

Nothing here calls `DeadLetterMessageAsync` directly. If the handler throws, the SDK auto-abandons the message; once its delivery count exceeds the subscription's `MaxDeliveryCount`, **Service Bus itself** moves it to the DLQ.

## Idempotency key handling — including a real bug I caught mid-run

`IdempotencyStore.cs` uses a **reserve-then-release** pattern, not "check-then-mark":

```csharp
public interface IIdempotencyStore
{
    bool TryReserve(string eventId);
    void Release(string eventId);
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _reservedEventIds = new();

    public bool TryReserve(string eventId) => _reservedEventIds.TryAdd(eventId, 0);
    public void Release(string eventId) => _reservedEventIds.TryRemove(eventId, out _);
}
```

**What actually happened, in order:** my first working version marked an event id "processed" the moment `TryReserve` succeeded — *before* the handler had actually done anything. First live run: the poison message failed once (`delivery attempt 1`), got abandoned and redelivered, but on redelivery `TryReserve` now returned `false` (already "reserved" from the failed first attempt) — so the retry was treated as a duplicate and silently completed. Checked the subscription counts right after: `activeMessageCount: 0, deadLetterMessageCount: 0`. The poison message hadn't reached the DLQ — it had **vanished**, swallowed by my own dedup logic mistaking "attempted" for "succeeded."

Fixed it to only leave the reservation in place on success, and to explicitly `Release` it in a `catch` block on failure — so a message that keeps failing keeps being genuinely retried (and eventually dead-lettered), while a message that actually succeeded stays correctly deduped against any later redelivery. Reran: `delivery attempt 1`, `2`, `3` all logged, then confirmed in the DLQ (see below).

## Proof a poison message landed in the DLQ

Subscription counts after the consumer ran, queried directly against the live namespace:

```
$ az servicebus topic subscription show --topic-name quote-events --name audit-log --query countDetails
{
  "activeMessageCount": 0,
  "deadLetterMessageCount": 1
}
```

The actual dead-lettered message, peeked from `quote-events/Subscriptions/audit-log/$DeadLetterQueue`:

```
DLQ message - MessageId: b836add9-84fb-43d8-916c-11add81ef085, DeliveryCount: 4,
DeadLetterReason: MaxDeliveryCountExceeded,
DeadLetterErrorDescription: Message could not be consumed after 3 delivery attempts.
  Body: {"EventId":"b836add9-84fb-43d8-916c-11add81ef085","QuoteId":3,"Author":"Nobody","Text":"This one is designed to fail every time.","ForcePoison":true}
```

`DeadLetterReason: MaxDeliveryCountExceeded` is Service Bus's own reason code — this wasn't a manual `DeadLetterMessageAsync` call from my code, it's the broker doing exactly what `MaxDeliveryCount: 3` says it will do.

The genuine duplicate (`quote1` sent twice, same `EventId`) was correctly deduped in the same run — one handler processed it, the concurrent delivery of its duplicate was recognized and skipped without reprocessing, confirmed by interleaved log output from two different competing-consumer threads picking up both copies nearly simultaneously.

## What I learned this session

Idempotency has a timing question hiding inside it that's easy to get backwards: *when* do you record that something happened? Recording it before the work finishes ("I've started, so mark it done") is the failure mode that quietly breaks the dead-letter story entirely — a message that only ever fails never gets the chance to fail *again*, because its own failed attempt gets mistaken for someone else's successful one. The fix isn't "add try/catch," it's recognizing that a reservation and a completion are two different states that need two different operations.

## What would break this

- The idempotency store is in-memory (`ConcurrentDictionary`) — a worker restart forgets every reservation, so a message that's genuinely already been processed (and completed) but somehow gets redelivered anyway (a crash between `CompleteMessageAsync` succeeding and the ack reaching the broker is rare but possible) would be reprocessed after a restart. A real system needs a persisted idempotency store (a database table keyed on event id with a unique constraint, Redis with a TTL) that survives the process, not survives-only-until-restart.
- `MaxDeliveryCount: 3` is deliberately low for this demo. In production, a transient failure (a downstream dependency blip) could exhaust delivery attempts and dead-letter a message that would have succeeded on a 4th try — the count needs to be tuned against how long transient failures actually last, not just picked for a fast demo.
- Nothing here alerts on a growing DLQ. A poison message sitting in `$DeadLetterQueue` is silent unless something (a monitor, an alert rule on `deadLetterMessageCount`) is watching for it — otherwise it just accumulates unnoticed.
