using QuotesApi.Abstractions;

namespace QuotesApi.Infrastructure;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}