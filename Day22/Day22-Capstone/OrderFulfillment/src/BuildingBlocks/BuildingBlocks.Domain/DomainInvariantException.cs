namespace BuildingBlocks.Domain;

/// <summary>
/// Thrown when a caller tries to push an aggregate into a state its own rules forbid.
/// Maps to 400, not 500, at the composition-root boundary — mirrors the convention already
/// used for Quote/Collection in the main QuotesApi project.
/// </summary>
public class DomainInvariantException(string message) : Exception(message);
