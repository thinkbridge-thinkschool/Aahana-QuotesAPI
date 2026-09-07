using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Shared idempotency guard: every handler below checks the notification log before sending, so
/// an at-least-once redelivery of the same integration event (e.g. after OutboxProcessor crashes
/// between publish and mark-processed) never double-emails a customer.
/// </summary>
public abstract class NotificationHandlerBase(INotificationLog log, INotificationSender sender)
{
    protected async Task NotifyOnceAsync(Guid sourceEventId, Guid orderId, string channel, string message, CancellationToken ct)
    {
        if (await log.AlreadySentAsync(sourceEventId, ct))
            return;

        await sender.SendAsync(orderId, message, ct);
        await log.RecordAsync(NotificationRecord.For(sourceEventId, channel), ct);
    }
}

public sealed class NotifyOrderPlaced(INotificationLog log, INotificationSender sender)
    : NotificationHandlerBase(log, sender), IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public Task HandleAsync(OrderPlacedIntegrationEvent @event, CancellationToken ct = default) =>
        NotifyOnceAsync(@event.EventId, @event.OrderId, "order-placed", $"We received order {@event.OrderId}.", ct);
}

public sealed class NotifyOrderConfirmed(INotificationLog log, INotificationSender sender)
    : NotificationHandlerBase(log, sender), IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public Task HandleAsync(OrderConfirmedIntegrationEvent @event, CancellationToken ct = default) =>
        NotifyOnceAsync(@event.EventId, @event.OrderId, "order-confirmed",
            $"Order {@event.OrderId} is confirmed. Total: {@event.Total} {@event.Currency}.", ct);
}

public sealed class NotifyOrderCancelled(INotificationLog log, INotificationSender sender)
    : NotificationHandlerBase(log, sender), IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public Task HandleAsync(OrderCancelledIntegrationEvent @event, CancellationToken ct = default) =>
        NotifyOnceAsync(@event.EventId, @event.OrderId, "order-cancelled",
            $"Order {@event.OrderId} was cancelled: {@event.Reason}.", ct);
}

public sealed class NotifyPaymentFailed(INotificationLog log, INotificationSender sender)
    : NotificationHandlerBase(log, sender), IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    public Task HandleAsync(PaymentFailedIntegrationEvent @event, CancellationToken ct = default) =>
        NotifyOnceAsync(@event.EventId, @event.OrderId, "payment-failed",
            $"Payment failed for order {@event.OrderId}: {@event.Reason}.", ct);
}

public sealed class NotifyShipmentDispatched(INotificationLog log, INotificationSender sender)
    : NotificationHandlerBase(log, sender), IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>
{
    public Task HandleAsync(ShipmentDispatchedIntegrationEvent @event, CancellationToken ct = default) =>
        NotifyOnceAsync(@event.EventId, @event.OrderId, "shipment-dispatched",
            $"Order {@event.OrderId} shipped via {@event.Carrier}, tracking {@event.TrackingNumber}.", ct);
}
