using System.Text.Json;

using Azure.Messaging.ServiceBus;

namespace Day19;

// A competing-consumer worker: ServiceBusProcessor with
// MaxConcurrentCalls > 1 means several message-handler invocations run
// concurrently against the *same* subscription, each pulling the next
// available message - the standard "competing consumers" pattern, not
// one dedicated consumer per message type.
//
// If a handler throws, the SDK's default AutoCompleteMessages behavior
// abandons the message (its lock is released, delivery count goes up).
// Once a message's delivery count exceeds the subscription's
// MaxDeliveryCount, Service Bus itself - not this code - moves it to
// the dead-letter queue. Nothing here calls DeadLetterMessageAsync
// directly; the poison message reaches the DLQ purely through repeated
// natural failures.
public sealed class QuoteEventWorker
{
    private readonly IIdempotencyStore _idempotencyStore;

    public QuoteEventWorker(IIdempotencyStore idempotencyStore)
    {
        _idempotencyStore = idempotencyStore;
    }

    public async Task RunAsync(
        ServiceBusClient client,
        string topicName,
        string subscriptionName,
        TimeSpan runFor,
        CancellationToken cancellationToken)
    {
        await using var processor = client.CreateProcessor(
            topicName,
            subscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 3,
                AutoCompleteMessages = false
            });

        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(cancellationToken);

        try
        {
            await Task.Delay(runFor, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected if the caller cancels early.
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var handlerId = Environment.CurrentManagedThreadId;
        var messageId = args.Message.MessageId;

        var quoteEvent = JsonSerializer.Deserialize<QuoteEvent>(
            args.Message.Body.ToString())
            ?? throw new InvalidOperationException(
                "Message body did not deserialize to a QuoteEvent.");

        if (!_idempotencyStore.TryReserve(messageId))
        {
            Console.WriteLine(
                $"[handler {handlerId}] duplicate of {messageId} " +
                $"(quote {quoteEvent.QuoteId}) - already processed, skipping work");

            await args.CompleteMessageAsync(args.Message);
            return;
        }

        try
        {
            if (quoteEvent.ForcePoison)
            {
                // Deliberately blow up every time - this is what makes
                // it "poison": no amount of retrying ever succeeds.
                throw new InvalidOperationException(
                    $"Simulated processing failure for quote {quoteEvent.QuoteId} " +
                    $"(delivery attempt {args.Message.DeliveryCount})");
            }

            Console.WriteLine(
                $"[handler {handlerId}] processing {messageId} - " +
                $"quote {quoteEvent.QuoteId} by {quoteEvent.Author}");
        }
        catch
        {
            // The work never actually succeeded - release the
            // reservation so the next delivery attempt (Service Bus
            // will redeliver after the lock is abandoned) genuinely
            // retries instead of being treated as a duplicate.
            _idempotencyStore.Release(messageId);
            throw;
        }

        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        Console.WriteLine(
            $"[error] {args.ErrorSource}: {args.Exception.Message}");

        return Task.CompletedTask;
    }
}
