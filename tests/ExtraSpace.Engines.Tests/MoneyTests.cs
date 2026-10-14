using System.Globalization;
using ExtraSpace.Engines;

namespace ExtraSpace.Engines.Tests;

public sealed class MoneyTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("1.004", "1.00")]
    [InlineData("1.015", "1.02")]
    [InlineData("-1.015", "-1.02")]
    [InlineData("10.999", "11.00")]
    [InlineData("0", "0.00")]
    public void Round_is_half_away_from_zero_to_two_places_for_SGD(string input, string expected) =>
        Money.Round(D(input), "SGD").Should().Be(D(expected));

    [Theory]
    [InlineData("1234.5", "1235")]
    [InlineData("-0.5", "-1")]
    [InlineData("1.4", "1")]
    public void KRW_rounds_to_whole_won(string input, string expected) =>
        Money.Round(D(input), "KRW").Should().Be(D(expected));

    [Fact]
    public void An_unsupported_currency_cannot_be_rounded()
    {
        var round = () => Money.Round(1m, "USD");
        round.Should().Throw<ArgumentException>().WithMessage("*USD*");
    }

    [Fact]
    public void Default_money_has_no_currency()
    {
        var money = default(Money);
        var read = () => money.Currency;
        read.Should().Throw<InvalidOperationException>().WithMessage("*Money.Zero*");
        var add = () => money + new Money(1m, "SGD");
        add.Should().Throw<InvalidOperationException>();
        money.ToString().Should().Be("0.00 (none)");
    }

    [Fact]
    public void ToString_ignores_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            new Money(1234.5m, "SGD").ToString().Should().Be("1234.50 SGD");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Instance_round_keeps_the_currency() =>
        new Money(D("100.005"), "SGD").Round().Should().Be(new Money(D("100.01"), "SGD"));

    [Fact]
    public void Multiplying_does_not_round_until_asked()
    {
        var line = new Money(D("33.335"), "MYR") * 3m;
        line.Amount.Should().Be(D("100.005"));
        line.Round().Amount.Should().Be(D("100.01"));
    }

    [Fact]
    public void Adding_and_subtracting_need_the_same_currency()
    {
        (new Money(1.10m, "SGD") + new Money(2.20m, "SGD")).Should().Be(new Money(3.30m, "SGD"));
        (new Money(1.10m, "SGD") - new Money(2.20m, "SGD")).Should().Be(new Money(-1.10m, "SGD"));
        var mixed = () => new Money(1m, "SGD") + new Money(1m, "MYR");
        mixed.Should().Throw<InvalidOperationException>().WithMessage("*SGD*MYR*");
    }

    [Fact]
    public void Negation_and_zero() =>
        (-new Money(5m, "SGD")).Should().Be(new Money(-5m, "SGD") + Money.Zero("SGD"));

    [Theory]
    [InlineData("sgd")]
    [InlineData("SG")]
    [InlineData("SGDX")]
    [InlineData("S1D")]
    [InlineData("")]
    [InlineData(null)]
    public void Currency_must_be_a_three_letter_upper_case_code(string? currency)
    {
        var create = () => new Money(1m, currency!);
        create.Should().Throw<ArgumentException>();
    }
}
