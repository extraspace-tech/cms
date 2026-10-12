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
    public void Round_is_half_away_from_zero_to_two_places(string input, string expected) =>
        Money.Round(D(input)).Should().Be(D(expected));

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
