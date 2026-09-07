namespace BuildingBlocks.Infrastructure;

/// <summary>
/// Appends to the calling module's own outbox table using the DbContext already inside the
/// current unit of work, so the insert rides along with SaveChangesAsync() rather than opening
/// a second connection/transaction.
/// </summary>
public interface IOutboxWriter
{
    void Add(OutboxMessage message);
}
