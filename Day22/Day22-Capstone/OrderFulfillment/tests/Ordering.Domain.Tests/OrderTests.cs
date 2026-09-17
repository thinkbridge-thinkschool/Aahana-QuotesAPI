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

    // Day 28 design review: an independent critique found these transitions were NOT idempotent
    // against outbox at-least-once redelivery — a StockReserved message reprocessed after a
    // consumer crash (between handling it and acknowledging it) would hit this exact path and
    // throw, permanently dead-lettering a perfectly valid order. This test used to assert the
    // throw; it now asserts the fix (see Order.Confirm()'s XML doc).
    [Fact]
    public void Confirm_WhenAlreadyConfirmed_IsIdempotentNoOp()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Confirm();
        order.ClearDomainEvents();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Empty(order.DomainEvents); // redelivery must not re-publish OrderConfirmed
    }

    [Fact]
    public void Confirm_WhenAlreadyPastConfirmed_IsIdempotentNoOp()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Confirm();
        order.MarkPaymentReceived();
        order.ClearDomainEvents();

        order.Confirm(); // a stale/redelivered StockReserved arriving after payment already landed

        Assert.Equal(OrderStatus.PaymentReceived, order.Status);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Confirm_OnACancelledOrder_StillThrows()
    {
        // A genuine conflict (not a redelivery of the same event) must still surface as an error
        // rather than being silently swallowed by the idempotency fix above.
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Cancel("stock reservation failed");

        Assert.Throws<DomainInvariantException>(() => order.Confirm());
    }

    [Fact]
    public void MarkShipped_WhenAlreadyShipped_IsIdempotentNoOp()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Confirm();
        order.MarkPaymentReceived();
        order.MarkShipped();
        order.ClearDomainEvents();

        order.MarkShipped();

        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotentNoOp()
    {
        var order = Order.Place(Guid.NewGuid(), [Line()]);
        order.Cancel("customer changed their mind");
        order.ClearDomainEvents();

        order.Cancel("customer changed their mind");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(order.DomainEvents);
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
