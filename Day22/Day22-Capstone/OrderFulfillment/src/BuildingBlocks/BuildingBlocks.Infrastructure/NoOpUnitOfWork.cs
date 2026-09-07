using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>For a module still backed by InMemoryOutboxStore/in-memory repositories — there's no transaction to commit yet.</summary>
public class NoOpUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
