namespace BuildingBlocks.Infrastructure;

/// <summary>
/// Where an outbox message lands after it exceeds MaxAttempts without a successful publish, or
/// a consumer repeatedly throws while handling it. Kept out of the OutboxMessage table itself so
/// the poison-message backlog never slows down the query that finds work still worth retrying.
/// A human (or a scheduled reaper) resolves these; they are not retried automatically.
/// </summary>
public class DeadLetterMessage
{
    public Guid Id { get; init; }
    public Guid SourceOutboxMessageId { get; init; }
    public string Type { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public string FailureReason { get; init; } = default!;
    public int AttemptCount { get; init; }
    public DateTimeOffset DeadLetteredOn { get; init; }
}
