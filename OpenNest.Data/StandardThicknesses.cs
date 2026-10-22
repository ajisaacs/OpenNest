using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNest.Data;

/// <summary>
/// The sheet gauges (manufacturers' standard gauge for steel) and common plate
/// fractions offered in thickness boxes, so an operator can pick "16 ga - 0.0598"
/// without knowing the decimal. Typed text still accepts any decimal thickness.
/// </summary>
public static partial class StandardThicknesses
{
    /// <summary>One listed thickness: its name ("16 ga", "1/4\"") and value in inches.</summary>
    public sealed record Entry(string Name, double Inches);

    private const double MillimetersPerInch = 25.4;

    private static readonly (int Gauge, double Inches)[] SteelGauges =
    {
        (3, 0.2391), (4, 0.2242), (5, 0.2092), (6, 0.1943), (7, 0.1793), (8, 0.1644),
        (9, 0.1495), (10, 0.1345), (11, 0.1196), (12, 0.1046), (13, 0.0897), (14, 0.0747),
        (15, 0.0673), (16, 0.0598), (17, 0.0538), (18, 0.0478), (19, 0.0418), (20, 0.0359),
        (21, 0.0329), (22, 0.0299), (23, 0.0269), (24, 0.0239), (25, 0.0209), (26, 0.0179),
    };

    private static readonly string[] PlateFractions =
    {
        "1/16", "1/8", "3/16", "1/4", "5/16", "3/8", "7/16", "1/2", "9/16", "5/8", "11/16",
        "3/4", "7/8", "1", "1-1/8", "1-1/4", "1-3/8", "1-1/2", "1-3/4", "2",
    };

    /// <summary>Gauges thinnest first, then plate fractions thinnest first.</summary>
    public static IReadOnlyList<Entry> Entries { get; } = SteelGauges
        .OrderBy(g => g.Inches)
        .Select(g => new Entry($"{g.Gauge} ga", g.Inches))
        .Concat(PlateFractions.Select(f => new Entry($"{f}\"", ReadInches(f) ?? 0)))
        .ToArray();

    /// <summary>The value of <paramref name="entry"/> in <paramref name="units"/>.</summary>
    public static double ValueIn(Entry entry, Units units) =>
        units == Units.Millimeters ? entry.Inches * MillimetersPerInch : entry.Inches;

    /// <summary>The list text for <paramref name="entry"/>, e.g. "16 ga - 0.0598".</summary>
    public static string Label(Entry entry, Units units) =>
        $"{entry.Name} - {FormatNumber(ValueIn(entry, units), units)}";

    /// <summary>
    /// The text a thickness box shows for <paramref name="value"/>: the listed label when
    /// it matches an entry to the displayed precision, otherwise the plain number.
    /// </summary>
    public static string Format(double value, Units units)
    {
        var entry = Find(value, units);
        return entry != null ? Label(entry, units) : FormatNumber(value, units);
    }

    /// <summary>The entry <paramref name="value"/> matches to the displayed precision, or null.</summary>
    public static Entry? Find(double value, Units units)
    {
        var tolerance = units == Units.Millimeters ? 0.0005 : 0.00005;
        return Entries.FirstOrDefault(e => System.Math.Abs(ValueIn(e, units) - value) < tolerance);
    }

    /// <summary>
    /// Reads a thickness box: a list label, a gauge ("16 ga", "16ga", "#16"), a fraction of
    /// an inch ("3/32", "1-1/4", "1 1/4\"") or a non-negative number. Blank text reads as 0.
    /// </summary>
    public static bool TryParse(string? text, Units units, out double value)
    {
        value = 0;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return true;

        var entry = Entries.FirstOrDefault(e =>
            string.Equals(Label(e, units), trimmed, StringComparison.OrdinalIgnoreCase));

        var gauge = GaugePattern().Match(trimmed);
        if (entry == null && gauge.Success)
            entry = Entries.FirstOrDefault(e => e.Name == $"{gauge.Groups["g"].Value} ga");

        if (entry != null)
        {
            value = ValueIn(entry, units);
            return true;
        }

        if (gauge.Success)
            return false;

        // Fractions are always inches, converted for a millimeter box.
        if (trimmed.Contains('/'))
        {
            if (ReadInches(trimmed) is not double inches)
                return false;
            value = units == Units.Millimeters ? inches * MillimetersPerInch : inches;
            return true;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && double.IsFinite(value) && value >= 0;
    }

    private static string FormatNumber(double value, Units units) =>
        value.ToString(units == Units.Millimeters ? "0.###" : "0.####", CultureInfo.CurrentCulture);

    /// <summary>Reads "3/16", "1-1/4" or "1 1/4\"" as inches; null when not a fraction.</summary>
    private static double? ReadInches(string text)
    {
        var match = FractionPattern().Match(text.Trim());
        if (!match.Success)
            return null;

        var whole = match.Groups["w"].Success ? int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture) : 0;
        if (!match.Groups["n"].Success)
            return whole;

        var numerator = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var denominator = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        return denominator == 0 ? null : whole + (double)numerator / denominator;
    }

    [GeneratedRegex(@"^(?:(?<w>\d{1,3})(?:[\s-]+(?<n>\d{1,4})/(?<d>\d{1,4}))?|(?<n>\d{1,4})/(?<d>\d{1,4}))\s*""?$")]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"^(?:#\s*(?<g>\d+)|(?<g>\d+)\s*(?:ga|gauge|gage)\.?)$", RegexOptions.IgnoreCase)]
    private static partial Regex GaugePattern();
}
