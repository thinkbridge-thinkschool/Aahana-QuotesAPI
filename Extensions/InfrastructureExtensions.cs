using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("QuotesDb")
            ?? "Data Source=quotes.db";

        services.AddDbContext<QuoteDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        services.AddScoped<
            ICollectionRepository,
            CollectionRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<IQuoteFormatter, QuoteFormatter>();

        return services;
    }
}