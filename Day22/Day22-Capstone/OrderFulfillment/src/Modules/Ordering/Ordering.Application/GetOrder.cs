using Ordering.Domain;

namespace Ordering.Application;

public sealed record OrderLineView(string Sku, int Quantity, decimal UnitPrice, string Currency);

public sealed record OrderView(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset PlacedOn,
    IReadOnlyCollection<OrderLineView> Lines);

/// <summary>
/// The read side PlaceOrderHandler's own Location header (Results.Created's second argument)
/// has been pointing at since Day 22 without anything actually serving it — a real, pre-existing
/// gap, not new scope invented for this. Read-only; never touches IUnitOfWork, matching this
/// aggregate's own CQS split (PlaceOrderHandler writes, this only reads).
/// </summary>
public sealed class GetOrderHandler(IOrderRepository orders)
{
    public async Task<OrderView?> HandleAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
            return null;

        return new OrderView(
            order.Id,
            order.CustomerId,
            order.Status.ToString(),
            order.Total.Amount,
            order.Total.Currency,
            order.PlacedOn,
            order.Lines.Select(l => new OrderLineView(l.Sku, l.Quantity, l.UnitPrice.Amount, l.UnitPrice.Currency)).ToList());
    }
}
