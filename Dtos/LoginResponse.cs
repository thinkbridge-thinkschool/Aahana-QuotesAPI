namespace QuotesApi.Dtos;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);