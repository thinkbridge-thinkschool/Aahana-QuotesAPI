using BuildingBlocks.Domain;

namespace Notifications.Domain;

/// <summary>
/// Not a DDD aggregate in any meaningful sense — Notifications is a generic-subdomain, purely
/// reactive module with no invariants of its own to protect. This row exists only so a handler
/// can check "have I already sent this?" and be idempotent under the outbox's at-least-once
/// delivery, keyed on the source IntegrationEvent's EventId.
/// </summary>
public sealed class NotificationRecord : Entity<Guid>
{
    public Guid SourceEventId { get; private set; }
    public string Channel { get; private set; } = default!;
    public DateTimeOffset SentOn { get; private set; }

    private NotificationRecord() { }

    public static NotificationRecord For(Guid sourceEventId, string channel) => new()
    {
        Id = Guid.NewGuid(),
        SourceEventId = sourceEventId,
        Channel = channel,
        SentOn = DateTimeOffset.UtcNow,
    };
}
