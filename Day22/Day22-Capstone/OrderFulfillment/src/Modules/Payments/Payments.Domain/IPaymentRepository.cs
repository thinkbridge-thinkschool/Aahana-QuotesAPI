namespace Payments.Domain;

public interface IPaymentRepository
{
    void Add(Payment payment);
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
}
