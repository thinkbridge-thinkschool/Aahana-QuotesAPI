using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Tests.Domain;

public class RefreshTokenServiceTests
{
    private static async Task<QuoteDbContext> CreateDbContextAsync(
        SqliteConnection connection)
    {
        await connection.OpenAsync();

        var options =
            new DbContextOptionsBuilder<QuoteDbContext>()
                .UseSqlite(connection)
                .Options;

        var db = new QuoteDbContext(options);

        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User
        {
            Id = 1,
            Email = "test@test.com",
            PasswordHash = "test-hash"
        });

        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task RevokeTokenFamily_revokes_current_token()
    {
        // Arrange
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await using var db =
            await CreateDbContextAsync(connection);

        var token = new RefreshToken
        {
            Token = "token-1",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(
                2026,
                8,
                12,
                12,
                0,
                0,
                TimeSpan.Zero)
        };

        var service =
            new RefreshTokenService(db, clock);

        // Act
        await service.RevokeTokenFamily(token);

        // Assert
        token.RevokedAt.Should().Be(
            clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task RevokeTokenFamily_revokes_replacement_tokens()
    {
        // Arrange
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await using var db =
            await CreateDbContextAsync(connection);

        var first = new RefreshToken
        {
            Token = "token-1",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow,
            ReplacedByToken = "token-2"
        };

        var second = new RefreshToken
        {
            Token = "token-2",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            ReplacedByToken = "token-3"
        };

        var third = new RefreshToken
        {
            Token = "token-3",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.RefreshTokens.AddRange(
            first,
            second,
            third);

        await db.SaveChangesAsync();

        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(
                2026,
                8,
                12,
                12,
                0,
                0,
                TimeSpan.Zero)
        };

        var service =
            new RefreshTokenService(db, clock);

        // Act
        await service.RevokeTokenFamily(first);

        // Assert
        second.RevokedAt.Should().Be(
            clock.UtcNow.UtcDateTime);

        third.RevokedAt.Should().Be(
            clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task RevokeTokenFamily_stops_when_replacement_does_not_exist()
    {
        // Arrange
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await using var db =
            await CreateDbContextAsync(connection);

        var token = new RefreshToken
        {
            Token = "token-1",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            ReplacedByToken = "missing-token"
        };

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(
                2026,
                8,
                12,
                12,
                0,
                0,
                TimeSpan.Zero)
        };

        var service =
            new RefreshTokenService(db, clock);

        // Act
        var act = async () =>
            await service.RevokeTokenFamily(token);

        // Assert
        await act.Should().NotThrowAsync();

        token.RevokedAt.Should().Be(
            clock.UtcNow.UtcDateTime);
    }
}

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}