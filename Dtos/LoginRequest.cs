namespace QuotesApi.Dtos;

public sealed record LoginRequest(
    string Email,
    string Password);