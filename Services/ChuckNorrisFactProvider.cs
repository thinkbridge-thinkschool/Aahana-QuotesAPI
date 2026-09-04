using System.Net.Http.Json;

using QuotesApi.Abstractions;

namespace QuotesApi.Services;

// Genuine outbound dependency for the Day 22 resilience exercise: enriches
// a quote with a fact from a free, unauthenticated public API. Not on any
// write path, so it's safe to retry (GET only) and safe to degrade - a
// failure here means "no fact today", not a broken quote.
public sealed class ChuckNorrisFactProvider(HttpClient httpClient)
    : IFunFactProvider
{
    public async Task<string> GetFactAsync(
        CancellationToken cancellationToken)
    {
        var joke = await httpClient.GetFromJsonAsync<ChuckNorrisJoke>(
            "jokes/random",
            cancellationToken);

        return joke?.Value
            ?? throw new InvalidOperationException(
                "Fact API returned an empty response.");
    }

    private sealed record ChuckNorrisJoke(string Value);
}
