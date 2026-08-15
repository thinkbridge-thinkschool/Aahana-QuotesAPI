using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Serilog;
using Serilog.Context;

using QuotesApi;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Services;
using QuotesApi.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Configuration + IOptions
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

// OpenTelemetry + Azure Application Insights
builder.Services
    .AddOpenTelemetry()
    .UseAzureMonitor()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });

// Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddProblemDetails();

builder.Services.AddInfrastructure(
    builder.Configuration);

builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<IClock, SystemClock>();

// HTTP resilience with Polly
builder.Services
    .AddHttpClient("my-service")
    .AddResilienceHandler("default", resilience =>
    {
        resilience.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        });

        resilience.AddCircuitBreaker(
            new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(30)
            });

        resilience.AddTimeout(
            TimeSpan.FromSeconds(10));
    });

// JWT configuration through JwtOptions
var jwtOptions =
    builder.Configuration
        .GetSection("Jwt")
        .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        "JWT configuration is not configured.");

var jwtKey = jwtOptions.Key;
var jwtIssuer = jwtOptions.Issuer;
var jwtAudience = jwtOptions.Audience;

var entraTenantId = builder.Configuration["Entra:TenantId"]
    ?? throw new InvalidOperationException(
        "Entra tenant ID is not configured.");

var entraAudience = builder.Configuration["Entra:Audience"]
    ?? throw new InvalidOperationException(
        "Entra audience is not configured.");

var entraAuthority =
    $"https://login.microsoftonline.com/{entraTenantId}/v2.0";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme(
        "Smart",
        "Internal JWT or Microsoft Entra ID",
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authorization =
                    context.Request.Headers.Authorization.ToString();

                if (!authorization.StartsWith(
                        "Bearer ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "InternalJwt";
                }

                var token =
                    authorization["Bearer ".Length..].Trim();

                try
                {
                    var handler =
                        new JwtSecurityTokenHandler();

                    if (!handler.CanReadToken(token))
                    {
                        return "InternalJwt";
                    }

                    var jwt =
                        handler.ReadJwtToken(token);

                    var issuer = jwt.Issuer;

                    if (issuer.Contains(
                            "login.microsoftonline.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "EntraJwt";
                    }

                    return "InternalJwt";
                }
                catch
                {
                    return "InternalJwt";
                }
            };
        })
    .AddJwtBearer(
        "InternalJwt",
        options =>
        {
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtKey)),

                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,

                    ValidateAudience = true,
                    ValidAudience = jwtAudience,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
        })
    .AddJwtBearer(
        "EntraJwt",
        options =>
        {
            options.Authority =
                entraAuthority;

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,

                    ValidIssuers = new[]
                    {
                        $"https://login.microsoftonline.com/{entraTenantId}/v2.0",
                        $"https://sts.windows.net/{entraTenantId}/"
                    },

                    ValidateAudience = true,
                    ValidAudience = entraAudience,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
        });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "can-edit-quotes",
        policy => policy.RequireClaim(
            "scope",
            "quotes.write"));
});

builder.Services.AddScoped<
    IAuthorizationHandler,
    CanDeleteOwnQuoteHandler>();

var app = builder.Build();

// Correlation ID for Serilog
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty(
        "TraceId",
        System.Diagnostics.Activity.Current?.TraceId.ToString()
        ?? ctx.TraceIdentifier))
    {
        await next();
    }
});

// Request logging
app.Use(async (ctx, next) =>
{
    Log.Information(
        "Request started {Method} {Path}",
        ctx.Request.Method,
        ctx.Request.Path);

    await next();

    Log.Information(
        "Request completed {StatusCode}",
        ctx.Response.StatusCode);
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<QuoteDbContext>();

    await db.Database.MigrateAsync();
}

app.MapAuthEndpoints();
app.MapQuoteEndpoints();

app.Run();

public partial class Program
{
}