using System.Collections.Concurrent;

using Day18;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IBackgroundTaskQueue>(
    new BackgroundTaskQueue(capacity: 100));

builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.AddSingleton<
    ConcurrentDictionary<Guid, JobStatus>>();

// How long the host waits, on shutdown, for StopAsync on every
// IHostedService (this one included) to finish before it gives up and
// exits anyway. This is the real backstop for an in-flight job that
// won't finish - not something the BackgroundService itself enforces.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.MapPost("/jobs", (
    IBackgroundTaskQueue taskQueue,
    ConcurrentDictionary<Guid, JobStatus> jobs,
    CancellationToken cancellationToken) =>
{
    var jobId = Guid.NewGuid();

    jobs[jobId] = JobStatus.Queued;

    _ = taskQueue.EnqueueAsync(
        async token =>
        {
            jobs[jobId] = JobStatus.Running;

            // Stand-in for real slow work (an export, an email send, a
            // report). Deliberately not cancelled mid-flight - see the
            // comment on QueuedHostedService for why.
            await Task.Delay(TimeSpan.FromSeconds(5), token);

            jobs[jobId] = JobStatus.Completed;
        },
        cancellationToken);

    return Results.Accepted($"/jobs/{jobId}", new { jobId });
});

app.MapGet("/jobs/{id:guid}", (
    Guid id,
    ConcurrentDictionary<Guid, JobStatus> jobs) =>
{
    return jobs.TryGetValue(id, out var status)
        ? Results.Ok(new { id, status = status.ToString() })
        : Results.NotFound();
});

app.Run();

public enum JobStatus
{
    Queued,
    Running,
    Completed
}
