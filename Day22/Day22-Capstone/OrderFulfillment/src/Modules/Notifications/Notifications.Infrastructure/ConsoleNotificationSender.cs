using Microsoft.Extensions.Logging;
using Notifications.Application;

namespace Notifications.Infrastructure;

/// <summary>Stand-in for a real email/SMS/push provider — logs instead of sending, so the flow is observable without external config.</summary>
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Guid orderId, string message, CancellationToken ct = default)
    {
        logger.LogInformation("[notification] order {OrderId}: {Message}", orderId, message);
        return Task.CompletedTask;
    }
}
