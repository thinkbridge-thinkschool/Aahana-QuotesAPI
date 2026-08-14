using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class CanDeleteOwnQuoteHandlerTests
{
    [Fact]
    public async Task Own_quote_with_name_identifier_succeeds()
    {
        var quote = Quote.Create(
            1,
            "Author",
            "My quote");

        var repository = new FakeQuoteRepository(quote);
        var handler = new CanDeleteOwnQuoteHandler(repository);

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "1")
                },
                "test"));

        var context = new AuthorizationHandlerContext(
            new[] { new CanDeleteOwnQuoteRequirement() },
            user,
            10);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Own_quote_with_sub_claim_succeeds()
    {
        var quote = Quote.Create(
            1,
            "Author",
            "My quote");

        var repository = new FakeQuoteRepository(quote);
        var handler = new CanDeleteOwnQuoteHandler(repository);

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim("sub", "1")
                },
                "test"));

        var context = new AuthorizationHandlerContext(
            new[] { new CanDeleteOwnQuoteRequirement() },
            user,
            10);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Missing_user_id_does_not_succeed()
    {
        var quote = Quote.Create(
            1,
            "Author",
            "My quote");

        var repository = new FakeQuoteRepository(quote);
        var handler = new CanDeleteOwnQuoteHandler(repository);

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                authenticationType: "test"));

        var context = new AuthorizationHandlerContext(
            new[] { new CanDeleteOwnQuoteRequirement() },
            user,
            10);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Nonexistent_quote_does_not_succeed()
    {
        var repository = new FakeQuoteRepository(null);
        var handler = new CanDeleteOwnQuoteHandler(repository);

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "1")
                },
                "test"));

        var context = new AuthorizationHandlerContext(
            new[] { new CanDeleteOwnQuoteRequirement() },
            user,
            10);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Quote_owned_by_another_user_does_not_succeed()
    {
        var quote = Quote.Create(
            2,
            "Other User",
            "Someone else's quote");

        var repository = new FakeQuoteRepository(quote);
        var handler = new CanDeleteOwnQuoteHandler(repository);

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "1")
                },
                "test"));

        var context = new AuthorizationHandlerContext(
            new[] { new CanDeleteOwnQuoteRequirement() },
            user,
            10);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private sealed class FakeQuoteRepository : IQuoteRepository
    {
        private readonly Quote? _quote;

        public FakeQuoteRepository(Quote? quote)
        {
            _quote = quote;
        }

        public Task<Quote?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_quote);
        }

        public Task<IReadOnlyList<Quote>> GetPagedAsync(
            int page,
            int size,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Quote> AddAsync(
            Quote quote,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}