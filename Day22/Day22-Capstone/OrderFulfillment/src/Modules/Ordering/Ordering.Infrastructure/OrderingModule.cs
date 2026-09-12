using BuildingBlocks.Application;
using BuildingBlocks.Application.IntegrationEvents;
using BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application;
using Ordering.Domain;

namespace Ordering.Infrastructure;

/// <summary>
/// Everything Host needs to know to wire this module up. Host calls AddOrderingModule() and
/// otherwise has zero knowledge of Ordering's internals — this is the module's public surface
/// at the composition-root level (distinct from the integration events, which are its public
/// surface at the runtime-messaging level).
/// </summary>
public static class OrderingModule
{
    public const string ModuleName = "Ordering";

    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Same connection string setting either way — which provider it needs decides itself
        // from its shape. Azure SQL's is always "Server=tcp:...;Authentication=Active Directory
        // Default;..." (see infra/modules/sql.bicep's sqlConnectionStringNoCredentials output);
        // local dev's default (appsettings.json) is "Data Source=orderfulfillment.db". No
        // password ever appears in either — Active Directory Default resolves through
        // Microsoft.Data.SqlClient's own DefaultAzureCredential integration, using whichever
        // identity is available (the Container App's managed identity when deployed, the
        // developer's own `az login`/Visual Studio session locally).
        var connectionString = configuration.GetConnectionString("OrderFulfillment");
        var isAzureSql = connectionString?.Contains("Server=tcp:", StringComparison.OrdinalIgnoreCase) == true;

        services.AddDbContext<OrderingDbContext>(options =>
        {
            if (isAzureSql)
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        services.AddScoped<IOrderRepository, OrderRepository>();

        // Keyed, not plain AddScoped<IUnitOfWork, ...>: every module registers its own
        // implementation of these BuildingBlocks-shared interfaces, and an unkeyed registration
        // is resolved container-wide as "whichever module registered last" — see DESIGN.md,
        // "A DI lesson learned the hard way" for the bug this caused before it was keyed.
        services.AddKeyedScoped<IUnitOfWork, UnitOfWork>(ModuleName);
        services.AddKeyedScoped<IOutboxWriter, OrderingOutboxWriter>(ModuleName);
        services.AddKeyedScoped<IIntegrationEventPublisher>(ModuleName,
            (sp, key) => new OutboxIntegrationEventPublisher(sp.GetRequiredKeyedService<IOutboxWriter>(key)));

        // Unkeyed and plural on purpose: OutboxProcessor wants every module's store via
        // IEnumerable<IOutboxStore>, which (unlike a single injected dependency) really does
        // aggregate every unkeyed registration rather than picking just the last one.
        services.AddScoped<IOutboxStore, OrderingOutboxStore>();

        services.AddScoped<PlaceOrderHandler>(sp => new PlaceOrderHandler(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<StockReservedIntegrationEvent>>(sp => new OnStockReserved(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<StockReservationFailedIntegrationEvent>>(sp => new OnStockReservationFailed(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<PaymentCapturedIntegrationEvent>>(sp => new OnPaymentCaptured(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<PaymentFailedIntegrationEvent>>(sp => new OnPaymentFailed(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        services.AddScoped<IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>>(sp => new OnShipmentDispatched(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredKeyedService<IIntegrationEventPublisher>(ModuleName),
            sp.GetRequiredKeyedService<IUnitOfWork>(ModuleName)));

        return services;
    }
}
