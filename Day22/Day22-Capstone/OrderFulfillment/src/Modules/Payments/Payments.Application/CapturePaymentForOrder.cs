using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using Payments.Domain;

namespace Payments.Application;

/// <summary>Reacts to OrderConfirmedIntegrationEvent — the order has stock reserved, so it's now worth attempting to charge for it.</summary>
public sealed class OnOrderConfirmed(
    IPaymentGateway gateway,
    IPaymentRepository payments,
    IIntegrationEventPublisher events,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public async Task HandleAsync(OrderConfirmedIntegrationEvent @event, CancellationToken ct = default)
    {
        var payment = Payment.Initiate(@event.OrderId, @event.Total, @event.Currency);
        var result = await gateway.CaptureAsync(@event.OrderId, @event.Total, @event.Currency, ct);

        if (result.Succeeded)
        {
            payment.MarkCaptured();
            payments.Add(payment);
            await events.PublishAsync(new PaymentCapturedIntegrationEvent(@event.OrderId, payment.Id), ct);
        }
        else
        {
            payment.MarkFailed(result.FailureReason ?? "Declined");
            payments.Add(payment);
            await events.PublishAsync(new PaymentFailedIntegrationEvent(@event.OrderId, payment.FailureReason!), ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
