using System.Text.Json;
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ordering.Infrastructure;

/// <summary>Rides along in OrderingDbContext, so writes commit atomically with the Order change.</summary>
public class OrderingOutboxWriter(OrderingDbContext db) : IOutboxWriter
{
    public void Add(OutboxMessage message) => db.OutboxMessages.Add(message);
}

/// <summary>The Ordering module's half of OutboxProcessor's polling loop — see BuildingBlocks.Infrastructure.OutboxProcessor.</summary>
public class OrderingOutboxStore(OrderingDbContext db) : IOutboxStore
{
    public string ModuleName => "Ordering";

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
    {
        // SQLite can't translate ORDER BY on a DateTimeOffset column, and the outbox backlog is
        // small by design (OutboxProcessor drains it every few seconds) — ordering client-side
        // after the (indexed, sargable) ProcessedOn filter is cheap enough to not need a
        // provider-specific workaround.
        var unprocessed = await db.OutboxMessages.Where(m => m.ProcessedOn == null).ToListAsync(ct);
        return unprocessed.OrderBy(m => m.OccurredOn).Take(batchSize).ToList();
    }

    public IntegrationEvent Deserialize(OutboxMessage message)
    {
        var type = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown integration event type: {message.Type}");

        return (IntegrationEvent)JsonSerializer.Deserialize(message.Payload, type)!;
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await db.OutboxMessages.FirstAsync(m => m.Id == messageId, ct);
        message.ProcessedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        var message = await db.OutboxMessages.FirstAsync(m => m.Id == messageId, ct);
        message.AttemptCount++;
        message.Error = error;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeadLetterAsync(OutboxMessage message, string reason, CancellationToken ct = default)
    {
        db.DeadLetterMessages.Add(new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            SourceOutboxMessageId = message.Id,
            Type = message.Type,
            Payload = message.Payload,
            FailureReason = reason,
            AttemptCount = message.AttemptCount + 1,
            DeadLetteredOn = DateTimeOffset.UtcNow,
        });

        var tracked = await db.OutboxMessages.FirstAsync(m => m.Id == message.Id, ct);
        tracked.ProcessedOn = DateTimeOffset.UtcNow;
        tracked.Error = $"Dead-lettered: {reason}";
        await db.SaveChangesAsync(ct);
    }
}
