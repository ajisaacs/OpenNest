using System.Globalization;
using System.Security.Cryptography;
using OpenNest;

namespace OpenNest.Data;

/// <summary>A downloaded library package as the caller read it (with <c>DrawingPackage.Read</c>).</summary>
public sealed class LibraryPackage
{
    public LibraryPackage(byte[] bytes, Drawing drawing, Units units, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(drawing);
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Drawing = drawing;
        Units = units;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Lower-case hex SHA-256 of the downloaded bytes.</summary>
    public string Sha256 { get; }

    /// <summary>The package's drawing; the insert adopts this instance as the nest's copy.</summary>
    public Drawing Drawing { get; }

    public Units Units { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public enum LibraryInsertKind
{
    /// <summary>The library drawing becomes a new nest drawing.</summary>
    AddDrawing,

    /// <summary>The nest already holds this exact revision; only its quantity grows.</summary>
    AddQuantity,

    /// <summary>Nothing may change; <see cref="LibraryDrawingInsert.Refusal"/> says why.</summary>
    Refused,
}

/// <summary>
/// Decides how one library drawing revision enters a nest, before anything changes:
/// a new exact copy carrying its library link, more quantity on the copy already there,
/// or a refusal. Material, grade or thickness that differ from the nest still allow the
/// add, but the drawing's cut pins are dropped (D3). Different units, a different revision
/// of the same drawing, or another drawing with the same name refuse it.
/// </summary>
public sealed class LibraryDrawingInsert
{
    private bool _applied;

    private LibraryDrawingInsert(Nest nest, LibraryInsertKind kind, Drawing? drawing, int quantity,
        bool keepPins, IReadOnlyList<string> warnings, string refusal)
    {
        Nest = nest;
        Kind = kind;
        Drawing = drawing;
        Quantity = quantity;
        KeepPins = keepPins;
        Warnings = warnings;
        Refusal = refusal;
    }

    public Nest Nest { get; }

    public LibraryInsertKind Kind { get; }

    /// <summary>The new copy (<see cref="LibraryInsertKind.AddDrawing"/>) or the existing one; null when refused.</summary>
    public Drawing? Drawing { get; }

    /// <summary>The quantity added to the nest's required count.</summary>
    public int Quantity { get; }

    /// <summary>
    /// For a new copy, true when the library drawing's cut pins come with it: the library
    /// material, grade and thickness match the nest's. Otherwise the copy has no pins. An
    /// added quantity leaves the existing copy as it is.
    /// </summary>
    public bool KeepPins { get; }

    /// <summary>Operator-facing notes to show before the add completes.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Why nothing may change; empty unless <see cref="Kind"/> is <see cref="LibraryInsertKind.Refused"/>.</summary>
    public string Refusal { get; }

    public bool IsRefused => Kind == LibraryInsertKind.Refused;

    public static LibraryDrawingInsert Plan(Nest nest, string serverUrl, DrawingLibraryRecord record,
        LibraryPackage package, int quantity)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        var serverKey = ServerUrl.Key(serverUrl);
        if (serverKey.Length == 0)
            throw new ArgumentException("A library insert needs the server the drawing came from.", nameof(serverUrl));

        var label = Describe(record);
        if (!string.Equals(package.Sha256, record.PackageSha256, StringComparison.OrdinalIgnoreCase))
            return Refuse(nest, $"The downloaded package for {label} does not match the library record. Try again.");
        if (package.Units != record.Units)
            return Refuse(nest, $"The package for {label} is drawn in {UnitName(package.Units)}, "
                + $"but its library record says {UnitName(record.Units)}.");
        if (package.Units != nest.Units)
            return Refuse(nest, $"{label} is drawn in {UnitName(package.Units)} and this nest is in "
                + $"{UnitName(nest.Units)}. Converting units is not supported.");

        var link = new DrawingLibraryLink(serverKey, record.Id, record.Revision, record.CutType,
            record.Thickness, record.MaterialName, record.MaterialGrade, record.Units, record.Notes,
            record.PackageSha256.ToLowerInvariant());

        var sameRevision = nest.Drawings.FirstOrDefault(d => d.Library?.IsSameRevision(link) == true);
        if (sameRevision != null)
            return new LibraryDrawingInsert(nest, LibraryInsertKind.AddQuantity, sameRevision, quantity,
                keepPins: false, Array.Empty<string>(), "");

        var otherRevision = nest.Drawings.FirstOrDefault(d => d.Library is { } l
            && l.Id == link.Id && string.Equals(l.ServerKey, link.ServerKey, StringComparison.Ordinal));
        if (otherRevision != null)
            return Refuse(nest, $"This nest already uses revision {otherRevision.Library!.Revision} of this "
                + $"library drawing as '{otherRevision.Name}'. Use Update Drawings to change its revision.");

        // DrawingCollection is keyed by name, so a second drawing with this name would be lost.
        var sameName = nest.Drawings.FirstOrDefault(d => string.Equals(d.Name, record.Name, StringComparison.Ordinal));
        if (sameName != null)
            return Refuse(nest, $"This nest already has a drawing named '{sameName.Name}' "
                + $"({DescribeExisting(sameName)}), so {label} was not added. Rename one of them first.");

        var differences = Differences(nest, record);
        var warnings = package.Warnings.ToList();
        if (differences.Count > 0)
            warnings.Add($"{label} differs from this nest: {string.Join("; ", differences)}. The drawing is "
                + "added, but its drawing cut pins are dropped and the nest's cutting settings apply.");

        var drawing = package.Drawing;
        drawing.Name = record.Name;
        drawing.Customer = record.Customer;
        drawing.Material = new Material(record.MaterialName, record.MaterialGrade, drawing.Material?.Density ?? 0);
        drawing.Quantity.Required = quantity;
        drawing.Library = link;
        // Pins were authored for the library material and thickness; elsewhere the nest's
        // cutting settings apply (D3). The link still records the revision the copy came from.
        if (differences.Count > 0)
            drawing.CutPins.Clear();
        return new LibraryDrawingInsert(nest, LibraryInsertKind.AddDrawing, drawing, quantity,
            keepPins: differences.Count == 0, warnings, "");
    }

    /// <summary>Changes the planned nest; call at most once, and only for an insert that is not refused.</summary>
    public void Apply()
    {
        if (IsRefused)
            throw new InvalidOperationException(Refusal);
        if (_applied)
            throw new InvalidOperationException("This library insert was already applied.");

        if (Kind == LibraryInsertKind.AddQuantity)
        {
            if (!Nest.Drawings.Any(d => ReferenceEquals(d, Drawing)))
                throw new InvalidOperationException($"'{Drawing!.Name}' is no longer in this nest.");
            Drawing!.Quantity.Required += Quantity;
        }
        else if (!Nest.Drawings.Add(Drawing!))
        {
            throw new InvalidOperationException($"This nest already has a drawing named '{Drawing!.Name}'.");
        }

        _applied = true;
    }

    private static LibraryDrawingInsert Refuse(Nest nest, string reason) =>
        new(nest, LibraryInsertKind.Refused, null, 0, keepPins: false, Array.Empty<string>(), reason);

    private static List<string> Differences(Nest nest, DrawingLibraryRecord record)
    {
        var differences = new List<string>();
        var material = nest.Material ?? new Material();
        if (SharedListNames.Key(record.MaterialName) != SharedListNames.Key(material.Name))
            differences.Add($"material {Text(record.MaterialName)} (nest {Text(material.Name)})");
        if (SharedListNames.Key(record.MaterialGrade) != SharedListNames.Key(material.Grade))
            differences.Add($"grade {Text(record.MaterialGrade)} (nest {Text(material.Grade)})");
        var thickness = DrawingLibraryRules.NormalizeThickness(record.Thickness);
        var nestThickness = DrawingLibraryRules.NormalizeThickness(nest.Thickness);
        if (thickness != nestThickness)
            differences.Add($"thickness {Number(thickness)} (nest {Number(nestThickness)})");
        return differences;
    }

    private static string Describe(DrawingLibraryRecord record) =>
        string.IsNullOrWhiteSpace(record.Customer)
            ? $"library drawing '{record.Name}' revision {record.Revision}"
            : $"library drawing '{record.Name}' ({record.Customer}) revision {record.Revision}";

    private static string DescribeExisting(Drawing drawing) =>
        drawing.Library is { } link
            ? $"library revision {link.Revision} from {link.ServerKey}"
            : "not from the drawing library";

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "not set" : value.Trim();

    private static string Number(double value) => value.ToString("0.####", CultureInfo.CurrentCulture);

    private static string UnitName(Units units) => units.ToString().ToLowerInvariant();
}
