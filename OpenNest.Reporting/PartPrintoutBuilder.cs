using System.Collections.Immutable;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Reporting;

/// <summary>One part page of a printout. Values are detached from the nest.</summary>
/// <param name="Width">Bounding-box extent along Y.</param>
/// <param name="Length">Bounding-box extent along X.</param>
/// <param name="Area">Net part area (outside minus cutouts).</param>
/// <param name="CutLength">Length of every move the head cuts: cut, break, common-line and lead moves.</param>
/// <param name="PierceCount">Cut runs: each starts with a pierce after a rapid, mark or tab.</param>
/// <param name="Quantity">Required quantity; zero when the nest does not track one.</param>
/// <param name="Weight">Net weight of one part in <see cref="PartPrintoutSnapshot.WeightUnits"/>; null when unknown.</param>
public sealed record PrintoutPart(string Name, string Customer, double Width, double Length, double Area,
    double CutLength, int PierceCount, long Quantity, double? Weight, ReportGeometry Geometry);

/// <summary>A detached value snapshot of the parts to print, one page each.</summary>
public sealed record PartPrintoutSnapshot(string Title, string Material, double Thickness, string Units,
    DateTimeOffset GeneratedAt, ImmutableArray<PrintoutPart> Parts)
{
    /// <summary>
    /// The name the nest machine's config gives this thickness ("16 ga"), shown before the
    /// decimal; empty when it has none. The caller supplies it, as for the nest report.
    /// </summary>
    public string ThicknessName { get; init; } = "";

    /// <summary>"lb" for inch jobs, "kg" for millimeter jobs.</summary>
    public string WeightUnits => Units == "mm" ? "kg" : "lb";
}

public static class PartPrintoutBuilder
{
    /// <summary>
    /// Captures <paramref name="drawings"/> (every non-cutoff drawing of the nest when null) in the
    /// given order. Material and thickness come from the nest, the drawing's own material being the
    /// fallback. <paramref name="catalogDensity"/> is the machine catalog density (lb/in^3) used
    /// when the nest material carries none. Capture synchronously while the drawings are stable.
    /// </summary>
    public static PartPrintoutSnapshot Capture(Nest nest, IEnumerable<Drawing>? drawings, DateTimeOffset generatedAt,
        double catalogDensity = 0)
    {
        ArgumentNullException.ThrowIfNull(nest);
        if (!double.IsFinite(nest.Thickness) || nest.Thickness < 0)
            throw new InvalidOperationException("Nest: thickness must be finite and nonnegative.");

        var selected = (drawings ?? nest.Drawings.Where(drawing => !drawing.IsCutOff)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("There are no drawings to print.");

        var parts = ImmutableArray.CreateBuilder<PrintoutPart>(selected.Count);
        foreach (var drawing in selected)
            parts.Add(CapturePart(drawing, nest, catalogDensity));

        var title = selected.Count == 1 ? $"{selected[0].Name} printout" : $"{nest.Name} part printouts";
        return new PartPrintoutSnapshot(title.Trim(), MaterialName(nest, selected), nest.Thickness,
            nest.Units == Units.Millimeters ? "mm" : "in", generatedAt, parts.MoveToImmutable());
    }

    private static PrintoutPart CapturePart(Drawing drawing, Nest nest, double catalogDensity)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var context = $"Drawing '{drawing.Name}'";
        var geometry = NestReportGeometry.Capture(drawing.Program, new Vector(), context);
        var (cutLength, pierces) = CutMetrics(drawing.Program);
        var area = drawing.Area;
        if (!double.IsFinite(area) || area < 0)
            throw new InvalidOperationException($"{context}: net part area is invalid.");
        var customer = string.IsNullOrWhiteSpace(drawing.Customer) ? nest.Customer : drawing.Customer;
        return new PrintoutPart(drawing.Name ?? "", customer?.Trim() ?? "", geometry.Bounds.Width,
            geometry.Bounds.Length, area, cutLength, pierces,
            System.Math.Max(drawing.Quantity.Required, 0), NestReportBuilder.PartWeight(drawing, nest, catalogDensity),
            geometry);
    }

    /// <summary>
    /// Walks the program as cut. Scribe/etch marks, tab spans and display-only moves are not cut,
    /// so they end a run, and the next cut move starts a new run with its own pierce. A display
    /// perimeter's tail is never cut.
    /// </summary>
    internal static (double Length, int Pierces) CutMetrics(Program program)
    {
        var length = 0.0;
        var pierces = 0;
        var cutting = false;
        foreach (var entity in ConvertProgram.ToGeometry(DisplayPerimeterTail.WithoutTail(program)))
        {
            if (!IsCut(entity.Layer))
            {
                cutting = false;
                continue;
            }
            length += entity.Length;
            if (!cutting)
                pierces++;
            cutting = true;
        }
        return (length, pierces);
    }

    private static bool IsCut(Layer layer) =>
        layer == SpecialLayers.Cut || layer == SpecialLayers.Break
        || layer == SpecialLayers.Leadin || layer == SpecialLayers.Leadout || layer == SpecialLayers.CommonLine;

    /// <summary>The nest material, else the drawings' material when they all share one.</summary>
    private static string MaterialName(Nest nest, IReadOnlyList<Drawing> drawings)
    {
        static string Name(Material? material) => $"{material?.Name} {material?.Grade}".Trim();

        var name = Name(nest.Material);
        if (name.Length > 0)
            return name;
        var names = drawings.Select(drawing => Name(drawing.Material)).Distinct().ToList();
        return names.Count == 1 ? names[0] : "";
    }
}
