namespace Inventory.Domain;

public interface IStockRepository
{
    Task<Stock?> GetAsync(string sku, CancellationToken ct = default);

    /// <summary>
    /// Records which SKU/quantity pairs were reserved for a given order, so a later
    /// OrderCancelled can release exactly what that order held — without Inventory needing to
    /// know anything else about the order.
    /// </summary>
    Task RecordReservationAsync(Guid orderId, IReadOnlyCollection<(string Sku, int Quantity)> lines, CancellationToken ct = default);

    Task<IReadOnlyCollection<(string Sku, int Quantity)>> GetReservationAsync(Guid orderId, CancellationToken ct = default);
}
