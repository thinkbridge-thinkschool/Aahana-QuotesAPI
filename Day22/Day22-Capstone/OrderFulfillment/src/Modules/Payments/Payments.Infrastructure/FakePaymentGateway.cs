using Payments.Application;

namespace Payments.Infrastructure;

/// <summary>
/// Stand-in for a real gateway SDK/HTTP client (Stripe, Adyen, ...). Always succeeds — it exists
/// to prove the ACL seam compiles and is where a real client, plus its resilience policy, would
/// be dropped in without Payments.Application or Payments.Domain changing at all.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<PaymentGatewayResult> CaptureAsync(Guid orderId, decimal amount, string currency, CancellationToken ct = default) =>
        Task.FromResult(new PaymentGatewayResult(true, null));
}
