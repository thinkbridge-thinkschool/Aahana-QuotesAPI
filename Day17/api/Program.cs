using Azure.Identity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // In Azure this resolves to the Function App's system-assigned
        // Managed Identity — no client secret is configured or stored
        // anywhere. Locally it falls back to the developer's Azure CLI
        // session for testing only.
        services.AddSingleton(new DefaultAzureCredential());

        services.AddHttpClient("QuotesApi");
    })
    .Build();

host.Run();
