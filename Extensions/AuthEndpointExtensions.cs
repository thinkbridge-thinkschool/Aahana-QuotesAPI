using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        // LOGIN
        app.MapPost(
            "/api/auth/login",
            async (
                LoginRequest request,
                QuoteDbContext db,
                IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                var user = await db.Users
                    .FirstOrDefaultAsync(
                        u => u.Email == request.Email,
                        cancellationToken);

                if (user is null ||
                    !global::BCrypt.Net.BCrypt.Verify(
                        request.Password,
                        user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                var accessToken = CreateAccessToken(
                    user,
                    configuration);

                var refreshToken =
                    GenerateRefreshToken();

                var refreshTokenEntity = new RefreshToken
                {
                    Token = HashToken(refreshToken),
                    UserId = user.Id,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                };

                db.RefreshTokens.Add(
                    refreshTokenEntity);

                await db.SaveChangesAsync(
                    cancellationToken);

                return Results.Ok(new
                {
                    access_token = accessToken,
                    refresh_token = refreshToken,
                    expires_in = 900
                });
            });

        // REFRESH
        app.MapPost(
            "/api/auth/refresh",
            async (
                RefreshRequest request,
                QuoteDbContext db,
                IConfiguration configuration,
                RefreshTokenService refreshTokenService,
                CancellationToken cancellationToken) =>
            {
                var tokenHash = HashToken(
                    request.RefreshToken);

                var refreshToken =
                    await db.RefreshTokens
                        .Include(t => t.User)
                        .FirstOrDefaultAsync(
                            t => t.Token == tokenHash,
                            cancellationToken);

                if (refreshToken is null)
                {
                    return Results.Unauthorized();
                }

                // Reuse detection
                if (refreshToken.RevokedAt is not null)
                {
                    await refreshTokenService.RevokeTokenFamily(
                        refreshToken,
                        cancellationToken);

                    return Results.Unauthorized();
                }

                if (refreshToken.ExpiresAt <= DateTime.UtcNow)
                {
                    return Results.Unauthorized();
                }

                var newRefreshToken =
                    GenerateRefreshToken();

                var newRefreshTokenEntity =
                    new RefreshToken
                    {
                        Token = HashToken(
                            newRefreshToken),

                        UserId = refreshToken.UserId,

                        ExpiresAt =
                            DateTime.UtcNow.AddDays(7)
                    };

                refreshToken.RevokedAt =
                    DateTime.UtcNow;

                refreshToken.ReplacedByToken =
                    newRefreshTokenEntity.Token;

                db.RefreshTokens.Add(
                    newRefreshTokenEntity);

                var newAccessToken =
                    CreateAccessToken(
                        refreshToken.User,
                        configuration);

                await db.SaveChangesAsync(
                    cancellationToken);

                return Results.Ok(new
                {
                    access_token = newAccessToken,
                    refresh_token = newRefreshToken,
                    expires_in = 900
                });
            });

        // LOGOUT
        app.MapPost(
            "/api/auth/logout",
            async (
                RefreshRequest request,
                QuoteDbContext db,
                CancellationToken cancellationToken) =>
            {
                var tokenHash = HashToken(
                    request.RefreshToken);

                var refreshToken =
                    await db.RefreshTokens
                        .FirstOrDefaultAsync(
                            t => t.Token == tokenHash,
                            cancellationToken);

                if (refreshToken is not null &&
                    refreshToken.RevokedAt is null)
                {
                    refreshToken.RevokedAt =
                        DateTime.UtcNow;

                    await db.SaveChangesAsync(
                        cancellationToken);
                }

                return Results.NoContent();
            });

        return app;
    }

    private static string CreateAccessToken(
        User user,
        IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT signing key is not configured.");

        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is not configured.");

        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is not configured.");

        var expires =
            DateTime.UtcNow.AddMinutes(15);

        var claims = new[]
        {
            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Email,
                user.Email),

            new System.Security.Claims.Claim(
                "scope",
                "quotes.write")
        };

        var credentials =
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(
        string token)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(hash);
    }
}