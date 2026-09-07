using BuildingBlocks.Domain;

namespace Inventory.Domain;

/// <summary>
/// One row per SKU. A separate aggregate from Order on purpose — inventory has its own
/// consistency boundary (you can't oversell a SKU) that has nothing to do with an order's
/// lifecycle, even though the two are tightly related in the business.
/// </summary>
public sealed class Stock : AggregateRoot<string>
{
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => OnHand - Reserved;

    private Stock() { }

    public static Stock Create(string sku, int onHand)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainInvariantException("Stock requires a SKU.");
        if (onHand < 0)
            throw new DomainInvariantException("On-hand quantity cannot be negative.");

        return new Stock { Id = sku, OnHand = onHand, Reserved = 0 };
    }

    /// <summary>Throws rather than partially reserving — the caller decides how to handle a shortfall per order.</summary>
    public void Reserve(int quantity)
    {
        if (quantity > Available)
            throw new DomainInvariantException($"Cannot reserve {quantity} of {Id}; only {Available} available.");

        Reserved += quantity;
    }

    public void Release(int quantity)
    {
        Reserved = Math.Max(0, Reserved - quantity);
    }
}
