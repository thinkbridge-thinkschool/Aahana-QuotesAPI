using System.Threading.Channels;

namespace Day18;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken);

    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken);
}

// Bounded so a burst of enqueues can't grow this into an unbounded
// in-memory list - a slow producer sees EnqueueAsync await instead of
// the process running out of memory.
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, ValueTask>> _channel;

    public BackgroundTaskQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    public ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
