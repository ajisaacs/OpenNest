namespace OpenNest.Data;

/// <summary>
/// Generated nest names: "N" + two-digit year + "-" + the year's sequence number in an
/// unambiguous base-29 alphabet, padded to three digits with its zero digit ('2').
/// No S (it reads as 5). The sequence runs for the whole year; three digits cover 24,389 nests
/// before a fourth appears.
/// </summary>
public static class NestNames
{
    private const string Base29Digits = "2345679ACDEFGHJKLMNPQRTUVWXYZ";

    /// <summary>Earliest and latest years a sequence may be allocated for.</summary>
    public const int MinYear = 2000, MaxYear = 2099;

    public static string Format(int year, int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        var seq = ToBase29(number).PadLeft(3, Base29Digits[0]);
        return $"N{year % 100:D2}-{seq}";
    }

    /// <summary>
    /// The sequence number of a generated name for <paramref name="year"/>, or false for
    /// any other name (renamed nests, other years, non-canonical padding).
    /// </summary>
    public static bool TryParse(string? name, int year, out int number)
    {
        number = 0;
        var prefix = $"N{year % 100:D2}-";
        if (name is null || name.Length < prefix.Length + 3 || !name.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        long value = 0;
        foreach (var c in name.AsSpan(prefix.Length))
        {
            var digit = Base29Digits.IndexOf(c);
            if (digit < 0)
                return false;
            value = value * Base29Digits.Length + digit;
            if (value > int.MaxValue)
                return false;
        }

        number = (int)value;
        return Format(year, number) == name;
    }

    private static string ToBase29(int value)
    {
        if (value == 0)
            return Base29Digits[0].ToString();

        var result = "";
        while (value > 0)
        {
            result = Base29Digits[value % Base29Digits.Length] + result;
            value /= Base29Digits.Length;
        }
        return result;
    }
}

/// <summary>Wire shape of the nest server's next-name allocation.</summary>
public sealed class NestNameAllocation
{
    public int Year { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
}
