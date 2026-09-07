using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application;
using Payments.Domain;

namespace Payments.Infrastructure;

public static class PaymentsModule
{
    public const string ModuleName = "Payments";

    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        services.AddScoped<IPaymentGateway, FakePaymentGateway>();

        // Keyed — see Ordering.Infrastructure.OrderingModule for why.
        services.AddKeyedSingleton<IUnitOfWork, NoOpUnitOfWork>(ModuleName);

        var outbox = new InMemoryOutboxStore(ModuleName);
        services.AddKeyedSingleton<IOutboxWriter>(ModuleName, outbox);
        services.AddSingleton<IOutboxStore>(outbox); // unkeyed: OutboxProcessor enumerates every module's store

        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(sp => new OnOrderConfirmed(
            sp.GetRequiredService<IPaymentGateway>(),
            sp.GetRequiredService<IPaymentRepository>(),
            new OutboxIntegrationEventPublisher(sp.GetRequiredKeyedService<IOutboxWriter>(ModuleName)),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        return services;
    }
}
