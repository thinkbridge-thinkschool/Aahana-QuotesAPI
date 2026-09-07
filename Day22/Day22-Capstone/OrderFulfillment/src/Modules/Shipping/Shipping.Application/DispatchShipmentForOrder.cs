using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using Shipping.Domain;

namespace Shipping.Application;

/// <summary>Reacts to OrderPaymentReceivedIntegrationEvent — the last precondition for shipping has been met.</summary>
public sealed class OnOrderPaymentReceived(
    IShipmentRepository shipments,
    IIntegrationEventPublisher events,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<OrderPaymentReceivedIntegrationEvent>
{
    private const string DefaultCarrier = "GlobalShip";

    public async Task HandleAsync(OrderPaymentReceivedIntegrationEvent @event, CancellationToken ct = default)
    {
        var shipment = Shipment.Dispatch(@event.OrderId, DefaultCarrier);
        shipments.Add(shipment);

        await events.PublishAsync(
            new ShipmentDispatchedIntegrationEvent(@event.OrderId, shipment.Id, shipment.Carrier, shipment.TrackingNumber), ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
