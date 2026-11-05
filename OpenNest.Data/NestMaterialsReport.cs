using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenNest.Data;

/// <summary>
/// The sheets a set of saved nests consume, grouped by material, grade, thickness, units
/// and plate size. Material, grade and thickness come from each nest's server record
/// (metadata-only edits can be newer than the archive); plate sizes and duplicates come
/// from the nest's plates. Plates holding no parts other than cutoffs are not counted:
/// they are never cut.
/// </summary>
public sealed class NestMaterialsReport
{
    private readonly Dictionary<Key, Accumulator> rows = new();
    private readonly List<string> nestNames = new();

    /// <summary>Nests added, in the order added.</summary>
    public IReadOnlyList<string> NestNames => nestNames;

    /// <summary>Adds one nest's plates to the report.</summary>
    public void Add(NestRecord record, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(nest);

        var name = string.IsNullOrWhiteSpace(record.Name) ? nest.Name ?? "" : record.Name;
        nestNames.Add(name);
        foreach (var plate in nest.Plates)
        {
            if (plate.Quantity < 1 || !plate.Parts.Any(part => !part.BaseDrawing.IsCutOff))
                continue;
            var key = new Key(
                (record.Material ?? "").Trim(),
                (record.MaterialGrade ?? "").Trim(),
                System.Math.Round(record.Thickness, 4),
                nest.Units,
                System.Math.Round(plate.Size.Width, 4),
                System.Math.Round(plate.Size.Length, 4));
            if (!rows.TryGetValue(key, out var row))
                rows[key] = row = new Accumulator();
            row.Quantity += plate.Quantity;
            row.Nests.TryAdd(name, 0);
            row.Nests[name] += plate.Quantity;
        }
    }

    /// <summary>One row per material, grade, thickness and sheet size, sorted in that order.</summary>
    public IReadOnlyList<NestMaterialRow> Rows =>
        rows
            .OrderBy(pair => pair.Key.Material, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Grade, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Thickness)
            .ThenBy(pair => pair.Key.Units)
            .ThenBy(pair => pair.Key.Width)
            .ThenBy(pair => pair.Key.Length)
            .Select(pair => new NestMaterialRow(
                pair.Key.Material,
                pair.Key.Grade,
                pair.Key.Thickness,
                pair.Key.Units,
                pair.Key.Width,
                pair.Key.Length,
                pair.Value.Quantity,
                pair.Value.Nests
                    .OrderBy(nest => nest.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(nest => new NestMaterialUse(nest.Key, nest.Value))
                    .ToList()))
            .ToList();

    /// <summary>The rows as CSV with a header line, for a spreadsheet.</summary>
    public string ToCsv(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("Material,Grade,Thickness,Units,Width,Length,Quantity,Nests");
        foreach (var row in Rows)
        {
            builder.AppendJoin(',',
                Csv(row.Material),
                Csv(row.Grade),
                Csv(row.Thickness.ToString("0.####", culture)),
                Csv(UnitName(row.Units)),
                Csv(row.Width.ToString("0.###", culture)),
                Csv(row.Length.ToString("0.###", culture)),
                row.Quantity.ToString(culture),
                Csv(row.NestList));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string UnitName(Units units) => units == Units.Millimeters ? "mm" : "in";

    private static string Csv(string? value)
    {
        value ??= "";
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private readonly record struct Key(
        string Material, string Grade, double Thickness, Units Units, double Width, double Length)
    {
        public bool Equals(Key other) =>
            string.Equals(Material, other.Material, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Grade, other.Grade, StringComparison.OrdinalIgnoreCase)
            && Thickness == other.Thickness && Units == other.Units
            && Width == other.Width && Length == other.Length;

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Material),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Grade),
            Thickness, Units, Width, Length);
    }

    private sealed class Accumulator
    {
        public int Quantity;
        public readonly Dictionary<string, int> Nests = new(StringComparer.OrdinalIgnoreCase);
    }
}

/// <param name="Quantity">Sheets of this size, counting each plate's duplicates.</param>
/// <param name="Nests">The nests that use this sheet and how many each uses.</param>
public sealed record NestMaterialRow(
    string Material,
    string Grade,
    double Thickness,
    Units Units,
    double Width,
    double Length,
    int Quantity,
    IReadOnlyList<NestMaterialUse> Nests)
{
    /// <summary>Nest names, with a count after any nest using more than one sheet.</summary>
    public string NestList => string.Join(", ",
        Nests.Select(nest => nest.Quantity > 1 ? $"{nest.Name} ({nest.Quantity})" : nest.Name));
}

public sealed record NestMaterialUse(string Name, int Quantity);
