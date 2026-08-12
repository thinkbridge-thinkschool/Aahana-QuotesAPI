using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public class RefreshTokenService
{
    private readonly QuoteDbContext _db;
    private readonly IClock _clock;

    public RefreshTokenService(
        QuoteDbContext db,
        IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task RevokeTokenFamily(
        RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        var current = token;

        while (current is not null)
        {
            if (current.RevokedAt is null)
            {
                current.RevokedAt =
                    _clock.UtcNow.UtcDateTime;
            }

            if (string.IsNullOrWhiteSpace(
                    current.ReplacedByToken))
            {
                break;
            }

            var next =
                await _db.RefreshTokens
                    .FirstOrDefaultAsync(
                        t => t.Token ==
                             current.ReplacedByToken,
                        cancellationToken);

            if (next is null)
            {
                break;
            }

            current = next;
        }

        await _db.SaveChangesAsync(
            cancellationToken);
    }
}