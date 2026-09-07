using BuildingBlocks.Domain;

namespace Shipping.Domain;

public sealed class Shipment : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }
    public string Carrier { get; private set; } = default!;
    public string TrackingNumber { get; private set; } = default!;
    public DateTimeOffset DispatchedOn { get; private set; }

    private Shipment() { }

    public static Shipment Dispatch(Guid orderId, string carrier)
    {
        if (string.IsNullOrWhiteSpace(carrier))
            throw new DomainInvariantException("A shipment requires a carrier.");

        return new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Carrier = carrier,
            TrackingNumber = $"{carrier.ToUpperInvariant()[..Math.Min(3, carrier.Length)]}-{Guid.NewGuid():N}"[..16],
            DispatchedOn = DateTimeOffset.UtcNow,
        };
    }
}
