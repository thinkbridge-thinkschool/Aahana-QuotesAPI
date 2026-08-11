using QuotesApi.Abstractions;

namespace QuotesApi.Tests;

public class ClockTests
{
    [Fact]
    public void FakeClock_Returns_Fixed_Time()
    {
        var expected = new DateTimeOffset(
            2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        IClock clock = new FakeClock
        {
            UtcNow = expected
        };

        Assert.Equal(expected, clock.UtcNow);
    }
}

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}