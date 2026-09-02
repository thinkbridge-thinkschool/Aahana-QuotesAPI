namespace Day20;

// The outbox row is what makes the domain write and the eventual
// publish transactionally consistent: it's written in the exact same
// EF Core transaction as the Quote it describes, so it can never exist
// without the domain change actually having happened, and the domain
// change can never happen without an outbox row recording it.
public class OutboxMessage
{
    // This id is the message's idempotency key downstream - the same
    // role the EventId/MessageId played in Day 19's Service Bus work.
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
