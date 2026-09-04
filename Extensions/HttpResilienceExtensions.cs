using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace QuotesApi.Extensions;

// Tunables for the fact-api resilience pipeline. Kept as a record (rather
// than inlined into Program.cs) so tests can build the exact same pipeline
// with shorter durations and drive it deterministically.
public sealed record FactApiResilienceOptions(
    int ConcurrencyLimit,
    int ConcurrencyQueueLimit,
    TimeSpan TotalTimeout,
    int RetryMaxAttempts,
    TimeSpan RetryBaseDelay,
    double CircuitBreakerFailureRatio,
    TimeSpan CircuitBreakerSamplingDuration,
    int CircuitBreakerMinimumThroughput,
    TimeSpan CircuitBreakerBreakDuration,
    TimeSpan AttemptTimeout)
{
    public static FactApiResilienceOptions Default { get; } = new(
        ConcurrencyLimit: 5,
        ConcurrencyQueueLimit: 0,
        TotalTimeout: TimeSpan.FromSeconds(8),
        RetryMaxAttempts: 3,
        RetryBaseDelay: TimeSpan.FromMilliseconds(200),
        CircuitBreakerFailureRatio: 0.5,
        CircuitBreakerSamplingDuration: TimeSpan.FromSeconds(10),
        CircuitBreakerMinimumThroughput: 4,
        CircuitBreakerBreakDuration: TimeSpan.FromSeconds(15),
        AttemptTimeout: TimeSpan.FromSeconds(3));
}

public static class HttpResilienceExtensions
{
    // Bulkhead -> total timeout -> retry (idempotent only) -> circuit
    // breaker -> per-attempt timeout, the same ordering
    // AddStandardResilienceHandler uses: the outer stages bound how much
    // damage a stuck dependency can do (queue depth, wall-clock budget)
    // before the inner stages ever get a chance to retry or trip.
    public static IHttpResiliencePipelineBuilder AddFactApiResilience(
        this IHttpClientBuilder httpClientBuilder,
        FactApiResilienceOptions options,
        Action<TimeSpan>? onOpened = null,
        Action? onHalfOpened = null,
        Action? onClosed = null)
    {
        return httpClientBuilder.AddResilienceHandler(
            "fact-api",
            builder =>
            {
                // Bulkhead: cap concurrent outbound calls so a stalled
                // dependency can't exhaust the thread/connection pool.
                // Reject over capacity immediately rather than queue -
                // a queued caller would just be a slower failure.
                builder.AddConcurrencyLimiter(
                    options.ConcurrencyLimit,
                    options.ConcurrencyQueueLimit);

                // Total budget across all attempts for one logical call.
                builder.AddTimeout(options.TotalTimeout);

                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = options.RetryBaseDelay,
                    UseJitter = true,
                    ShouldHandle = args =>
                    {
                        var method =
                            args.Context.GetRequestMessage()?.Method;

                        var isIdempotent = method is not null &&
                            (method == HttpMethod.Get ||
                             method == HttpMethod.Head ||
                             method == HttpMethod.Put ||
                             method == HttpMethod.Delete ||
                             method == HttpMethod.Options);

                        if (!isIdempotent)
                        {
                            return ValueTask.FromResult(false);
                        }

                        return ValueTask.FromResult(
                            HttpClientResiliencePredicates.IsTransient(
                                args.Outcome));
                    }
                });

                builder.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio =
                            options.CircuitBreakerFailureRatio,
                        SamplingDuration =
                            options.CircuitBreakerSamplingDuration,
                        MinimumThroughput =
                            options.CircuitBreakerMinimumThroughput,
                        BreakDuration =
                            options.CircuitBreakerBreakDuration,
                        OnOpened = args =>
                        {
                            onOpened?.Invoke(args.BreakDuration);
                            return ValueTask.CompletedTask;
                        },
                        OnHalfOpened = _ =>
                        {
                            onHalfOpened?.Invoke();
                            return ValueTask.CompletedTask;
                        },
                        OnClosed = _ =>
                        {
                            onClosed?.Invoke();
                            return ValueTask.CompletedTask;
                        }
                    });

                // Per-attempt timeout, tighter than the total budget so a
                // single hung attempt can't burn the whole retry budget.
                builder.AddTimeout(options.AttemptTimeout);
            });
    }
}
