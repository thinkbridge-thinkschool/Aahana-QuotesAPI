using Microsoft.EntityFrameworkCore;
using Ordering.Domain;

namespace Ordering.Infrastructure;

public class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid orderId, CancellationToken ct = default) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public void Add(Order order) => db.Orders.Add(order);
}
