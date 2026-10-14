using System.Globalization;

namespace ExtraSpace.Engines;

/// <summary>An exact amount in one currency. Rounding happens only where a feature spec says so, through <see cref="Round()"/>.</summary>
public readonly record struct Money
{
    private readonly string? _currency;

    public decimal Amount { get; }

    /// <summary><c>default(Money)</c> has no currency and throws here; start from <see cref="Zero"/>.</summary>
    public string Currency => _currency ?? throw new InvalidOperationException("default(Money) has no currency; use Money.Zero(currency).");

    public Money(decimal amount, string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"Currency must be a 3-letter upper-case ISO 4217 code, got '{currency}'.", nameof(currency));
        Amount = amount;
        _currency = currency;
    }

    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Half away from zero to the currency's places (SGD/MYR 2, KRW 0; S0 spec §4.6, S1 spec §8.2). The only money rounding rule.</summary>
    public static decimal Round(decimal amount, string currency) =>
        decimal.Round(amount, Currencies.Decimals(currency), MidpointRounding.AwayFromZero);

    public Money Round() => new(Round(Amount, Currency), Currency);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, SameCurrency(a, b));
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, SameCurrency(a, b));
    public static Money operator -(Money a) => new(-a.Amount, a.Currency);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);

    private static string SameCurrency(Money a, Money b) =>
        a.Currency == b.Currency ? a.Currency : throw new InvalidOperationException($"Cannot combine {a.Currency} and {b.Currency}.");

    public override string ToString() => $"{Amount.ToString("0.00##", CultureInfo.InvariantCulture)} {_currency ?? "(none)"}";
}
