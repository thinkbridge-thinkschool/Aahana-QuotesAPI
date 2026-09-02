using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Day20;

public sealed record QuoteCreatedPayload(int QuoteId, string Author, string Text);

// Writes the domain change and its outbox row in one EF Core
// transaction. If anything fails before the commit - including the
// deliberate crash-simulation below - the whole transaction rolls back,
// so there is never a Quote row without a matching OutboxMessage row,
// or vice versa.
public sealed class QuoteService(OutboxDbContext db)
{
    public async Task<Quote> CreateQuoteAsync(
        string author,
        string text,
        bool simulateCrashBeforeCommit = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var quote = new Quote
        {
            Author = author,
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(
            new QuoteCreatedPayload(quote.Id, quote.Author, quote.Text));

        var outboxMessage = new OutboxMessage
        {
            QuoteId = quote.Id,
            Type = "QuoteCreated",
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            Sent = false
        };

        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        if (simulateCrashBeforeCommit)
        {
            // Both inserts above ran inside this transaction but were
            // never committed. Throwing here - before CommitAsync -
            // and letting the transaction get disposed without a
            // commit is functionally identical to the process dying
            // at this exact point: SQLite rolls the whole thing back.
            throw new InvalidOperationException(
                "Simulated crash after writing rows but before commit.");
        }

        await transaction.CommitAsync();

        return quote;
    }
}
