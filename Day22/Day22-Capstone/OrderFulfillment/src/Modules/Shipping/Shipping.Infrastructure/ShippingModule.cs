using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Application;
using Shipping.Domain;

namespace Shipping.Infrastructure;

public static class ShippingModule
{
    public const string ModuleName = "Shipping";

    public static IServiceCollection AddShippingModule(this IServiceCollection services)
    {
        services.AddSingleton<IShipmentRepository, InMemoryShipmentRepository>();

        // Keyed — see Ordering.Infrastructure.OrderingModule for why.
        services.AddKeyedSingleton<IUnitOfWork, NoOpUnitOfWork>(ModuleName);

        var outbox = new InMemoryOutboxStore(ModuleName);
        services.AddKeyedSingleton<IOutboxWriter>(ModuleName, outbox);
        services.AddSingleton<IOutboxStore>(outbox); // unkeyed: OutboxProcessor enumerates every module's store

        services.AddScoped<IIntegrationEventHandler<OrderPaymentReceivedIntegrationEvent>>(sp => new OnOrderPaymentReceived(
            sp.GetRequiredService<IShipmentRepository>(),
            new OutboxIntegrationEventPublisher(sp.GetRequiredKeyedService<IOutboxWriter>(ModuleName)),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        return services;
    }
}
