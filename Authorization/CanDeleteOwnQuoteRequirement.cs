using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

public sealed class CanDeleteOwnQuoteRequirement
    : IAuthorizationRequirement
{
}