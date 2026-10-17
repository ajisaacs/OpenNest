using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Bounds shared by the drawing library client and server. The client checks them
/// before sending; the server refuses anything outside them with no state change.
/// </summary>
public static class DrawingLibraryRules
{
    /// <summary>Longest name, customer, material, grade or saved-by text.</summary>
    public const int MaxTextLength = 200;

    public const int MaxNotesLength = 4000;

    /// <summary>Largest accepted drawing package (16 MiB).</summary>
    public const int MaxPackageBytes = 16 * 1024 * 1024;

    public const string PackageTooLargeError = "The drawing package exceeds 16 MiB.";

    public const int DefaultRevisionLimit = 100;

    public const int MaxRevisionLimit = 200;

    /// <summary>Thickness as stored and compared: rounded to four decimals.</summary>
    public static double NormalizeThickness(double thickness) =>
        System.Math.Round(thickness, 4, MidpointRounding.AwayFromZero);

    /// <summary>The first metadata rule <paramref name="record"/> breaks, or null when it may be saved.</summary>
    public static string? GetMetadataError(DrawingLibraryRecord? record)
    {
        if (record is null)
            return "Missing metadata.";
        if (string.IsNullOrWhiteSpace(record.Name))
            return "name is required.";
        return TextError("name", record.Name, MaxTextLength)
            ?? TextError("customer", record.Customer, MaxTextLength)
            ?? TextError("materialName", record.MaterialName, MaxTextLength)
            ?? TextError("materialGrade", record.MaterialGrade, MaxTextLength)
            ?? TextError("savedBy", record.SavedBy, MaxTextLength)
            ?? TextError("notes", record.Notes, MaxNotesLength)
            ?? (!double.IsFinite(record.Thickness) || record.Thickness < 0
                ? "thickness must be a finite number, zero or greater."
                : null)
            ?? (!Enum.IsDefined(record.Units) ? "units must be a supported unit name." : null)
            ?? (!Enum.IsDefined(record.CutType) ? "cutType must be Laser, Waterjet or Plasma." : null);
    }

    /// <summary>The first package rule <paramref name="package"/> breaks, or null when it may be stored.</summary>
    public static string? GetPackageError(ReadOnlySpan<byte> package)
    {
        if (package.Length == 0)
            return "The drawing package is empty.";
        if (package.Length > MaxPackageBytes)
            return PackageTooLargeError;
        // Every ZIP archive, including an empty one, starts with a "PK" record signature.
        if (package.Length < 4 || package[0] != (byte)'P' || package[1] != (byte)'K')
            return "The drawing package is not a ZIP archive.";
        return null;
    }

    internal static string? TextError(string field, string? value, int maxLength)
    {
        if (value is null)
            return $"{field} is required.";
        if (value.Length > maxLength)
            return $"{field} must be at most {maxLength} characters.";
        // SQLite LIKE stops at an embedded NUL; refuse it everywhere for one rule.
        if (value.Contains('\0'))
            return $"{field} must not contain NUL characters.";
        return null;
    }
}
