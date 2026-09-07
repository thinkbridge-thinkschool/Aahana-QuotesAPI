using BuildingBlocks.Domain;

namespace Ordering.Domain;

/// <summary>
/// The core aggregate of the whole capstone slice. Owns the order lifecycle end to end
/// (Pending -> Confirmed -> PaymentReceived -> Shipped, or -> Cancelled from any pre-Shipped
/// state) and is the only thing allowed to change that lifecycle. Inventory, Payments, and
/// Shipping never write to an Order directly — they publish integration events; Ordering's
/// Application layer is what reacts and calls back into this aggregate.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = new();

    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public Money Total => _lines.Aggregate(Money.Zero(), (sum, line) => sum.Add(line.LineTotal));
    public DateTimeOffset PlacedOn { get; private set; }

    private Order() { }

    public static Order Place(Guid customerId, IEnumerable<OrderLine> lines)
    {
        var lineList = lines.ToList();
        if (lineList.Count == 0)
            throw new DomainInvariantException("An order must contain at least one line.");
        if (customerId == Guid.Empty)
            throw new DomainInvariantException("An order must belong to a customer.");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            PlacedOn = DateTimeOffset.UtcNow,
        };
        order._lines.AddRange(lineList);

        order.Raise(new OrderPlaced(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            order.Id,
            customerId,
            lineList.Select(l => (l.Sku, l.Quantity)).ToList()));

        return order;
    }

    /// <summary>Called by Ordering.Application when Inventory reports the reservation succeeded.</summary>
    public void Confirm()
    {
        EnsureNotTerminal();
        if (Status != OrderStatus.Pending)
            throw new DomainInvariantException($"Cannot confirm an order in status {Status}.");

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmed(Guid.NewGuid(), DateTimeOffset.UtcNow, Id));
    }

    /// <summary>Called by Ordering.Application when Payments reports a captured payment.</summary>
    public void MarkPaymentReceived()
    {
        EnsureNotTerminal();
        if (Status != OrderStatus.Confirmed)
            throw new DomainInvariantException($"Cannot mark payment received for an order in status {Status}.");

        Status = OrderStatus.PaymentReceived;
        Raise(new OrderPaymentReceived(Guid.NewGuid(), DateTimeOffset.UtcNow, Id));
    }

    /// <summary>Called by Ordering.Application when Shipping reports dispatch.</summary>
    public void MarkShipped()
    {
        if (Status != OrderStatus.PaymentReceived)
            throw new DomainInvariantException($"Cannot ship an order in status {Status}.");

        Status = OrderStatus.Shipped;
        Raise(new OrderShipped(Guid.NewGuid(), DateTimeOffset.UtcNow, Id));
    }

    /// <summary>
    /// Reachable from any pre-Shipped state: a failed stock reservation or a declined payment
    /// both cancel the order the same way, from wherever it currently sits in the pipeline.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
            throw new DomainInvariantException($"Cannot cancel an order in status {Status}.");

        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelled(Guid.NewGuid(), DateTimeOffset.UtcNow, Id, reason));
    }

    private void EnsureNotTerminal()
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Cancelled)
            throw new DomainInvariantException($"Order {Id} is in a terminal state ({Status}).");
    }
}
