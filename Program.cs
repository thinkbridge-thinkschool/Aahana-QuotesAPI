using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Services;
using QuotesApi.Abstractions;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry + Azure Application Insights
builder.Services
    .AddOpenTelemetry()
    .UseAzureMonitor(options =>
    {
        options.ConnectionString =
            builder.Configuration[
                "APPLICATIONINSIGHTS_CONNECTION_STRING"];
    })
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
builder.Services.AddSingleton<IClock, SystemClock>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT signing key is not configured.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer is not configured.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience is not configured.");

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