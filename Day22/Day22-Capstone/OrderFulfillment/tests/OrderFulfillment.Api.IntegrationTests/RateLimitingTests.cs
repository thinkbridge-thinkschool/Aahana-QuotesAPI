using System.Net;
using System.Net.Http.Json;

namespace OrderFulfillment.Api.IntegrationTests;

/// <summary>
/// Its own fresh factory (see PlaceOrderEndpointTests for why this isn't an IClassFixture) — a
/// fresh fixed-window limiter that only ever sees this test's own traffic. Day 27 verified this
/// by hand with curl (25 rapid requests -> 20x 201, then 5x 429); this is the same check,
/// automated.
/// </summary>
public class RateLimitingTests : IDisposable
{
    private readonly OrderFulfillmentApiFactory _factory = new();
    private readonly HttpClient _client;

    public RateLimitingTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PlaceOrder_Beyond20RequestsIn10Seconds_StartsReturning429()
    {
        var statusCodes = new List<HttpStatusCode>();

        for (var i = 0; i < 25; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/orders", new
            {
                customerId = Guid.NewGuid(),
                lines = new[] { new { sku = "WIDGET-1", quantity = 1, unitPrice = 9.99m, currency = "USD" } },
            });
            statusCodes.Add(response.StatusCode);
        }

        Assert.Equal(20, statusCodes.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(5, statusCodes.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
