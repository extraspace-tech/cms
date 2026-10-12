namespace ExtraSpace.Engines;

/// <summary>An exact amount in one currency. Rounding happens only where a feature spec says so, through <see cref="Round()"/>.</summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"Currency must be a 3-letter upper-case ISO 4217 code, got '{currency}'.", nameof(currency));
        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Half away from zero to 2 places (S0 spec §4.6). The only money rounding rule in the codebase.</summary>
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public Money Round() => new(Round(Amount), Currency);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, SameCurrency(a, b));
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, SameCurrency(a, b));
    public static Money operator -(Money a) => new(-a.Amount, a.Currency);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);

    private static string SameCurrency(Money a, Money b) =>
        a.Currency == b.Currency ? a.Currency : throw new InvalidOperationException($"Cannot combine {a.Currency} and {b.Currency}.");

    public override string ToString() => $"{Amount:0.00##} {Currency}";
}
