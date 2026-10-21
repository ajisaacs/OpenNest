using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// What <see cref="NestRenest"/> kept: whether a layout was committed, its physical sheets
/// and utilization, the copies it left unplaced, and one operator-facing note.
/// </summary>
public sealed record NestRenestResult(
    bool Nested,
    int Sheets,
    double Utilization,
    IReadOnlyList<NestUnplacedPart> Unplaced,
    string Note
);

/// <summary>
/// Auto Nest without the dialog, for a nest no one is looking at yet (a released drawing
/// set): every plate is removed, then every drawing's required quantity is nested onto
/// sheets from the nest's plate defaults and plate-size options through
/// <see cref="NestPipeline"/>. A layout is committed only when it is valid and keepable;
/// otherwise the nest is left without plates for the operator to run Auto Nest. Copies the
/// layout leaves unplaced stay as remaining quantity. The caller owns the nest throughout.
/// </summary>
public static class NestRenest
{
    /// <summary>The note left when no layout is kept.</summary>
    public const string NotKeptNote = "Auto Nest could not keep a layout: open it and run Auto Nest.";

    /// <summary>Most physical sheets one renest may use, as the Auto Nest dialog allows.</summary>
    public const int MaxPlates = 100;

    /// <summary>Renests with the engine registered as <paramref name="engineName"/>.</summary>
    public static NestRenestResult Run(Nest nest, string engineName, double minimumSalvageDimension = 0,
        IProgress<NestJobProgress> progress = null, CancellationToken token = default) =>
        Run(nest, NestingEngineRegistry.Create(engineName), engineName, minimumSalvageDimension, progress, token);

    /// <param name="minimumSalvageDimension">
    /// Smallest offcut credited when the nest offers plate-size options, in the nest's units.
    /// </param>
    public static NestRenestResult Run(Nest nest, INestingEngine engine, string engineName,
        double minimumSalvageDimension = 0, IProgress<NestJobProgress> progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(engine);
        token.ThrowIfCancellationRequested();

        nest.Plates.Clear();
        nest.UpdateDrawingQuantities();
        var items = nest.Drawings
            .Where(d => !d.IsCutOff && d.Quantity.Required > 0)
            .Select(d => new NestItem
            {
                Drawing = d,
                Quantity = d.Quantity.Required,
                Priority = d.Priority,
                RotationStart = d.Constraints.StartAngle,
                RotationEnd = d.Constraints.EndAngle,
                StepAngle = d.Constraints.StepAngle,
            })
            .ToList();
        if (items.Count == 0)
            return new NestRenestResult(false, 0, 0, Array.Empty<NestUnplacedPart>(), "Nothing has a quantity to nest.");

        var options = nest.PlateOptions;
        var request = new NestPipelineRequest(engineName, items,
            NestStockBuilder.FromTemplate(nest.PlateDefaults.CreateNew(), options),
            new NestJobOptions(maxPlates: MaxPlates,
                salvageRate: options.Count > 0 ? nest.SalvageRate : 0,
                minimumSalvageDimension: options.Count > 0 ? minimumSalvageDimension : 0));
        var result = NestPipeline.Run(engine, engineName, request, progress, token);
        // Engines that ignore cancellation still have their proposal discarded.
        token.ThrowIfCancellationRequested();
        if (!result.IsValid || !result.CanKeep || result.Plates.All(p => p.Parts.Count == 0))
            return new NestRenestResult(false, 0, 0, result.Unplaced, NotKeptNote);

        var applied = NestPipelineCommit.ApplyToPlates(result, nest.CreatePlate, token: token);
        var sheets = applied.Sum(p => p.Quantity);
        var area = applied.Sum(p => p.Area() * p.Quantity);
        var utilization = area > 0 ? applied.Sum(p => p.Utilization() * p.Area() * p.Quantity) / area : 0;
        return new NestRenestResult(true, sheets, utilization, result.Unplaced, Note(sheets, utilization, result.Unplaced));
    }

    private static string Note(int sheets, double utilization, IReadOnlyList<NestUnplacedPart> unplaced)
    {
        var note = string.Format(CultureInfo.CurrentCulture, "Nested on {0} sheet{1}, {2:P0} used.",
            sheets, sheets == 1 ? "" : "s", utilization);
        if (unplaced.Count == 0)
            return note;
        var copies = unplaced.Sum(u => u.Count);
        return note + $" {copies} cop{(copies == 1 ? "y" : "ies")} not placed, left to nest: "
            + string.Join("; ", unplaced.Select(u => u.Describe())) + ".";
    }
}
