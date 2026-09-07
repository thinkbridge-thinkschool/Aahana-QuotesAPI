using System.Collections.Concurrent;
using Payments.Domain;

namespace Payments.Infrastructure;

public class InMemoryPaymentRepository : IPaymentRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();

    public void Add(Payment payment) => _payments[payment.Id] = payment;

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_payments.Values.FirstOrDefault(p => p.OrderId == orderId));
}
