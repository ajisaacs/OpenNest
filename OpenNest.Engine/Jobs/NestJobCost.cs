using System;
using System.Linq;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Generic per-sheet scoring; unspecified costs retain legacy area scores.</summary>
public static class NestJobCost
{
    /// <summary>
    /// Sheet area less SalvageRate times the largest usable full-width/full-length edge offcut.
    /// Both offcut dimensions must meet MinimumSalvageDimension, which must be positive.
    /// Bounds use rotated material contours only; scribe/etch marks never shrink salvage offcuts.
    /// </summary>
    public static double NetSheetArea(NestJob job, NestJobPlateResult sheet)
    {
        var area = sheet.Stock.Size.Width * sheet.Stock.Size.Length;
        var minimum = job.Options.MinimumSalvageDimension;
        if (job.Options.SalvageRate == 0 || minimum <= 0 || sheet.Placements.Count == 0)
            return area;
        var parts = job.Parts.ToDictionary(p => p.Id);
        var boxes = sheet
            .Placements.Select(p =>
            {
                var part = new Part(DrawingJobMapper.CreateDrawing(parts[p.PartId]));
                part.Rotate(p.Rotation);
                part.Location = new OpenNest.Geometry.Vector(p.X, p.Y);
                part.UpdateBounds();
                return NestLayoutCheck.MaterialBounds(part);
            })
            .ToList();
        return NetSheetArea(job.Options, sheet.Stock, boxes.Min(b => b.Left),
            boxes.Min(b => b.Bottom), boxes.Max(b => b.Right), boxes.Max(b => b.Top));
    }

    /// <summary>
    /// Computes net area from an existing placed-parts envelope without rebuilding geometry.
    /// The envelope must contain material only; exclude scribe/etch marks.
    /// Empty sheets should use the sheet overload, which returns their full area.
    /// </summary>
    public static double NetSheetArea(NestJobOptions options, NestPlateStock stock, Box partsEnvelope) =>
        NetSheetArea(options, stock, partsEnvelope.Left, partsEnvelope.Bottom,
            partsEnvelope.Right, partsEnvelope.Top);

    private static double NetSheetArea(
        NestJobOptions options, NestPlateStock stock, double left, double bottom, double right, double top)
    {
        var area = stock.Size.Width * stock.Size.Length;
        var minimum = options.MinimumSalvageDimension;
        if (options.SalvageRate == 0 || minimum <= 0)
            return area;
        var work = stock.WorkArea;
        var gap = stock.PartSpacing;
        var candidates = new[]
        {
            (work.Length, bottom - work.Bottom - gap),
            (work.Length, work.Top - top - gap),
            (left - work.Left - gap, work.Width),
            (work.Right - right - gap, work.Width),
        };
        var salvage = candidates
            .Where(c => c.Item1 >= minimum && c.Item2 >= minimum)
            .Select(c => c.Item1 * c.Item2)
            .DefaultIfEmpty(0)
            .Max();
        return area - options.SalvageRate * salvage;
    }

    public static bool UsesExplicitCosts(NestJob job) =>
        job.Plates.Any(s => s.Quantity != 0 && s.Cost.HasValue);

    public static double RequireFinite(double value) => double.IsFinite(value) ? value
        : throw new OverflowException("Stock cost arithmetic exceeded the finite scoring range.");

    public static double GrossSheetCost(NestPlateStock stock) => RequireFinite(stock.Cost ?? stock.Area);

    public static double NetSheetCost(NestJob job, NestJobPlateResult sheet) =>
        PriceArea(sheet.Stock, NetSheetArea(job, sheet));

    public static double NetSheetCost(NestJobOptions options, NestPlateStock stock, Box envelope) =>
        PriceArea(stock, NetSheetArea(options, stock, envelope));

    private static double PriceArea(NestPlateStock stock, double area) =>
        RequireFinite(stock.Cost is double cost ? cost * (area / stock.Area) : area);

    /// <summary>Largest offered sheet score, charged per unplaced part.</summary>
    public static double UnplacedPartPenalty(NestJob job) =>
        job.Plates.Count == 0 ? 0 : UsesExplicitCosts(job)
            ? job.Plates.Where(s => s.Quantity != 0).Max(GrossSheetCost)
            : job.Plates.Max(s => RequireFinite(s.Area));

    /// <summary>
    /// Sum of net sheet costs plus unplaced quantity times the largest offered sheet score.
    /// Matches the benchmark for a valid run; this method does not validate the result.
    /// </summary>
    public static double Evaluate(NestJob job, NestJobResult result) =>
        RequireFinite(result.Plates.Sum(sheet => NetSheetCost(job, sheet))
        + System.Math.Max(0, job.Parts.Sum(part => part.Quantity)
            - result.Plates.Sum(sheet => sheet.Placements.Count)) * UnplacedPartPenalty(job));
}
