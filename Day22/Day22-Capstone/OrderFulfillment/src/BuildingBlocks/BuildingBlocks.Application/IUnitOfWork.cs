namespace BuildingBlocks.Application;

/// <summary>
/// One instance per module (each module owns its own DbContext/schema) — there is no
/// cross-module unit of work, which is what keeps this a set of modules and not a single
/// tangled aggregate boundary. Saving commits both the aggregate change and any outbox rows
/// written during the same handler in one transaction.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
