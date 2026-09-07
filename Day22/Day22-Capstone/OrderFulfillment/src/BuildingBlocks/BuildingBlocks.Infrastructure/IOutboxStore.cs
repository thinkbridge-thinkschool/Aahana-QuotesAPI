using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// Each module's Infrastructure layer implements this once, over its own DbContext/outbox table
/// — modules never share a table, let alone query across each other's. OutboxProcessor holds one
/// of these per module (registered in Host) and drives them all on the same polling loop.
/// </summary>
public interface IOutboxStore
{
    string ModuleName { get; }

    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Deserializes the stored payload back into the concrete IntegrationEvent type.</summary>
    IntegrationEvent Deserialize(OutboxMessage message);

    Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default);

    Task RecordFailureAsync(Guid messageId, string error, CancellationToken ct = default);

    Task DeadLetterAsync(OutboxMessage message, string reason, CancellationToken ct = default);
}
