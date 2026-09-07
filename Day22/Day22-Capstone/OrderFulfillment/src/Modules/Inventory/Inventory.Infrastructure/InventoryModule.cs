using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Infrastructure;
using Inventory.Application;
using Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public static class InventoryModule
{
    public const string ModuleName = "Inventory";

    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddSingleton<IStockRepository, InMemoryStockRepository>();

        // Keyed — see Ordering.Infrastructure.OrderingModule for why these can't be plain
        // AddSingleton<IUnitOfWork, ...>/<IOutboxWriter, ...> registrations.
        services.AddKeyedSingleton<IUnitOfWork, NoOpUnitOfWork>(ModuleName);

        var outbox = new InMemoryOutboxStore(ModuleName);
        services.AddKeyedSingleton<IOutboxWriter>(ModuleName, outbox);
        services.AddSingleton<IOutboxStore>(outbox); // unkeyed: OutboxProcessor enumerates every module's store

        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(sp => new OnOrderPlaced(
            sp.GetRequiredService<IStockRepository>(),
            new OutboxIntegrationEventPublisher(sp.GetRequiredKeyedService<IOutboxWriter>(ModuleName)),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>>(sp => new OnOrderCancelled(
            sp.GetRequiredService<IStockRepository>(),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        return services;
    }
}
