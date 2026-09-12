using System.Threading.RateLimiting;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using Inventory.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Notifications.Infrastructure;
using OpenTelemetry.Trace;
using Ordering.Application;
using Ordering.Infrastructure;
using Payments.Infrastructure;
using Shipping.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Day 27: input limits (OWASP API4:2023, Unrestricted Resource Consumption) ---
// 64 KB is generous for a JSON order payload (PlaceOrderValidator.MaxLines below caps line count
// independently) — this exists so a client can't exhaust memory/CPU with an enormous request body
// before the app-level validator ever gets a chance to run.
// AddServerHeader = false found and fixed by the manual header check below (Docker's disk-space
// issue ruled out a live ZAP run this pass — see DAY27-SECURITY-PASS.md): every response was
// disclosing `Server: Kestrel`, telling an attacker exactly which web server implementation to
// go look up known CVEs for, for free.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 64 * 1024;
    kestrel.AddServerHeader = false;
});

// A fixed window is enough for a kickoff-scope API with one real write endpoint — no per-user
// tiering, no token-bucket burst allowance. RequireRateLimiting("orders") below is what actually
// applies it; registering the policy alone does nothing to any endpoint.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("orders", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromSeconds(10);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddOpenApi();

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
var entraConfigured = !string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraAudience);

// --- Day 27: close the fail-open gap Day 25 documented and deliberately left open ---
// Unauthenticated-by-default was fine for a kickoff scaffold; it is not fine for anything that
// could plausibly run outside a developer's own machine. Refusing to start is the harden-closed
// half of "harden the OpenAPI surface (auth...)" — a misconfigured deploy should fail loudly at
// startup, not silently serve every endpoint to anyone.
if (!entraConfigured && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Entra:TenantId and Entra:Audience must be configured outside Development — refusing to " +
        "start unauthenticated in a non-Development environment.");
}

if (entraConfigured)
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
// Entra config still legitimately absent here only in Development — local `dotnet run` with
// nothing set keeps working unauthenticated, same as Day 25, just no longer possible by accident
// anywhere else.

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

// --- Day 27: baseline security response headers ---
// Cheap, static, and exactly the kind of thing a ZAP baseline scan flags by default when they're
// missing — nosniff and the frame-ancestors/no-referrer pair cost nothing and close three
// passive-scan findings before the scan even runs (see DAY27-SECURITY-PASS.md's before/after).
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

app.UseRateLimiter();

if (entraConfigured)
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

app.MapOpenApi();

// --- Day 27: /api/v1, not /api — URL-segment versioning. A route group prefix is enough at this
// stage (one version, one consumer: nobody yet) without pulling in a versioning package whose
// content-negotiation/deprecation-header machinery this API doesn't need yet.
var ordersV1 = app.MapGroup("/api/v1/orders").RequireRateLimiting("orders");

var placeOrder = ordersV1.MapPost("/", async (PlaceOrderCommand command, PlaceOrderHandler handler, CancellationToken ct) =>
{
    // Domain invariants (Order.Place, OrderLine.Create, Money.Of) still run inside handler —
    // this only catches the resource-consumption shapes those deliberately don't (see
    // PlaceOrderValidator's own comment).
    var validationErrors = PlaceOrderValidator.Validate(command);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["command"] = [.. validationErrors],
        });
    }

    var orderId = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/v1/orders/{orderId}", new { orderId });
});

if (entraConfigured)
{
    placeOrder.RequireAuthorization();
}

app.MapGet("/", () => "Order Fulfillment — Day 22 capstone kickoff");

app.Run();
