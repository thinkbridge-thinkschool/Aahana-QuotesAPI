using System.Collections.Concurrent;
using Inventory.Domain;

namespace Inventory.Infrastructure;

/// <summary>Singleton in-memory store so reservations survive across the request/handler scopes that touch them.</summary>
public class InMemoryStockRepository : IStockRepository
{
    private readonly ConcurrentDictionary<string, Stock> _stock = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyCollection<(string Sku, int Quantity)>> _reservations = new();

    public InMemoryStockRepository()
    {
        // Seed a couple of SKUs so the flow is runnable out of the box.
        _stock["WIDGET-1"] = Stock.Create("WIDGET-1", 100);
        _stock["GADGET-9"] = Stock.Create("GADGET-9", 5);
    }

    public Task<Stock?> GetAsync(string sku, CancellationToken ct = default) =>
        Task.FromResult(_stock.GetValueOrDefault(sku));

    public Task RecordReservationAsync(Guid orderId, IReadOnlyCollection<(string Sku, int Quantity)> lines, CancellationToken ct = default)
    {
        _reservations[orderId] = lines;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<(string Sku, int Quantity)>> GetReservationAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_reservations.GetValueOrDefault(orderId, []));
}
