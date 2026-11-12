using System.Globalization;

namespace OpenNest.Data;

/// <summary>
/// The drawing library's Filter dialog: a customer and inclusive ranges of thickness, size,
/// holes and saved date, all optional. Thickness and sizes are inches, whatever units each
/// drawing is in. Size and hole limits match only drawings the server has measured (see
/// <see cref="DrawingGeometrySummary"/>). The browse session sends it with every query.
/// </summary>
public sealed record DrawingLibraryFilter
{
    public static DrawingLibraryFilter None { get; } = new();

    /// <summary>Exact customer, compared by <see cref="SharedListNames.Key"/>. Blank means any.</summary>
    public string Customer { get; init; } = "";

    public double? MinThickness { get; init; }

    public double? MaxThickness { get; init; }

    /// <summary>
    /// Whether drawings saved without a thickness (0, which suits any thickness) still match a
    /// thickness range. False also leaves them out when no range is set.
    /// </summary>
    public bool IncludeBlankThickness { get; init; } = true;

    /// <summary>Limits on the longer side of the bounding box.</summary>
    public double? MinLength { get; init; }

    public double? MaxLength { get; init; }

    /// <summary>Limits on the shorter side of the bounding box.</summary>
    public double? MinWidth { get; init; }

    public double? MaxWidth { get; init; }

    public int? MinHoles { get; init; }

    public int? MaxHoles { get; init; }

    /// <summary>First and last day saved, inclusive, in the server's local dates.</summary>
    public DateOnly? SavedFrom { get; init; }

    public DateOnly? SavedTo { get; init; }

    public bool IsEmpty => this == None;

    /// <summary>True when a size or hole limit is set, so only measured drawings can match.</summary>
    public bool NeedsGeometry =>
        MinLength is not null || MaxLength is not null || MinWidth is not null || MaxWidth is not null
        || MinHoles is not null || MaxHoles is not null;

    /// <summary>The first invalid range, or null when the filter may be sent.</summary>
    public string? GetValidationError() =>
        RangeError("thickness", MinThickness, MaxThickness)
        ?? RangeError("length", MinLength, MaxLength)
        ?? RangeError("width", MinWidth, MaxWidth)
        ?? RangeError("holes", MinHoles, MaxHoles)
        ?? (SavedFrom is { } from && SavedTo is { } to && from > to ? "The saved-from date must not be after the saved-to date." : null)
        ?? DrawingLibraryRules.TextError("customer", Customer ?? "", DrawingLibraryRules.MaxTextLength);

    /// <summary>A one-line summary for the library's filter label, for example "Thickness 0.0598-0.1875 in; Holes 2+".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Customer))
            parts.Add($"Customer {Customer.Trim()}");
        if (Range(MinThickness, MaxThickness, " in") is { } thickness)
            parts.Add($"Thickness {thickness}" + (IncludeBlankThickness ? " (or none)" : ""));
        else if (!IncludeBlankThickness)
            parts.Add("Has a thickness");
        if (Range(MinLength, MaxLength, " in") is { } length)
            parts.Add($"Length {length}");
        if (Range(MinWidth, MaxWidth, " in") is { } width)
            parts.Add($"Width {width}");
        if (Range(MinHoles, MaxHoles, "") is { } holes)
            parts.Add($"Holes {holes}");
        if (SavedFrom is not null || SavedTo is not null)
            parts.Add("Saved " + (SavedFrom, SavedTo) switch
            {
                ({ } from, { } to) => $"{Date(from)} to {Date(to)}",
                ({ } from, null) => $"on or after {Date(from)}",
                (null, { } to) => $"on or before {Date(to)}",
                _ => "",
            });
        return string.Join("; ", parts);
    }

    internal static string? RangeError(string name, double? min, double? max)
    {
        if (min is { } low && (!double.IsFinite(low) || low < 0))
            return $"The lowest {name} must be a finite number, zero or greater.";
        if (max is { } high && (!double.IsFinite(high) || high < 0))
            return $"The highest {name} must be a finite number, zero or greater.";
        return min > max ? $"The lowest {name} must not be above the highest." : null;
    }

    internal static string? RangeError(string name, int? min, int? max)
    {
        if (min < 0 || max < 0)
            return $"The {name} limits must be zero or greater.";
        return min > max ? $"The lowest {name} must not be above the highest." : null;
    }

    private static string? Range(double? min, double? max, string unit) => (min, max) switch
    {
        ({ } low, { } high) when low == high => $"{Number(low)}{unit}",
        ({ } low, { } high) => $"{Number(low)}-{Number(high)}{unit}",
        ({ } low, null) => $"{Number(low)}{unit}+",
        (null, { } high) => $"up to {Number(high)}{unit}",
        _ => null,
    };

    private static string? Range(int? min, int? max, string unit) =>
        Range(min is { } low ? low : null, max is { } high ? high : (double?)null, unit);

    private static string Number(double value) => value.ToString("0.####", CultureInfo.CurrentCulture);

    private static string Date(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);
}
