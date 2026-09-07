namespace Payments.Application;

/// <summary>
/// The anti-corruption layer boundary: everything this module knows about the external payment
/// provider's API shape stays behind this interface. Payments.Infrastructure's implementation is
/// also where retry/circuit-breaker resilience policies around the real HTTP call would live.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentGatewayResult> CaptureAsync(Guid orderId, decimal amount, string currency, CancellationToken ct = default);
}

public sealed record PaymentGatewayResult(bool Succeeded, string? FailureReason);
