using BuildingBlocks.Domain;
using Ordering.Domain;

namespace Ordering.Domain.Tests;

public class OrderTests
{
    private static OrderLine Line(string sku = "WIDGET-1", int qty = 2, decimal unitPrice = 9.99m) =>
        OrderLine.Create(sku, qty, Money.Of(unitPrice));

    [Fact]
    public void Place_WithNoLines_ThrowsDomainInvariantException()
    {
        Assert.Throws<DomainInvariantException>(() => Order.Place(Guid.NewGuid(), []));
    }

    [Fact]
    public void Place_RaisesOrderPlacedAndStartsPending()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderPlaced>(order.DomainEvents.Single());
    }

    [Fact]
    public void Total_SumsLineTotalsAcrossAllLines()
    {
        var order = Order.Place(Guid.NewGuid(), [Line(qty: 2, unitPrice: 10m), Line(sku: "GADGET-9", qty: 1, unitPrice: 5m)]);

        Assert.Equal(25m, order.Total.Amount);
    }

    [Fact]
    public void Confirm_FromPending_TransitionsToConfirmed()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_Throws()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Confirm();

        Assert.Throws<DomainInvariantException>(() => order.Confirm());
    }

    [Fact]
    public void Cancel_FromShipped_Throws()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Confirm();
        order.MarkPaymentReceived();
        order.MarkShipped();

        Assert.Throws<DomainInvariantException>(() => order.Cancel("too late"));
    }

    [Fact]
    public void Cancel_FromPending_Succeeds()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);

        order.Cancel("customer changed their mind");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
