using Microsoft.Extensions.Options;
using QuotesApi;

namespace QuotesApi.Services;

public class TokenService
{
    private readonly JwtOptions _jwtOptions;

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string Issuer => _jwtOptions.Issuer;

    public string Audience => _jwtOptions.Audience;

    public TimeSpan AccessTokenLifetime =>
        _jwtOptions.AccessTokenLifetime;
}