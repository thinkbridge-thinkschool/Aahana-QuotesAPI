using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Repositories;

namespace QuotesApi.Authorization;

public sealed class CanDeleteOwnQuoteHandler
    : AuthorizationHandler<CanDeleteOwnQuoteRequirement, int>
{
    private readonly IQuoteRepository _repository;

    public CanDeleteOwnQuoteHandler(
        IQuoteRepository repository)
    {
        _repository = repository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanDeleteOwnQuoteRequirement requirement,
        int quoteId)
    {
        var userIdClaim = context.User.FindFirst(
            ClaimTypes.NameIdentifier);

        if (userIdClaim is null &&
            context.User.Identity?.IsAuthenticated == true)
        {
            userIdClaim = context.User.FindFirst("sub");
        }

        if (!int.TryParse(
                userIdClaim?.Value,
                out var userId))
        {
            return;
        }

        var quote = await _repository.GetByIdAsync(
            quoteId,
            CancellationToken.None);

        if (quote is null)
        {
            return;
        }

        if (quote.UserId == userId)
        {
            context.Succeed(requirement);
        }
    }
}