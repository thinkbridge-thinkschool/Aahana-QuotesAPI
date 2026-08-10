using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository(QuoteDbContext db)
    : ICollectionRepository
{
    public async Task<Collection?> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        return await db.Set<Collection>()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.Id == id,
                cancellationToken);
    }

    public async Task<Collection> Add(
        Collection collection,
        CancellationToken cancellationToken)
    {
        db.Set<Collection>().Add(collection);

        await db.SaveChangesAsync(cancellationToken);

        return collection;
    }

    public async Task Update(
        Collection collection,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task Delete(
        Collection collection,
        CancellationToken cancellationToken)
    {
        db.Set<Collection>().Remove(collection);

        await db.SaveChangesAsync(cancellationToken);
    }
}