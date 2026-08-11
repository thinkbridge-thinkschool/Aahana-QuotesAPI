using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Dtos;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
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

                var key = configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException(
                        "JWT signing key is not configured.");

                var issuer = configuration["Jwt:Issuer"]
                    ?? throw new InvalidOperationException(
                        "JWT issuer is not configured.");

                var audience = configuration["Jwt:Audience"]
                    ?? throw new InvalidOperationException(
                        "JWT audience is not configured.");

                var expires = DateTime.UtcNow.AddMinutes(15);

                var claims = new[]
                {
                    new Claim(
                        JwtRegisteredClaimNames.Sub,
                        user.Id.ToString()),

                    new Claim(
                        JwtRegisteredClaimNames.Email,
                        user.Email)
                };

                var credentials = new SigningCredentials(
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(key)),
                    SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: issuer,
                    audience: audience,
                    claims: claims,
                    expires: expires,
                    signingCredentials: credentials);

                var accessToken =
                    new JwtSecurityTokenHandler()
                        .WriteToken(token);

                var refreshToken =
                    Convert.ToBase64String(
                        RandomNumberGenerator.GetBytes(64));

                return Results.Ok(new
                {
                    access_token = accessToken,
                    refresh_token = refreshToken,
                    expires_in = 900
                });
            });

        return app;
    }
}