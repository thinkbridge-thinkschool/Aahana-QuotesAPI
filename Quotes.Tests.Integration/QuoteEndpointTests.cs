using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Quotes.Tests.Integration;

public class QuoteEndpointTests
{
    [Fact]
    public async Task GetQuote_when_quote_does_not_exist_returns_not_found()
    {
        // Arrange
        await using var factory =
            new CustomWebApplicationFactory();

        await factory.StartDatabaseAsync();

        using var client =
            factory.CreateClient();

        // Act
        var response =
            await client.GetAsync(
                "/api/quotes/99999");

        // Assert
        response.StatusCode
            .Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQuote_with_valid_token_returns_created()
    {
        // Arrange
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

        var body = new
        {
            author = "Integration Author",
            text = "Integration test quote"
        };

        var content =
            new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

        // Act
        var response =
            await client.PostAsync(
                "/api/quotes/",
                content);

        // Assert
        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);
    }

    private static string CreateToken()
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    CustomWebApplicationFactory.TestJwtKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Sub,
                "1"),

            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Email,
                "integration@test.com"),

            new System.Security.Claims.Claim(
                "scope",
                "quotes.write")
        };

        var token =
            new JwtSecurityToken(
                issuer: "QuotesApi",
                audience: "QuotesApi",
                claims: claims,
                expires:
                    DateTime.UtcNow.AddMinutes(15),
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}