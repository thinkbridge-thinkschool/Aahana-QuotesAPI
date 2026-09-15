namespace Day19;

public sealed record QuoteEvent(
    Guid EventId,
    int QuoteId,
    string Author,
    string Text,
    bool ForcePoison = false);
