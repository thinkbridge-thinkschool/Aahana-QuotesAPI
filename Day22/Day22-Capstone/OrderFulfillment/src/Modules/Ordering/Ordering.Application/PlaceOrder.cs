using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using Ordering.Domain;

namespace Ordering.Application;

public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice, string Currency);

public sealed record PlaceOrderCommand(Guid CustomerId, IReadOnlyCollection<PlaceOrderLine> Lines);

/// <summary>
/// Places the order and, in the same unit of work, writes the OrderPlaced integration event to
/// the outbox — Inventory only finds out once this transaction actually commits.
/// </summary>
public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IIntegrationEventPublisher events,
    IUnitOfWork unitOfWork)
{
    public async Task<Guid> HandleAsync(PlaceOrderCommand command, CancellationToken ct = default)
    {
        var lines = command.Lines.Select(l => OrderLine.Create(l.Sku, l.Quantity, Money.Of(l.UnitPrice, l.Currency)));
        var order = Order.Place(command.CustomerId, lines);

        orders.Add(order);

        await events.PublishAsync(new OrderPlacedIntegrationEvent(
            order.Id,
            order.CustomerId,
            order.Lines.Select(l => new OrderLineDto(l.Sku, l.Quantity)).ToList()), ct);

        order.ClearDomainEvents();
        await unitOfWork.SaveChangesAsync(ct);

        return order.Id;
    }
}
