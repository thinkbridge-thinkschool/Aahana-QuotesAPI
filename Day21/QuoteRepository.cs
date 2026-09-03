namespace Day21;

public sealed record Quote(int Id, string Author, string Text);

// Stands in for a real database - the point of this exercise is the
// caching layer in front of it, not the storage itself. The artificial
// delay simulates a genuinely "hot but slow" read (a join-heavy query,
// a slow downstream call), and DbCallCount is the actual proof: every
// call to GetQuoteAsync represents one real database round trip that
// did or didn't happen.
public sealed class QuoteRepository
{
    private long _dbCallCount;

    public long DbCallCount => Interlocked.Read(ref _dbCallCount);

    public void ResetCallCount() => Interlocked.Exchange(ref _dbCallCount, 0);

    public async Task<Quote> GetQuoteAsync(int id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dbCallCount);

        // A real "hot read" that's slow enough to be worth caching -
        // e.g. an aggregate query or a call to a slow downstream service.
        await Task.Delay(100, cancellationToken);

        return new Quote(id, "Ada Lovelace", "That brain of mine is something more than merely mortal.");
    }
}
