# Day 20 — The outbox pattern

## The outbox table

`OutboxMessage.cs` — note `SequenceNumber`, not `Id`, is the actual EF/SQLite primary key. `Id` (a `Guid`) is the message's business/idempotency key, separately unique-indexed:

```csharp
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // A dedicated auto-incrementing column for the relay's "oldest
    // unsent first" ordering, rather than ordering by CreatedAt.
    // Wall-clock timestamps aren't reliable for ordering under
    // concurrent writers (clock resolution, clock skew across
    // processes) - a database-assigned sequence is.
    public long SequenceNumber { get; set; }

    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public bool Sent { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
```

The EF Core relationship (`Quote` 1 → many `OutboxMessage`), in `OutboxDbContext.cs`:

```csharp
modelBuilder.Entity<OutboxMessage>(entity =>
{
    entity.HasKey(m => m.SequenceNumber);
    entity.HasIndex(m => m.Id).IsUnique();

    entity.HasOne(m => m.Quote)
        .WithMany(q => q.OutboxMessages)
        .HasForeignKey(m => m.QuoteId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasIndex(m => m.Sent);
});
```

Writing the domain change and the outbox row **in one transaction** — `QuoteService.cs`:

```csharp
public async Task<Quote> CreateQuoteAsync(string author, string text, bool simulateCrashBeforeCommit = false)
{
    await using var transaction = await db.Database.BeginTransactionAsync();

    var quote = new Quote { Author = author, Text = text, CreatedAt = DateTimeOffset.UtcNow };
    db.Quotes.Add(quote);
    await db.SaveChangesAsync();

    var payload = JsonSerializer.Serialize(new QuoteCreatedPayload(quote.Id, quote.Author, quote.Text));
    var outboxMessage = new OutboxMessage
    {
        QuoteId = quote.Id, Type = "QuoteCreated", Payload = payload,
        CreatedAt = DateTimeOffset.UtcNow, Sent = false
    };
    db.OutboxMessages.Add(outboxMessage);
    await db.SaveChangesAsync();

    if (simulateCrashBeforeCommit)
    {
        throw new InvalidOperationException("Simulated crash after writing rows but before commit.");
    }

    await transaction.CommitAsync();
    return quote;
}
```

## The relay

`OutboxRelay.cs` — marks each message sent **immediately after publishing it**, not batched at the end, so a crash only ever affects the one message that was in-flight:

```csharp
public sealed class OutboxRelay(OutboxDbContext db, IMessagePublisher publisher)
{
    public async Task<int> ProcessPendingAsync(bool simulateCrashAfterFirstPublish = false)
    {
        var pending = await db.OutboxMessages
            .Where(m => !m.Sent)
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync();

        var sentCount = 0;

        foreach (var message in pending)
        {
            await publisher.PublishAsync(message);

            if (simulateCrashAfterFirstPublish)
            {
                throw new InvalidOperationException(
                    "Simulated crash after publish but before marking sent.");
            }

            message.Sent = true;
            message.SentAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            sentCount++;
        }

        return sentCount;
    }
}
```

## The crash scenario I tested — and why no message is lost or duplicated

I tested **two** crash points, not one, because the outbox pattern actually has two different failure modes to prove safe:

**1. Crash before the transaction commits** (inside `CreateQuoteAsync`, right after both the `Quote` and `OutboxMessage` inserts are staged but before `CommitAsync`). Because both writes are in the same transaction, the crash rolls back *both* — there is never a domain change without its outbox row, or an outbox row for a domain change that didn't really happen. Verified: `Quotes=0, OutboxMessages=0` afterward. Nothing to lose, because nothing was ever really committed.

**2. Crash after the relay publishes but before it marks the row sent** (inside `ProcessPendingAsync`, right after `publisher.PublishAsync` succeeds, before `message.Sent = true` is saved). This is the actual hazard the outbox pattern exists to survive: the message has already reached the "broker," but the database doesn't know it yet. Verified: after the simulated crash, `OutboxMessage.Sent` was still `false` — the row survived, unmarked, exactly where a real relay restarting would find it again.

