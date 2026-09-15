using Microsoft.EntityFrameworkCore;

namespace Day20;

// Polls the outbox for unsent rows, publishes each one, then marks it
// sent - immediately, per message, not batched at the end. That's what
// bounds the damage of a crash to at most the one message that was
// in-flight when it happened, instead of re-publishing an entire batch.
public sealed class OutboxRelay(OutboxDbContext db, IMessagePublisher publisher)
{
    // Returns how many messages were successfully published and marked
    // sent in this pass, so callers (and this demo) can see progress.
    public async Task<int> ProcessPendingAsync(
        bool simulateCrashAfterFirstPublish = false)
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
                // The message has already been delivered to the
                // "broker" (the line is in the inbox file) - but the
                // process dies right here, before the database ever
                // finds out. This is the exact hazard the outbox
                // pattern has to survive: the publish succeeded, the
                // bookkeeping didn't.
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
