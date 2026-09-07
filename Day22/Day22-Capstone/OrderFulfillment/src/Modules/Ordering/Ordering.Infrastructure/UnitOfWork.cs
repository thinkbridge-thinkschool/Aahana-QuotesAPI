using BuildingBlocks.Application;

namespace Ordering.Infrastructure;

public class UnitOfWork(OrderingDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
