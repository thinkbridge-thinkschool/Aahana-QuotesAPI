using BuildingBlocks.Domain;

namespace Ordering.Domain;

public sealed class Money : ValueObject
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;

    // EF Core materializes owned instances through this and the private setters above — the
    // Of() factory below is still the only path application code can use to build a valid one.
    private Money() { }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency = "USD")
    {
        if (amount < 0)
            throw new DomainInvariantException("Money amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainInvariantException("Currency must be a 3-letter ISO code.");

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money Zero(string currency = "USD") => Of(0, currency);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
            throw new DomainInvariantException($"Cannot add {other.Currency} to {Currency}.");

        return Of(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
