using BuildingBlocks.Domain;

namespace Payments.Domain;

public enum PaymentStatus
{
    Pending,
    Captured,
    Failed,
}

/// <summary>One per order. Captures the outcome of a single attempt against the external gateway (see Payments.Infrastructure.IPaymentGateway).</summary>
public sealed class Payment : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }

    private Payment() { }

    public static Payment Initiate(Guid orderId, decimal amount, string currency) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Amount = amount,
        Currency = currency,
        Status = PaymentStatus.Pending,
    };

    public void MarkCaptured() => Status = PaymentStatus.Captured;

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
    }
}
