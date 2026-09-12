using System.Diagnostics;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// The one ActivitySource shared by every module's outbox write (capturing the caller's trace
/// context) and OutboxProcessor (replaying it as a parent when the background poll actually
/// publishes). Program.cs registers this exact name with OpenTelemetry
/// (`.AddSource(OutboxTelemetry.SourceName)`) — without that registration these activities are
/// created (Activity.StartActivity returns non-null regardless) but never sampled or exported,
/// so the name has to match exactly in both places.
/// </summary>
public static class OutboxTelemetry
{
    public const string SourceName = "OrderFulfillment.Outbox";

    public static readonly ActivitySource Source = new(SourceName);
}
