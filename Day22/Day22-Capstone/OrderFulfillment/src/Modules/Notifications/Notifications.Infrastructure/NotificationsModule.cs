using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// No outbox registration here on purpose: Notifications only consumes integration events, it
/// never produces its own, so it has nothing for OutboxProcessor to poll on its behalf.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddSingleton<INotificationLog, InMemoryNotificationLog>();
        services.AddScoped<INotificationSender, ConsoleNotificationSender>();

        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, NotifyOrderPlaced>();
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, NotifyOrderConfirmed>();
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, NotifyOrderCancelled>();
        services.AddScoped<IIntegrationEventHandler<PaymentFailedIntegrationEvent>, NotifyPaymentFailed>();
        services.AddScoped<IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>, NotifyShipmentDispatched>();

        return services;
    }
}
