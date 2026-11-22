#nullable enable
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

// What a decode is asked for and what it gives back, and how sheets and plans are costed.
public sealed partial class IrregularNestingEngine
{
    /// <summary>Relative difference within which two plan or tail costs tie.</summary>
    private const double CostTie = 1e-9;

    /// <summary>How a decode fills each sheet.</summary>
    private enum SheetRule
    {
        /// <summary>The frontier packer alone, offered Lattice blocks.</summary>
        Frontier,

        /// <summary>Like parts packed as groups, where the remaining demand has any.</summary>
        Grouped,

        /// <summary>Full-height bands or strips.</summary>
        Banded,
    }

    /// <summary>
    /// One whole-job decode: the front direction and area exponent of the placement rule, the
    /// per-type weights on it (none outside a seeded search), how each sheet is filled, and for a
    /// frontier fill optional per-type ranks served after priority (a large-first decode ranks
    /// the large types 0 and the rest 1, so the small parts take only the large parts' gaps).
    /// </summary>
    private sealed record Strategy(PackAxis Axis, double Beta, IReadOnlyList<double>? Weights = null,
        SheetRule Rule = SheetRule.Frontier, IReadOnlyList<int>? Ranks = null)
    {
        public static Strategy Grouped(PackAxis axis, IReadOnlyList<double>? weights = null) =>
            new(axis, 1.0, weights, SheetRule.Grouped);

        public static Strategy Banded { get; } = new(PackAxis.X, 1.0, null, SheetRule.Banded);
    }

    /// <summary>One sheet a decode packed on trial, with its net cost.</summary>
    private readonly record struct Trial(SheetFill Fill, double Net);

    /// <summary>A decode's sheets in order, their net cost and why it stopped.</summary>
    private sealed record Run(IReadOnlyList<SheetFill> Sheets, double Net, NestJobStopReason Reason);

    /// <summary>Ranked by unplaced parts, cost, sheet count, then compactness offcut (without salvage
    /// credit every one-sheet plan costs the same); full ties keep the incumbent.</summary>
    private sealed record Plan(IReadOnlyList<SheetFill> Sheets, double Cost, int Unplaced, NestJobStopReason Reason,
        bool Priced, NestJobOptions Options)
    {
        private double Offcut => Sheets.Sum(s => IrregularNestingEngine.Offcut(Options, s));

        public bool IsBetterThan(Plan other)
        {
            if (Unplaced != other.Unplaced)
                return Unplaced < other.Unplaced;
            if (Priced && Cost != other.Cost)
                return Cost < other.Cost;
            var scale = System.Math.Max(1, System.Math.Max(Cost, other.Cost));
            if (System.Math.Abs(Cost - other.Cost) > CostTie * scale)
                return Cost < other.Cost;
            if (Sheets.Count != other.Sheets.Count)
                return Sheets.Count < other.Sheets.Count;
            var offcut = Offcut;
            var otherOffcut = other.Offcut;
            return offcut - otherOffcut > CostTie * System.Math.Max(1, System.Math.Max(offcut, otherOffcut));
        }
    }

    /// <summary>A plan of <paramref name="job"/>, ranked as the job is priced.</summary>
    private static Plan PlanOf(NestJob job, IReadOnlyList<SheetFill> sheets, double cost, int unplaced,
        NestJobStopReason reason) =>
        new(sheets, cost, unplaced, reason, NestJobCost.UsesExplicitCosts(job), job.Options);

    private static double NetCost(NestJobOptions options, SheetFill fill) =>
        fill.Parts.Count == 0 ? NestJobCost.GrossSheetCost(fill.Stock)
            : NestJobCost.NetSheetCost(options, fill.Stock, Envelope(fill));

    /// <summary>Equal-cost tie-break: the fill's <see cref="NestJobCost.CompactnessOffcut(NestJobOptions, NestPlateStock, OpenNest.Geometry.Box)"/>.</summary>
    private static double Offcut(NestJobOptions options, SheetFill fill) =>
        fill.Parts.Count == 0 ? 0 : NestJobCost.CompactnessOffcut(options, fill.Stock, Envelope(fill));

    private static OpenNest.Geometry.Box Envelope(SheetFill fill)
    {
        var left = fill.Parts.Min(p => p.Left);
        var bottom = fill.Parts.Min(p => p.Bottom);
        return new OpenNest.Geometry.Box(left, bottom,
            fill.Parts.Max(p => p.Right) - left, fill.Parts.Max(p => p.Top) - bottom);
    }
}
