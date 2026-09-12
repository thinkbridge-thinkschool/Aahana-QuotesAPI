using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using Inventory.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Notifications.Infrastructure;
using OpenTelemetry.Trace;
using Ordering.Application;
using Ordering.Infrastructure;
using Payments.Infrastructure;
using Shipping.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Every module wires its own DI in one call — Program.cs knows the module list, not a single
// module's internals. This is the entire "modular" half of "modular monolith" made visible.
builder.Services.AddOrderingModule(builder.Configuration);
builder.Services.AddInventoryModule();
builder.Services.AddPaymentsModule();
builder.Services.AddShippingModule();
builder.Services.AddNotificationsModule();

// --- Day 26: OpenTelemetry -> Application Insights ---
// Enabled only when a connection string is configured (same fail-open-to-"no telemetry" pattern
// as QuotesApi's own Program.cs) — the app runs identically with or without Azure configured,
// never refusing to start over an observability setting. AddSource(OutboxTelemetry.SourceName) is
// what makes OutboxProcessor's re-parented activities (BuildingBlocks.Infrastructure/
// OutboxProcessor.cs) actually get sampled and exported — without it those Activity objects are
// still created (they're just plain .NET objects either way) but the OTel SDK ignores them.
var azureMonitorConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

var otelBuilder = builder.Services.AddOpenTelemetry();

if (!string.IsNullOrWhiteSpace(azureMonitorConnectionString))
{
    otelBuilder.UseAzureMonitor(options => options.ConnectionString = azureMonitorConnectionString);
}

// AddEntityFrameworkCoreInstrumentation() has to be explicit — verified by testing this locally
// against real Application Insights: the Azure Monitor Distro's UseAzureMonitor() auto-enables
// ASP.NET Core/HttpClient instrumentation reflectively, but NOT EF Core's, even though the
// package is referenced. Without this line the `dependencies` table stays completely empty —
// every SQL call the API makes is invisible, silently, with no error anywhere. QuotesApi's own
// Program.cs has the exact same gap, never caught because it was never tested against a live
// Application Insights resource either.
otelBuilder.WithTracing(tracing => tracing
    .AddSource(OutboxTelemetry.SourceName)
    .AddEntityFrameworkCoreInstrumentation());

// --- Day 25: Entra ID for app auth ---
// Only two settings needed, and neither is a secret: a tenant ID and an audience (the app
// registration's Application ID URI / client ID) are both public identifiers, not credentials —
// there is nothing here to put behind @secure() in Bicep or reference from Key Vault.
var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraAudience = builder.Configuration["Entra:Audience"];

if (!string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraAudience))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{entraTenantId}/v2.0",
                    $"https://sts.windows.net/{entraTenantId}/",
                ],
                ValidateAudience = true,
                ValidAudience = entraAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
    builder.Services.AddAuthorization();
}
// No Entra config present (e.g. local dev with nothing set) — endpoints stay unauthenticated
// rather than the app refusing to start. Known trade-off: convenient locally, but it means
// Entra:TenantId/Entra:Audience being unset in a real environment fails open, not closed. A
// production hardening pass would make these required in non-Development environments.

// --- Day 25: Managed Identity for the API -> Service Bus path ---
// ServiceBusClient takes a fully-qualified namespace + a TokenCredential — never a connection
// string, never a SharedAccessKey. DefaultAzureCredential resolves to the Container App's own
// managed identity when deployed, and to the developer's own `az login`/Visual Studio/VS Code
// session locally — same code path either way, matching infra/modules/servicebus-access.bicep's
// RBAC grant, which is what actually allows this identity to send/receive at all.
var serviceBusNamespace = builder.Configuration["ServiceBus:Namespace"];
var serviceBusTopic = builder.Configuration["ServiceBus:Topic"];

if (!string.IsNullOrEmpty(serviceBusNamespace) && !string.IsNullOrEmpty(serviceBusTopic))
{
    builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential()));
    builder.Services.AddSingleton<IMessageBus>(sp =>
        new AzureServiceBusMessageBus(sp.GetRequiredService<ServiceBusClient>(), serviceBusTopic));

    // One processor per subscription in modules/servicebus.bicep's subscriptionEventMap — five
    // separate IHostedService registrations (unkeyed, collection-style, same reasoning as
    // IOutboxStore) so the host actually starts all five, not "the last one registered".
    foreach (var subscriptionName in new[] { "Ordering", "Inventory", "Payments", "Shipping", "Notifications" })
    {
        builder.Services.AddSingleton<IHostedService>(sp => new ServiceBusSubscriptionProcessor(
            sp.GetRequiredService<ServiceBusClient>(),
            serviceBusTopic,
            subscriptionName,
            sp,
            sp.GetRequiredService<ILogger<ServiceBusSubscriptionProcessor>>()));
    }
}
else
{
    // Local dev fallback: same IMessageBus contract, in-process dispatch, no Azure identity or
    // network needed at all.
    builder.Services.AddSingleton<IMessageBus, InProcessMessageBus>();
}

builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

if (!string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraAudience))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// EnsureCreated, not Migrate: this kickoff scaffold has no EF Core migrations yet (see
// DESIGN.md — that's explicitly out of scope for this pass). Swap for Migrate() once the first
// migration is added.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.EnsureCreated();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var isDomainError = feature?.Error is DomainInvariantException;

    context.Response.StatusCode = isDomainError ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = isDomainError ? feature!.Error.Message : "An unexpected error occurred." });
}));

var placeOrder = app.MapPost("/api/orders", async (PlaceOrderCommand command, PlaceOrderHandler handler, CancellationToken ct) =>
{
    var orderId = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/orders/{orderId}", new { orderId });
});

if (!string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraAudience))
{
    placeOrder.RequireAuthorization();
}

app.MapGet("/", () => "Order Fulfillment — Day 22 capstone kickoff");

app.Run();
