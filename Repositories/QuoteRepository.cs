using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository(QuoteDbContext db) : IQuoteRepository
{
    public async Task<IReadOnlyList<Quote>> GetPagedAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
            .AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
        .AsNoTracking()
        .Where(q => !q.IsDeleted)
        .FirstOrDefaultAsync(
        q => q.Id == id,
        cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        db.Quotes.Add(quote);

        await db.SaveChangesAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(
    int id,
    CancellationToken cancellationToken)
{
    var quote = await db.Quotes
        .FirstOrDefaultAsync(
            q => q.Id == id,
            cancellationToken);

    if (quote is null)
    {
        return false;
    }

    quote.SoftDelete();

    await db.SaveChangesAsync(cancellationToken);

    return true;
}
}