namespace ExtraSpace.Engines;

/// <summary>The currencies our sites use and their minor-unit places (S1 spec §5, §9.1). KRW has none.</summary>
public static class Currencies
{
    private static readonly Dictionary<string, int> Places = new(StringComparer.Ordinal) { ["SGD"] = 2, ["MYR"] = 2, ["KRW"] = 0 };

    public static bool IsSupported(string? currency) => currency is not null && Places.ContainsKey(currency);

    public static int Decimals(string currency) =>
        Places.TryGetValue(currency, out var places) ? places : throw new ArgumentException($"Unsupported currency '{currency}'.", nameof(currency));
}
