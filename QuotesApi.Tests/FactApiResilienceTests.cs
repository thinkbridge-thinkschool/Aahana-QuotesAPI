using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Polly.CircuitBreaker;

using QuotesApi.Extensions;

using Xunit.Abstractions;

namespace QuotesApi.Tests;

// Day 22: proves the resilience pipeline in HttpResilienceExtensions
// actually opens under sustained failure, fast-fails while open, and
// recovers - against a scripted local handler, not a live dependency, so
// the transitions are deterministic and fast to run.
public class FactApiResilienceTests(ITestOutputHelper output)
{
    private static FactApiResilienceOptions TestOptions() => new(
        ConcurrencyLimit: 5,
        ConcurrencyQueueLimit: 0,
        TotalTimeout: TimeSpan.FromSeconds(5),
        RetryMaxAttempts: 2,
        RetryBaseDelay: TimeSpan.FromMilliseconds(10),
        CircuitBreakerFailureRatio: 0.5,
        CircuitBreakerSamplingDuration: TimeSpan.FromSeconds(10),
        CircuitBreakerMinimumThroughput: 4,
        CircuitBreakerBreakDuration: TimeSpan.FromMilliseconds(600),
        AttemptTimeout: TimeSpan.FromSeconds(2));

    [Fact]
    public async Task CircuitBreaker_Opens_Under_Sustained_Failure_Then_Recovers()
    {
        var handler = new ScriptedHandler { AlwaysFail = true };
        var transitions = new List<string>();
        var options = TestOptions();

        var services = new ServiceCollection();

        void Log(string message) => output.WriteLine(
            $"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        services.AddHttpClient("fact-api-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddFactApiResilience(
                options,
                onOpened: breakDuration =>
                {
                    transitions.Add("opened");
                    Log($"CIRCUIT OPENED for {breakDuration}");
                },
                onHalfOpened: () =>
                {
                    transitions.Add("half-opened");
                    Log("CIRCUIT HALF-OPENED, testing recovery");
                },
                onClosed: () =>
                {
                    transitions.Add("closed");
                    Log("CIRCUIT CLOSED, dependency recovered");
                });

        await using var provider = services.BuildServiceProvider();

        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("fact-api-test");

        Log("--- sustained failures against the fact API ---");

        // Drive sustained failures until the breaker trips. Each call
        // either exhausts its retries and returns a 503, or (once the
        // breaker is already open) throws BrokenCircuitException - both
        // count as "the dependency is failing".
        for (var i = 0;
             i < 6 && !transitions.Contains("opened");
             i++)
        {
            try
            {
                var result = await client.GetAsync(
                    "http://fake/jokes/random");

                Log($"call {i}: {(int)result.StatusCode} " +
                    $"(handler invocations so far: {handler.CallCount})");
            }
            catch (BrokenCircuitException)
            {
                Log($"call {i}: BrokenCircuitException, fast-failed " +
                    $"(handler invocations so far: {handler.CallCount})");
            }
        }

        Assert.Equal(["opened"], transitions);

        var callsWhileOpen = handler.CallCount;

        // Fast-fail while open: no attempt should reach the handler.
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.GetAsync("http://fake/jokes/random"));

        Log($"call while open: BrokenCircuitException, fast-failed " +
            $"(handler invocations: {handler.CallCount}, unchanged: " +
            $"{handler.CallCount == callsWhileOpen})");

        Assert.Equal(callsWhileOpen, handler.CallCount);

        Log("--- dependency recovers, waiting out the break duration ---");

        // Dependency recovers; once the break duration elapses, the next
        // call is a half-open trial that succeeds and closes the circuit.
        handler.AlwaysFail = false;

        await Task.Delay(
            options.CircuitBreakerBreakDuration
                + TimeSpan.FromMilliseconds(300));

        var response = await client.GetAsync(
            "http://fake/jokes/random");

        Log($"recovery call: {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            ["opened", "half-opened", "closed"],
            transitions);
    }

    [Fact]
    public async Task Retry_Only_Applies_To_Idempotent_Methods()
    {
        var handler = new ScriptedHandler { AlwaysFail = true };

        var services = new ServiceCollection();

        services.AddHttpClient("fact-api-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddFactApiResilience(
                // A throughput the single calls below can never reach,
                // so the breaker never trips and can't muddy this test.
                TestOptions() with { CircuitBreakerMinimumThroughput = 1000 });

        await using var provider = services.BuildServiceProvider();

        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("fact-api-test");

        await client.PostAsync(
            "http://fake/jokes/random",
            new StringContent("x"));

        Assert.Equal(1, handler.CallCount);

        handler.CallCount = 0;

        await client.GetAsync("http://fake/jokes/random");

        Assert.True(
            handler.CallCount > 1,
            "an idempotent GET should have been retried");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public bool AlwaysFail { get; set; }

        public int CallCount { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            var statusCode = AlwaysFail
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;

            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
