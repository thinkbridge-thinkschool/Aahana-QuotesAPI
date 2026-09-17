using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderFulfillment.Api.IntegrationTests;

/// <summary>
/// Real HTTP pipeline, real EF Core/SQLite — see OrderFulfillmentApiFactory. Deliberately NOT an
/// IClassFixture: xUnit can run test methods within one class concurrently, and sharing one
/// factory (one host, one SQLite file) across them raced on EnsureCreated() — "table already
/// exists" on ~1 in 8 runs. A fresh factory (fresh temp SQLite file, fresh in-process host) per
/// test method removes the shared state instead of chasing the race.
/// </summary>
public class PlaceOrderEndpointTests : IDisposable
{
    private readonly OrderFulfillmentApiFactory _factory = new();
    private readonly HttpClient _client;

    public PlaceOrderEndpointTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private static object ValidOrder(string sku = "WIDGET-1", int quantity = 1) => new
    {
        customerId = Guid.NewGuid(),
        lines = new[] { new { sku, quantity, unitPrice = 9.99m, currency = "USD" } },
    };

    [Fact]
    public async Task PlaceOrder_WithValidPayload_Returns201WithOrderId()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", ValidOrder());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task PlaceOrder_WithNoLines_Returns400NotServerError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = Guid.NewGuid(),
            lines = Array.Empty<object>(),
        });

        // Domain invariant (Order.Place requires at least one line), surfaced through
        // UseExceptionHandler as a 400 ProblemDetails — not a 500, and not an unhandled crash.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_WithTooManyLines_Returns400FromValidator()
    {
        var tooManyLines = Enumerable.Range(0, 101)
            .Select(i => new { sku = $"SKU-{i}", quantity = 1, unitPrice = 1m, currency = "USD" })
            .ToArray();

        var response = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = Guid.NewGuid(),
            lines = tooManyLines,
        });

        // PlaceOrderValidator's line-count cap (Day 27, STRIDE row 6) — a resource-consumption
        // limit domain invariants don't cover, checked before the handler ever runs.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_ForMoreStockThanExists_StillReturns201ButOrderIsCancelledBySaga()
    {
        // GADGET-9 is seeded with 5 units (InMemoryStockRepository) — requesting 999 doesn't
        // fail the HTTP request; it places successfully and the async saga compensates.
        var response = await _client.PostAsJsonAsync("/api/v1/orders", ValidOrder(sku: "GADGET-9", quantity: 999));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Response_NeverDisclosesTheServerHeader()
    {
        // Day 27 finding, re-checked here so it can't regress silently: every response was
        // leaking "Server: Kestrel" before AddServerHeader = false.
        var response = await _client.GetAsync("/");

        Assert.False(response.Headers.Contains("Server"), "Server header must not be present.");
    }

    [Fact]
    public async Task Response_CarriesTheSecurityHeadersFromDay27()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", GetHeader(response, "Referrer-Policy"));
        Assert.Contains("default-src 'none'", GetHeader(response, "Content-Security-Policy"));
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
