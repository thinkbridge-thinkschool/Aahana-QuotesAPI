namespace Notifications.Domain;

public interface INotificationLog
{
    Task<bool> AlreadySentAsync(Guid sourceEventId, CancellationToken ct = default);
    Task RecordAsync(NotificationRecord record, CancellationToken ct = default);
}
