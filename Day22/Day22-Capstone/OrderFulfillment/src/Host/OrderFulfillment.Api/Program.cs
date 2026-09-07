using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Notifications.Infrastructure;
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

builder.Services.AddSingleton<IMessageBus, InProcessMessageBus>();
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

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

app.MapPost("/api/orders", async (PlaceOrderCommand command, PlaceOrderHandler handler, CancellationToken ct) =>
{
    var orderId = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/orders/{orderId}", new { orderId });
});

app.MapGet("/", () => "Order Fulfillment — Day 22 capstone kickoff");

app.Run();
