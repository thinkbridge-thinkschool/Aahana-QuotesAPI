using System.IdentityModel.Tokens.Jwt;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

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
// Azure Monitor is enabled only when a connection string is configured.
var azureMonitorConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!string.IsNullOrWhiteSpace(azureMonitorConnectionString))
{
    builder.Services
        .AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString =
                azureMonitorConnectionString;
        });
}
else
{
    builder.Services.AddOpenTelemetry();
}

// Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddProblemDetails();

// CORS for the Angular dev server (ng serve, default port 4200) plus
// whatever production origins (SWA default hostname, custom domain) are
// configured via Cors:AllowedOrigins.
var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

var corsOrigins = configuredOrigins
    .Append("http://localhost:4200")
    .Distinct()
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularClient", policy =>
    {
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Retry-After");
    });
});

builder.Services.AddInfrastructure(
    builder.Configuration);

builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<TokenService>();

// HTTP client
builder.Services.AddHttpClient("my-service");

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

                    // Entra issues v1 tokens (issuer sts.windows.net) for
                    // app registrations without requestedAccessTokenVersion
                    // set to 2, and v2 tokens (login.microsoftonline.com)
                    // otherwise. EntraJwt's ValidIssuers already accepts
                    // both, so route on either.
                    if (issuer.Contains(
                            "login.microsoftonline.com",
                            StringComparison.OrdinalIgnoreCase) ||
                        issuer.Contains(
                            "sts.windows.net",
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
            
            options.MapInboundClaims = false;

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

app.UseCors("AngularClient");

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