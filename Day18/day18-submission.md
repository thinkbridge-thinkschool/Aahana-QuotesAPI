# Day 18 — Background jobs

## The BackgroundService that drains a queue

`Day18/BackgroundTaskQueue.cs` — a bounded `Channel<T>`-backed queue (bounded so a burst of enqueues can't grow into unbounded memory; a slow producer awaits instead):

```csharp
public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken);

    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken);
}

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
```

`Day18/QueuedHostedService.cs` — the `BackgroundService` itself:

```csharp
public sealed class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(
        IBackgroundTaskQueue taskQueue,
        ILogger<QueuedHostedService> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queued hosted service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, ValueTask> workItem;

            try
            {
                workItem = await _taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown while waiting for the next item.
                break;
            }

            try
            {
                await workItem(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // One bad job must not take the whole drain loop down -
                // log it and keep processing the rest of the queue.
                _logger.LogError(
                    ex,
                    "Unhandled exception executing a queued work item");
            }
        }

        _logger.LogInformation("Queued hosted service drain loop exited");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Queued hosted service stopping - waiting for the current " +
            "item (if any) to finish, up to the host's shutdown timeout");

        await base.StopAsync(cancellationToken);
    }
}
```

Wired in `Program.cs`:

```csharp
builder.Services.AddSingleton<IBackgroundTaskQueue>(
    new BackgroundTaskQueue(capacity: 100));

builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
});
```

## How it shuts down cleanly

Two cancellation tokens do two different jobs, and conflating them is the usual way this pattern breaks:

- **Waiting for the *next* item** uses `stoppingToken`. If shutdown begins while the loop is idle (blocked in `DequeueAsync`), that throws `OperationCanceledException` immediately, the loop exits the `catch` with `break`, and the service stops right away instead of hanging forever waiting for a job that may never arrive.
- **Running an item already dequeued** uses `CancellationToken.None`, not `stoppingToken`. A job that's mid-flight when shutdown starts gets to finish rather than being torn down half-done — the ASP.NET Core host's own `HostOptions.ShutdownTimeout` (set to 10s here) is the real backstop if a job hangs, not this service cancelling it out from under itself.
- `StopAsync` is overridden only to log; `base.StopAsync` is what actually blocks the host's shutdown sequence until `ExecuteAsync` returns (or the shutdown timeout elapses), which is exactly why the in-flight item gets to finish.

**Verified, not just reasoned about.** I ran the compiled service, enqueued a job with a 5-second simulated delay, and triggered shutdown at the 1-second mark (via `IHostApplicationLifetime.StopApplication()` from a temporary test endpoint, since a backgrounded console process on Windows can't receive a graceful `taskkill` without an attached console — removed from the committed code, this was verification-only). The process didn't actually exit until **6 seconds after the job was enqueued** — proof the in-flight job ran to completion rather than being cut off the moment shutdown began. Log sequence observed:

```
Application is shutting down...
Queued hosted service stopping - waiting for the current item (if any) to finish, up to the host's shutdown timeout
[[the 5-second job actually finishes here]]
Queued hosted service drain loop exited
```

## Hangfire vs. a hosted service — one line

A `BackgroundService`/`IHostedService` is the right tool for "drain this in-process queue for as long as the app is running"; reach for Hangfire instead the moment you need persistence across restarts, retries with backoff, a dashboard, or cron-style recurring jobs — those are exactly the problems a hand-rolled in-memory queue doesn't solve (this one loses every unprocessed job on a crash or restart, since the channel is in-memory only).

## What I learned this session

The subtle part isn't "use a `CancellationToken`" — it's using *two different ones* for two different questions ("should I stop looking for more work?" vs. "should I abandon the work I already picked up?"). Passing `stoppingToken` everywhere, which is the instinctive first draft, silently turns "graceful" shutdown into "whatever was running gets killed mid-write" the first time a job takes longer than an instant.

## What would break this

- The queue is in-memory (`Channel<T>`) — a crash or a redeploy loses every job that was enqueued but not yet dequeued. There's no persistence, no retry, and no visibility into what was lost. That's precisely the gap Hangfire (or a durable queue like Azure Service Bus/Storage Queues) closes.
- If a work item never respects any cancellation at all internally (e.g. a tight CPU-bound loop with no `await` and no cancellation checks), `HostOptions.ShutdownTimeout` won't actually stop it — the host gives up waiting and exits, but the CLR won't forcibly kill an in-progress synchronous block. The timeout bounds how long the host *waits*, not how long the work *can* run.
- `BoundedChannelFullMode.Wait` means a producer that enqueues faster than the single drain loop can consume will block on `EnqueueAsync` indefinitely once the queue is full — there's only one consumer here, so a sustained burst turns into back-pressure on every caller, not just a fuller queue.
