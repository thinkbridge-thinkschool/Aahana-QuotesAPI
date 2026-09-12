namespace Ordering.Application;

/// <summary>
/// Deliberately separate from OrderLine.Create/Order.Place's domain invariants (Ordering.Domain) —
/// those enforce business correctness (a SKU exists, quantity is positive) and always will, no
/// matter who calls them. This enforces resource-consumption limits instead: an order with
/// 1,000,000 individually-valid lines, or a single line whose "Sku" is a 50MB string, would sail
/// straight through every domain invariant above and still be a genuine denial-of-service vector
/// (OWASP API4:2023, Unrestricted Resource Consumption) — that's an API-boundary concern, not a
/// business rule, so it lives here instead of inside the aggregate.
/// </summary>
public static class PlaceOrderValidator
{
    public const int MaxLines = 100;
    public const int MaxSkuLength = 40;

    public static IReadOnlyList<string> Validate(PlaceOrderCommand command)
    {
        var errors = new List<string>();

        if (command.Lines.Count > MaxLines)
        {
            errors.Add($"An order cannot contain more than {MaxLines} lines (had {command.Lines.Count}).");
        }

        foreach (var line in command.Lines)
        {
            if (!IsValidSkuShape(line.Sku))
            {
                errors.Add($"'{Truncate(line.Sku)}' is not a valid SKU shape (1-{MaxSkuLength} ASCII letters/digits/hyphens).");
            }
        }

        return errors;
    }

    /// <summary>
    /// Walks the SKU as a ReadOnlySpan&lt;char&gt; — no substring, no regex, no LINQ closure —
    /// because this runs over every line of every order placed. A regex or `sku.Any(...)` would
    /// allocate per call; a Span-based char-by-char scan allocates nothing, which matters
    /// precisely because this is the boundary meant to stay cheap even under a hostile,
    /// high-volume request rather than becoming part of the resource-consumption problem itself.
    /// </summary>
    private static bool IsValidSkuShape(string? sku)
    {
        ReadOnlySpan<char> span = sku;

        if (span.IsEmpty || span.Length > MaxSkuLength)
        {
            return false;
        }

        foreach (var c in span)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value.Length <= 20 ? value : string.Concat(value.AsSpan(0, 20), "…");
}
