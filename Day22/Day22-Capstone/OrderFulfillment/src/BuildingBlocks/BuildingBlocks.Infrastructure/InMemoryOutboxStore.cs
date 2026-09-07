using System.Collections.Concurrent;
using System.Text.Json;
using BuildingBlocks.Application;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// A dependency-free stand-in for a module's real (EF Core-backed) outbox table — see
/// Ordering.Infrastructure.OrderingOutbox for what the real thing looks like once a module has a
/// DbContext. Registered as a singleton per module so state survives across request scopes;
/// swapping this for OrderingOutbox's pattern is the only change needed to make a module durable.
/// </summary>
public class InMemoryOutboxStore(string moduleName) : IOutboxWriter, IOutboxStore
{
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();
    private readonly List<DeadLetterMessage> _deadLetters = new();

    public string ModuleName => moduleName;

    public void Add(OutboxMessage message) => _messages[message.Id] = message;

    public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OutboxMessage>>(_messages.Values
            .Where(m => m.ProcessedOn is null)
            .OrderBy(m => m.OccurredOn)
            .Take(batchSize)
            .ToList());

    public IntegrationEvent Deserialize(OutboxMessage message)
    {
        var type = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown integration event type: {message.Type}");

        return (IntegrationEvent)JsonSerializer.Deserialize(message.Payload, type)!;
    }

    public Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
            message.ProcessedOn = DateTimeOffset.UtcNow;

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.AttemptCount++;
            message.Error = error;
        }

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(OutboxMessage message, string reason, CancellationToken ct = default)
    {
        _deadLetters.Add(new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            SourceOutboxMessageId = message.Id,
            Type = message.Type,
            Payload = message.Payload,
            FailureReason = reason,
            AttemptCount = message.AttemptCount + 1,
            DeadLetteredOn = DateTimeOffset.UtcNow,
        });

        if (_messages.TryGetValue(message.Id, out var tracked))
        {
            tracked.ProcessedOn = DateTimeOffset.UtcNow;
            tracked.Error = $"Dead-lettered: {reason}";
        }

        return Task.CompletedTask;
    }
}
