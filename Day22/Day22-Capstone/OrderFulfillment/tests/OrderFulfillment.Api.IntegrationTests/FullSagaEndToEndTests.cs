using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain;
using Ordering.Infrastructure;

namespace OrderFulfillment.Api.IntegrationTests;

/// <summary>
/// The one end-to-end test the exercise asks for: not one module, not one HTTP call — the whole
/// cross-module saga, exactly as a real client would see it, driven only through the public API
/// and observed only through state a client could also observe (the order's own status). Uses
/// the real OutboxProcessor on its real 5-second poll timer (BuildingBlocks.Infrastructure/
/// OutboxProcessor.cs) rather than reaching in to trigger it manually — the whole point is to
/// prove the actual async pipeline advances the saga by itself, the same way it would in
/// production. Polls with a generous timeout rather than a fixed sleep, so it's slow when the
/// real poll cadence requires it but never flaky.
/// </summary>
public class FullSagaEndToEndTests : IDisposable
{
    private readonly OrderFulfillmentApiFactory _factory = new();
    private readonly HttpClient _client;

    public FullSagaEndToEndTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PlacingAnOrder_CascadesThroughTheWholeSaga_ToShipped()
    {
        // WIDGET-1 is one of InMemoryStockRepository's two seeded SKUs (100 units) — well within
        // stock, so the happy path, not the compensating-cancel path, is what should play out.
        var response = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = Guid.NewGuid(),
            lines = new[] { new { sku = "WIDGET-1", quantity = 1, unitPrice = 9.99m, currency = "USD" } },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("orderId").GetGuid();

        var finalStatus = await WaitForStatusAsync(orderId, OrderStatus.Shipped, timeout: TimeSpan.FromSeconds(45));

        Assert.Equal(OrderStatus.Shipped, finalStatus);
    }

    [Fact]
    public async Task PlacingAnOrder_ForMoreStockThanExists_CascadesToCancelled_NotStuck()
    {
        // GADGET-9 is seeded with only 5 units — Inventory's reservation fails, and the saga's
        // compensating action (Cancel) should run just as reliably as the happy path does.
        var response = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = Guid.NewGuid(),
            lines = new[] { new { sku = "GADGET-9", quantity = 999, unitPrice = 49.99m, currency = "USD" } },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("orderId").GetGuid();

        // Same generous timeout as the happy-path test, not a shorter one: this test caught a
        // real flake at 20s when run alongside the rest of the suite (every test class starts
        // its own in-process host + a real 5-second-poll OutboxProcessor, and they compete for
        // CPU) — correctness of this test mattered more than shaving a few seconds off it.
        var finalStatus = await WaitForStatusAsync(orderId, OrderStatus.Cancelled, timeout: TimeSpan.FromSeconds(45));

        Assert.Equal(OrderStatus.Cancelled, finalStatus);
    }

    /// <summary>
    /// Reads through the real DbContext, not a test-only endpoint — a client placing a real
    /// order has no API for this either, so this is a test-harness-only shortcut for
    /// *observing* the saga, not a second, easier path into the system under test.
    /// </summary>
    private async Task<OrderStatus?> WaitForStatusAsync(Guid orderId, OrderStatus target, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        OrderStatus? lastSeen = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var order = await db.Orders.FindAsync(orderId);

            lastSeen = order?.Status;
            if (lastSeen == target)
                return lastSeen;

            await Task.Delay(500);
        }

        return lastSeen; // let the assertion report exactly what it was stuck at, not just "timed out"
    }
}
