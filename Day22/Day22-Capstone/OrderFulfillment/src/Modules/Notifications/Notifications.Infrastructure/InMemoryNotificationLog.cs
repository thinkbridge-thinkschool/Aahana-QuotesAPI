using System.Collections.Concurrent;
using Notifications.Domain;

namespace Notifications.Infrastructure;

public class InMemoryNotificationLog : INotificationLog
{
    private readonly ConcurrentDictionary<Guid, NotificationRecord> _sent = new();

    public Task<bool> AlreadySentAsync(Guid sourceEventId, CancellationToken ct = default) =>
        Task.FromResult(_sent.ContainsKey(sourceEventId));

    public Task RecordAsync(NotificationRecord record, CancellationToken ct = default)
    {
        _sent[record.SourceEventId] = record;
        return Task.CompletedTask;
    }
}
