using System.Collections.Concurrent;
using Shipping.Domain;

namespace Shipping.Infrastructure;

public class InMemoryShipmentRepository : IShipmentRepository
{
    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();

    public void Add(Shipment shipment) => _shipments[shipment.Id] = shipment;
}
