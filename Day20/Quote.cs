namespace Day20;

public class Quote
{
    public int Id { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    // The EF Core relationship: one quote can have several outbox
    // messages over its lifetime (created, later updated, deleted -
    // this demo only exercises "created", but the shape is real).
    public ICollection<OutboxMessage> OutboxMessages { get; set; } =
        new List<OutboxMessage>();
}
