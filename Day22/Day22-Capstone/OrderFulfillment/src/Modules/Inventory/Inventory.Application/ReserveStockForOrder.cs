using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Domain;
using Inventory.Domain;

namespace Inventory.Application;

/// <summary>
/// Reacts to OrderPlacedIntegrationEvent. Reserves every line or none — a partial reservation
/// would leave the order confirmable for stock it doesn't actually have covered.
/// </summary>
public sealed class OnOrderPlaced(IStockRepository stock, IIntegrationEventPublisher events, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async Task HandleAsync(OrderPlacedIntegrationEvent @event, CancellationToken ct = default)
    {
        var reserved = new List<(string Sku, int Quantity)>();

        try
        {
            foreach (var line in @event.Lines)
            {
                var item = await stock.GetAsync(line.Sku, ct)
                    ?? throw new DomainInvariantException($"Unknown SKU {line.Sku}.");

                item.Reserve(line.Quantity);
                reserved.Add((line.Sku, line.Quantity));
            }

            await stock.RecordReservationAsync(@event.OrderId, reserved, ct);
            await events.PublishAsync(new StockReservedIntegrationEvent(@event.OrderId), ct);
        }
        catch (DomainInvariantException ex)
        {
            // Roll back only what this handler reserved before hitting the shortfall — the
            // repository holds the aggregates in the current unit of work, so undoing here and
            // saving is equivalent to never having reserved them.
            foreach (var (sku, quantity) in reserved)
            {
                var item = await stock.GetAsync(sku, ct);
                item?.Release(quantity);
            }

            await events.PublishAsync(new StockReservationFailedIntegrationEvent(@event.OrderId, ex.Message), ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>Reacts to OrderCancelledIntegrationEvent — releases whatever this order's reservation held, if any.</summary>
public sealed class OnOrderCancelled(IStockRepository stock, IUnitOfWork unitOfWork) : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent @event, CancellationToken ct = default)
    {
        var reservation = await stock.GetReservationAsync(@event.OrderId, ct);

        foreach (var (sku, quantity) in reservation)
        {
            var item = await stock.GetAsync(sku, ct);
            item?.Release(quantity);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
