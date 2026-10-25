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
        var (left, bottom, right, top) = MaterialEnvelope(job, sheet);
        return NetSheetArea(job.Options, sheet.Stock, left, bottom, right, top);
    }

    /// <summary>
    /// Tie-break for layouts of equal cost: the largest full-width/full-length edge offcut, as
    /// salvage measures it but whatever the salvage rate. Offcuts must meet a positive
    /// MinimumSalvageDimension; with none, any offcut counts. Larger is more compact. An empty
    /// sheet has none. Never part of the cost, so it cannot outrank a cheaper layout.
    /// </summary>
    public static double CompactnessOffcut(NestJob job, NestJobPlateResult sheet)
    {
        if (sheet.Placements.Count == 0)
            return 0;
        var (left, bottom, right, top) = MaterialEnvelope(job, sheet);
        return LargestOffcut(sheet.Stock, left, bottom, right, top,
            System.Math.Max(0, job.Options.MinimumSalvageDimension));
    }

    /// <summary><see cref="CompactnessOffcut(NestJob, NestJobPlateResult)"/> from an existing
    /// material-only placed-parts envelope.</summary>
    public static double CompactnessOffcut(NestJobOptions options, NestPlateStock stock, Box partsEnvelope) =>
        LargestOffcut(stock, partsEnvelope.Left, partsEnvelope.Bottom, partsEnvelope.Right, partsEnvelope.Top,
            System.Math.Max(0, options.MinimumSalvageDimension));

    /// <summary>
    /// An offcut on a 1e-6 grid, for exact tie-break comparisons: differences below it are
    /// envelope rounding, which must not decide a tie differently across platform math libraries.
    /// </summary>
    public static double TieBreakKey(double offcut) => System.Math.Round(offcut, 6);

    /// <summary>Whole-job <see cref="CompactnessOffcut(NestJob, NestJobPlateResult)"/>, summed over sheets.</summary>
    public static double CompactnessOffcut(NestJob job, NestJobResult result) =>
        RequireFinite(result.Plates.Sum(sheet => CompactnessOffcut(job, sheet)));

    /// <summary>Bounds of the sheet's rotated material contours; scribe/etch marks are excluded.</summary>
    private static (double Left, double Bottom, double Right, double Top) MaterialEnvelope(
        NestJob job, NestJobPlateResult sheet)
    {
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
        return (boxes.Min(b => b.Left), boxes.Min(b => b.Bottom), boxes.Max(b => b.Right), boxes.Max(b => b.Top));
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
        return area - options.SalvageRate * LargestOffcut(stock, left, bottom, right, top, minimum);
    }

    private static double LargestOffcut(
        NestPlateStock stock, double left, double bottom, double right, double top, double minimum) =>
        PlateRemnant.LargestOffcut(stock.WorkArea, stock.PartSpacing, left, bottom, right, top, minimum)
            is { } offcut ? offcut.Length * offcut.Width : 0;

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
