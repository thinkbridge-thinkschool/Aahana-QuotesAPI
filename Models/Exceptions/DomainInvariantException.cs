namespace QuotesApi.Exceptions;

public sealed class DomainInvariantException(string message)
    : Exception(message);