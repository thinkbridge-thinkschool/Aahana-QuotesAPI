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
    public async Task PlaceOrder_ThenGetById_ReturnsTheOrderAndMatchesTheLocationHeader()
    {
        var postResponse = await _client.PostAsJsonAsync("/api/v1/orders", ValidOrder(sku: "WIDGET-1", quantity: 3));
        var location = postResponse.Headers.Location!;

        // The Location header has pointed at this route since Day 22 — this is the first test
        // that actually follows it, rather than just asserting it exists.
        var getResponse = await _client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal(1, order.GetProperty("lines").GetArrayLength());
        Assert.Equal(3, order.GetProperty("lines")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task GetOrder_ForAnUnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var response = await _client.GetAsync("/api");

        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", GetHeader(response, "Referrer-Policy"));
        // Day 32: loosened from 'none' to 'self' once this app started serving the wwwroot demo
        // page — still refuses every third-party origin and every inline script/style.
        Assert.Contains("default-src 'self'", GetHeader(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task DemoPage_AlsoCarriesTheSecurityHeaders()
    {
        // The regression this test exists to catch: UseStaticFiles can short-circuit a request
        // (serve the file, return, never call next()) — ordering the header middleware after it
        // meant every static-file response, including this one, silently skipped the headers
        // entirely. Caught by this exact test the same session the demo page was added.
        var response = await _client.GetAsync("/");

        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
