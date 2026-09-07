using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Domain;
using Ordering.Domain;

namespace Ordering.Application;

/// <summary>
/// Base for the five handlers below: load the order, apply one state transition, publish
/// whatever new integration event that transition raises, save. Every handler here is a fresh
/// reaction to a fact that already happened in another module — none of them call back into
/// Inventory/Payments/Shipping directly.
/// </summary>
public abstract class OrderReactionHandlerBase(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
{
    protected async Task ApplyAsync(Guid orderId, Action<Order> transition, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct)
            ?? throw new DomainInvariantException($"Order {orderId} not found.");

        transition(order);

        foreach (var domainEvent in order.DomainEvents)
        {
            var integrationEvent = domainEvent switch
            {
                OrderConfirmed => new OrderConfirmedIntegrationEvent(order.Id, order.CustomerId, order.Total.Amount, order.Total.Currency),
                OrderPaymentReceived => (IntegrationEvent)new OrderPaymentReceivedIntegrationEvent(order.Id, order.CustomerId),
                OrderCancelled cancelled => new OrderCancelledIntegrationEvent(order.Id, cancelled.Reason),
                _ => null,
            };

            if (integrationEvent is not null)
                await events.PublishAsync(integrationEvent, ct);
        }

        order.ClearDomainEvents();
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>Inventory reserved stock for every line -> the order can move to Confirmed.</summary>
public sealed class OnStockReserved(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : OrderReactionHandlerBase(orders, events, unitOfWork), IIntegrationEventHandler<StockReservedIntegrationEvent>
{
    public Task HandleAsync(StockReservedIntegrationEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.OrderId, order => order.Confirm(), ct);
}

/// <summary>Inventory couldn't cover every line -> cancel rather than confirm a partial order.</summary>
public sealed class OnStockReservationFailed(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : OrderReactionHandlerBase(orders, events, unitOfWork), IIntegrationEventHandler<StockReservationFailedIntegrationEvent>
{
    public Task HandleAsync(StockReservationFailedIntegrationEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.OrderId, order => order.Cancel($"Stock reservation failed: {@event.Reason}"), ct);
}

/// <summary>Payments captured the charge -> the order can move to PaymentReceived.</summary>
public sealed class OnPaymentCaptured(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : OrderReactionHandlerBase(orders, events, unitOfWork), IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    public Task HandleAsync(PaymentCapturedIntegrationEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.OrderId, order => order.MarkPaymentReceived(), ct);
}

/// <summary>Payment was declined -> cancel; Inventory reacts to OrderCancelled by releasing the reservation.</summary>
public sealed class OnPaymentFailed(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : OrderReactionHandlerBase(orders, events, unitOfWork), IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    public Task HandleAsync(PaymentFailedIntegrationEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.OrderId, order => order.Cancel($"Payment failed: {@event.Reason}"), ct);
}

/// <summary>Shipping dispatched the package -> the order reaches its final, happy-path state.</summary>
public sealed class OnShipmentDispatched(IOrderRepository orders, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : OrderReactionHandlerBase(orders, events, unitOfWork), IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>
{
    public Task HandleAsync(ShipmentDispatchedIntegrationEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.OrderId, order => order.MarkShipped(), ct);
}
