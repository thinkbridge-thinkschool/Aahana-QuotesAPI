using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using QuotesApi.Data;
using QuotesApi.Models;

using Xunit;

namespace Quotes.Tests.Integration;

public class QuoteEndpointTests
{
    [Fact]
    public async Task GetQuote_when_quote_does_not_exist_returns_not_found()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        var response =
            await client.GetAsync(
                "/api/quotes/99999");

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQuote_without_token_returns_unauthorized()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        var response =
            await CreateQuoteAsync(
                client,
                "Test Author",
                "Test quote");

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuote_with_wrong_policy_returns_forbidden()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        var token =
            CreateToken();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await CreateQuoteAsync(
                client,
                "Test Author",
                "Test quote");

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateQuote_with_valid_token_returns_created()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        var token =
            CreateToken(
                includeWriteScope: true);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await CreateQuoteAsync(
                client,
                "Integration Author",
                "Integration test quote");

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateQuote_with_expired_token_returns_unauthorized()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        var token =
            CreateToken(
                includeWriteScope: true,
                expiresAt: DateTime.UtcNow.AddMinutes(-5));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await CreateQuoteAsync(
                client,
                "Expired Author",
                "Expired token quote");

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_revoked_token_returns_unauthorized()
    {
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        var firstRawToken =
            "first-refresh-token";

        var secondRawToken =
            "second-refresh-token";

        using (var scope =
            factory.Services.CreateScope())
        {
            var db =
                scope.ServiceProvider
                    .GetRequiredService<QuoteDbContext>();

            var user = new User
            {
                Id = 1,
                Email = "refresh@test.com",
                PasswordHash = "test-hash"
            };

            db.Users.Add(user);

            var firstToken =
                new RefreshToken
                {
                    Token = HashToken(firstRawToken),
                    UserId = user.Id,
                    ExpiresAt =
                        DateTime.UtcNow.AddDays(7),
                    RevokedAt =
                        DateTime.UtcNow,
                    ReplacedByToken =
                        HashToken(secondRawToken)
                };

            var secondToken =
                new RefreshToken
                {
                    Token = HashToken(secondRawToken),
                    UserId = user.Id,
                    ExpiresAt =
                        DateTime.UtcNow.AddDays(7)
                };

            db.RefreshTokens.AddRange(
                firstToken,
                secondToken);

            await db.SaveChangesAsync();
        }

        using var client =
            factory.CreateClient();

        var body =
            new
            {
                refresh_token =
                    firstRawToken
            };

        var content =
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

        var response =
            await client.PostAsync(
                "/api/auth/refresh",
                content);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        using var verifyScope =
            factory.Services.CreateScope();

        var verifyDb =
            verifyScope.ServiceProvider
                .GetRequiredService<QuoteDbContext>();

        var replacement =
            await verifyDb.RefreshTokens
                .SingleAsync(
                    x => x.Token ==
                         HashToken(secondRawToken));

        replacement.RevokedAt
            .Should()
            .NotBeNull();
    }

    private static async Task<HttpResponseMessage>
        CreateQuoteAsync(
            HttpClient client,
            string author,
            string text)
    {
        var body =
            new
            {
                author,
                text
            };

        var content =
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

        return await client.PostAsync(
            "/api/quotes/",
            content);
    }

    private static string CreateToken(
        bool includeWriteScope = false,
        DateTime? expiresAt = null)
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    CustomWebApplicationFactory.TestJwtKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var claims =
            new List<System.Security.Claims.Claim>
            {
                new(
                    JwtRegisteredClaimNames.Sub,
                    "1"),

                new(
                    JwtRegisteredClaimNames.Email,
                    "integration@test.com")
            };

        if (includeWriteScope)
        {
            claims.Add(
                new System.Security.Claims.Claim(
                    "scope",
                    "quotes.write"));
        }

        var token =
            new JwtSecurityToken(
                issuer: "QuotesApi",
                audience: "QuotesApi",
                claims: claims,
                expires:
                    expiresAt ??
                    DateTime.UtcNow.AddMinutes(15),
                signingCredentials:
                    credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    private static string HashToken(
        string token)
    {
        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(hash);
    }
}