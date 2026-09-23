using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Opus55;

/// <summary>
/// The objective the engine optimizes: sheet area consumed, less the salvage credit for the
/// single largest full-width or full-length edge offcut the job's options allow. Packing toward
/// one edge (see <see cref="PackAxis"/>) is what makes that offcut large.
/// </summary>
internal static class SheetEconomics
{
    public static double SheetArea(NestPlateStock stock) => stock.Size.Width * stock.Size.Length;

    public static double NetArea(NestJobOptions options, SheetFill fill)
    {
        var area = SheetArea(fill.Stock);
        var minimum = options.MinimumSalvageDimension;
        if (options.SalvageRate <= 0 || minimum <= 0 || fill.Parts.Count == 0)
            return area;

        var work = FrontierPacker.WorkArea(fill.Stock);
        var gap = fill.Stock.PartSpacing;
        var left = fill.Parts.Min(p => p.Left);
        var right = fill.Parts.Max(p => p.Right);
        var bottom = fill.Parts.Min(p => p.Bottom);
        var top = fill.Parts.Max(p => p.Top);
        var offcuts = new[]
        {
            // Box.Length is the X extent, Box.Width the Y extent.
            (work.Length, bottom - work.Bottom - gap),
            (work.Length, work.Top - top - gap),
            (left - work.Left - gap, work.Width),
            (work.Right - right - gap, work.Width),
        };
        var salvage = 0.0;
        foreach (var (a, b) in offcuts)
            if (a >= minimum && b >= minimum)
                salvage = System.Math.Max(salvage, a * b);
        return area - options.SalvageRate * salvage;
    }
}