When the relay "restarts" (runs `ProcessPendingAsync` again with no crash), it finds that same still-unsent row and **republishes it** — the downstream inbox now has two lines for the identical message id. That's the duplicate. **This is expected and correct, not a bug**: the outbox pattern only ever promises *at-least-once* delivery, never *exactly-once* — guaranteeing exactly-once across a network boundary is a much harder, generally impractical problem. What makes this safe is the consumer: it deduplicates on the message id (`Guid`), so the second delivery is recognized and skipped. Verified: `Consumer` result was `applied=1, duplicatesSkipped=1` — the quote was delivered twice, applied once.

**Full real output from one run** (`Day20/Program.cs`, no manual edits):

```
=== Scenario A: crash before commit ===
Caught simulated crash: Simulated crash after writing rows but before commit.
After crash-before-commit: Quotes=0, OutboxMessages=0 (expect 0, 0 - the transaction rolled back atomically)

=== Scenario B: crash after publish, before marking sent ===
Created quote 1 and its outbox row, in one transaction.
Caught simulated crash: Simulated crash after publish but before marking sent.
After the crash: OutboxMessage.Sent=False (expect false - the row was never lost, just never marked complete), inbox has 1 line(s)

=== Scenario C: relay 'restarts' and finds the still-unsent row ===
Relay processed 1 message(s) on this pass.
After the restart: OutboxMessage.Sent=True, inbox now has 2 line(s) for the same message id (the earlier crash's publish plus this one - a genuine duplicate delivery)

=== Consumer processes the inbox ===
[consumer] applying QuoteCreated 72c38e3f-7f2d-471d-bc78-0edbe761f18e: {"QuoteId":1,"Author":"Ada Lovelace","Text":"That brain of mine is something more than merely mortal."}
[consumer] duplicate delivery of 72c38e3f-7f2d-471d-bc78-0edbe761f18e - already applied, skipping
Consumer result: applied=1, duplicatesSkipped=1 (the quote was delivered twice but applied exactly once)
```

**A real bug caught along the way:** the first version ordered the relay's query by `CreatedAt` (a `DateTimeOffset`). EF Core's SQLite provider can't translate `ORDER BY` on `DateTimeOffset` at all — it throws `NotSupportedException` outright, not silently wrong results. Fixed by adding a dedicated `SequenceNumber` column as the actual primary key (SQLite auto-increments only the single-column `INTEGER PRIMARY KEY`), which is also the more correct design regardless of the SQLite quirk: wall-clock timestamps aren't reliable for ordering under concurrent writers, a database-assigned sequence is.

## What I learned this session

The outbox pattern doesn't remove the "publish could fail" problem — it relocates it somewhere survivable. Without the pattern, a crash between "write to DB" and "publish to broker" loses the message outright (nobody downstream ever hears about it) or, if you flip the order, publishes a message for a DB write that then fails to commit (a phantom event for something that never happened). With the pattern, that same crash just leaves a row sitting there, unsent — recoverable by definition, because "unsent" is a real, durable, query-able state, not a gap in a log file.

## What would break this

- The relay here runs the publish and the "mark sent" write sequentially, one message at a time - correct, but sequential. A relay that batches many messages between saves reintroduces exactly the hazard this pattern is meant to remove: a crash mid-batch would leave every message in that batch as a genuine duplicate on restart, not just one.
- The consumer's dedup set is in-memory and rebuilt from scratch each run in this demo - a real consumer needs a persisted set of applied message ids (or a unique constraint on the write it's applying), or a restart of the *consumer* itself would forget what it already applied and reprocess everything in the inbox from the start.
- Nothing here ever removes old `Sent = true` rows. A real system needs a cleanup/archival job for the outbox table, or it grows forever - the table this pattern relies on for correctness becomes its own operational liability if left unmanaged.
