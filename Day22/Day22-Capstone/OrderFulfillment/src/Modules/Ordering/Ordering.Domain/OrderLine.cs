using BuildingBlocks.Domain;

namespace Ordering.Domain;

/// <summary>
/// Local to the Order aggregate — Inventory's Stock aggregate is a separate consistency
/// boundary in a separate module, referenced here only by Sku, never by object reference.
/// </summary>
public sealed class OrderLine : Entity<Guid>
{
    public string Sku { get; private set; } = default!;
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; } = default!;

    public Money LineTotal => Money.Of(UnitPrice.Amount * Quantity, UnitPrice.Currency);

    private OrderLine() { }

    private OrderLine(Guid id, string sku, int quantity, Money unitPrice)
    {
        Id = id;
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public static OrderLine Create(string sku, int quantity, Money unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainInvariantException("An order line requires a SKU.");
        if (quantity <= 0)
            throw new DomainInvariantException("Order line quantity must be positive.");

        return new OrderLine(Guid.NewGuid(), sku, quantity, unitPrice);
    }
}
