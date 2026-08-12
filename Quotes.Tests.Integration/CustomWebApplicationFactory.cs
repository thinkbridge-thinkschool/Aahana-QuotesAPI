using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using System.Text;

namespace Quotes.Tests.Integration;

public class CustomWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    public const string TestJwtKey =
        "THIS_IS_A_TEST_JWT_KEY_12345678901234567890";

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = TestJwtKey,
                        ["Jwt:Issuer"] = "QuotesApi",
                        ["Jwt:Audience"] = "QuotesApi",

                        ["Entra:TenantId"] =
                            "00000000-0000-0000-0000-000000000000",

                        ["Entra:Audience"] =
                            "api://integration-test"
                    });
            });

        builder.ConfigureServices(
            services =>
            {
                // Replace production database
                services.RemoveAll<
                    DbContextOptions<QuoteDbContext>>();

                services.RemoveAll<QuoteDbContext>();

                _connection =
                    new SqliteConnection(
                        "Data Source=:memory:");

                _connection.Open();

                services.AddDbContext<QuoteDbContext>(
                    options =>
                        options.UseSqlite(_connection));

                // Replace production clock
                services.RemoveAll<IClock>();

                services.AddSingleton<IClock>(
                    new FakeClock());

                // Configure the real InternalJwt handler
                services.PostConfigure<JwtBearerOptions>(
                    "InternalJwt",
                    options =>
                    {
                        options.MapInboundClaims = false;

                        options.RequireHttpsMetadata = false;

                        options.TokenValidationParameters =
                            new TokenValidationParameters
                            {
                                ValidateIssuerSigningKey = true,

                                IssuerSigningKey =
                                    new SymmetricSecurityKey(
                                        Encoding.UTF8.GetBytes(
                                            TestJwtKey)),

                                ValidateIssuer = true,

                                ValidIssuer = "QuotesApi",

                                ValidateAudience = true,

                                ValidAudience = "QuotesApi",

                                ValidateLifetime = true,

                                ClockSkew = TimeSpan.Zero
                            };
                    });
            });
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }

        base.Dispose(disposing);
    }
}

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new DateTimeOffset(
            2026,
            8,
            12,
            12,
            0,
            0,
            TimeSpan.Zero);
}