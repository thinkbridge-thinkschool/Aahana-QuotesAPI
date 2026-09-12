namespace BuildingBlocks.Infrastructure;

/// <summary>
/// A row written in the *same* DB transaction as the aggregate change that caused it, so
/// "the order was placed" and "an OrderPlaced event will eventually be published" either both
/// commit or both roll back. A separate OutboxProcessor polls Unprocessed rows and publishes
/// them to the bus, marking ProcessedOn once the publish itself succeeds (at-least-once, not
/// exactly-once — consumers must be idempotent).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; init; }
    public string Type { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public DateTimeOffset OccurredOn { get; init; }
    public DateTimeOffset? ProcessedOn { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }

    /// <summary>
    /// The W3C traceparent (Activity.Current?.Id) of the HTTP request that caused this write,
    /// captured at write time so OutboxProcessor can later re-parent its own activity under the
    /// *original* request's trace instead of starting a disconnected one — this one field is what
    /// makes "API → worker → DB" one stitched-together trace in Application Insights instead of
    /// two unrelated ones (see Day 26's DESIGN.md).
    /// </summary>
    public string? TraceParent { get; init; }
}
