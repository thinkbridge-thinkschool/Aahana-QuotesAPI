using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OrderFulfillment.Api.IntegrationTests;

/// <summary>
/// Real HTTP pipeline (TestServer), real SQLite (a fresh temp file per instance — never the
/// dev-loop's own orderfulfillment.db, and never shared between test classes running in
/// parallel), real in-process message bus (no ServiceBus:Namespace configured, so Program.cs
/// falls back to InProcessMessageBus exactly the way local `dotnet run` does — deterministic,
/// no Azure dependency, no network in CI). This is "integration" in the sense the exercise
/// means it: exercising the real ASP.NET Core pipeline + real EF Core/SQLite, not mocking
/// either — the only thing swapped out is the infrastructure that would otherwise require a
/// live Azure subscription to even construct.
/// </summary>
public sealed class OrderFulfillmentApiFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(Path.GetTempPath(), $"orderfulfillment-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFulfillment"] = $"Data Source={DbPath}",
                // Deliberately absent: ServiceBus:Namespace/Topic, Entra:TenantId/Audience,
                // APPLICATIONINSIGHTS_CONNECTION_STRING — same "nothing configured" shape as a
                // developer's own machine, which Program.cs already treats as a first-class,
                // supported mode, not a special test-only path.
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = DbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
