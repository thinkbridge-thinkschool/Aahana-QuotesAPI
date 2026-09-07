namespace Ordering.Domain;

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid orderId, CancellationToken ct = default);
    void Add(Order order);
}
