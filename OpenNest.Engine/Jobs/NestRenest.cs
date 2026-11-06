using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// What <see cref="NestRenest"/> kept: whether a layout was committed, its physical sheets
/// and utilization, the copies it left unplaced, and one operator-facing note. A staged run
/// (<see cref="NestRenest.RunStagedAsync"/>) lists every copy of a layout it did not keep,
/// the ones that layout placed as <see cref="NestUnplacedReason.ProposalNotKeepable"/>;
/// <see cref="NestRenest.Keep"/> lists the result's own unplaced copies.
/// </summary>
public sealed record NestRenestResult(
    bool Nested,
    int Sheets,
    double Utilization,
    IReadOnlyList<NestUnplacedPart> Unplaced,
    string Note
)
{
    /// <summary>
    /// Each committed plate with the engine stock id it was solved on, in commit order.
    /// </summary>
    public IReadOnlyList<(Plate Plate, string StockId)> Committed { get; init; } =
        Array.Empty<(Plate Plate, string StockId)>();

    /// <summary>
    /// True when a staged run's first stock got a layout that was not valid or not keepable,
    /// so none of it was used (<see cref="NestRenest.RunStagedAsync"/>).
    /// </summary>
    public bool FirstStockNotKept { get; init; }
}

/// <summary>
/// Auto Nest without the dialog, for a nest no one is looking at yet (a released drawing
/// set): every plate is removed, then every drawing's required quantity is nested onto
/// sheets from the nest's plate defaults and plate-size options through
/// <see cref="NestPipeline"/>. A layout is committed only when it is valid and keepable;
/// otherwise the nest is left without plates for the operator to run Auto Nest. Copies the
/// layout leaves unplaced stay as remaining quantity. The caller owns the nest throughout.
/// A caller showing the nest uses <see cref="RunStagedAsync"/> (or <see cref="CreateRequest"/>
/// and <see cref="Keep"/>) to solve off its thread and commit on it.
/// </summary>
public static class NestRenest
{
    /// <summary>The note left when no layout is kept.</summary>
    public const string NotKeptNote = "Auto Nest could not keep a layout: open it and run Auto Nest.";

    /// <summary>The note left when no drawing has a quantity to nest.</summary>
    public const string NothingToNestNote = "Nothing has a quantity to nest.";

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
        double minimumSalvageDimension = 0, IProgress<NestJobProgress> progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(engine);
        token.ThrowIfCancellationRequested();

        nest.Plates.Clear();
        nest.UpdateDrawingQuantities();
        // Solved synchronously: the returned task has already completed.
        return RunStagedAsync(nest, engineName, null, minimumSalvageDimension,
                request => Task.FromResult(
                    NestPipeline.Run(engine, engineName, request, progress, token)),
                kept => NestPipelineCommit.ApplyToPlates(kept, nest.CreatePlate, token: token),
                token)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Nests every drawing's remaining quantity of <paramref name="nest"/>, which has no parts
    /// placed yet, in up to two kept layouts: first on <paramref name="firstStock"/> alone
    /// (finite sheets such as MaterialLedger stock, so they are used before anything else),
    /// then whatever is left on the plate defaults and plate-size options. A stage whose layout
    /// is not valid and keepable commits nothing, so its copies fall to the next stage or stay
    /// remaining, listed as unplaced when no later stage takes them. <paramref name="solve"/>
    /// runs a request; <paramref name="commit"/> attaches a kept result's sheets to the nest's
    /// plates on the nest's own thread and returns them. Attaching the parts counts them as
    /// nested, which is how the next stage asks only for the rest. Each stage may use up to
    /// <see cref="MaxPlates"/> sheets.
    /// </summary>
    public static async Task<NestRenestResult> RunStagedAsync(Nest nest, string engineName,
        IReadOnlyList<PlateOption> firstStock, double minimumSalvageDimension,
        Func<NestPipelineRequest, Task<NestPipelineResult>> solve,
        Func<NestPipelineResult, IReadOnlyList<Plate>> commit, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(commit);
        token.ThrowIfCancellationRequested();

        var applied = new List<Plate>();
        var committed = new List<(Plate Plate, string StockId)>();
        IReadOnlyList<NestUnplacedPart> unplaced = Array.Empty<NestUnplacedPart>();
        var anyRequest = false;
        var firstStockNotKept = false;
        var stages = firstStock is { Count: > 0 }
            ? new[] { firstStock, null }
            : new IReadOnlyList<PlateOption>[] { null };
        foreach (var stock in stages)
        {
            var request = CreateRequest(nest, engineName, minimumSalvageDimension, stock,
                remainingOnly: true);
            if (request == null)
                break;
            anyRequest = true;
            var result = await solve(request);
            // Engines that ignore cancellation still have their proposal discarded.
            token.ThrowIfCancellationRequested();
            unplaced = result.Unplaced;
            if (!IsKeepable(result))
            {
                if (!result.IsValid || !result.CanKeep)
                {
                    // Every copy this stage asked for is left, not just what its layout missed.
                    unplaced = NotKept(request, result);
                    firstStockNotKept |= stock != null;
                }
                continue;
            }
            var plates = commit(result);
            committed.AddRange(StockIds(result, plates));
            applied.AddRange(plates);
        }

        if (!anyRequest)
            return new NestRenestResult(false, 0, 0, Array.Empty<NestUnplacedPart>(),
                NothingToNestNote);
        if (applied.Count == 0)
            return new NestRenestResult(false, 0, 0, unplaced, NotKeptNote)
            {
                FirstStockNotKept = firstStockNotKept,
            };
        return Summarize(applied, unplaced) with
        {
            Committed = committed,
            FirstStockNotKept = firstStockNotKept,
        };
    }

    /// <summary>
    /// The Auto Nest job for every drawing's required quantity of <paramref name="nest"/>, on
    /// sheets from its plate defaults and plate-size options; null when nothing has a quantity.
    /// The nest is not changed, so a caller showing it can solve this off its thread.
    /// </summary>
    public static NestPipelineRequest CreateRequest(Nest nest, string engineName,
        double minimumSalvageDimension = 0) =>
        CreateRequest(nest, engineName, minimumSalvageDimension, null, remainingOnly: false);

    // stock: offered instead of the nest's plate-size options; remainingOnly: copies already
    // placed are not asked for again.
    private static NestPipelineRequest CreateRequest(Nest nest, string engineName,
        double minimumSalvageDimension, IReadOnlyList<PlateOption> stock, bool remainingOnly)
    {
        ArgumentNullException.ThrowIfNull(nest);
        int Wanted(Drawing d) => remainingOnly ? d.Quantity.Remaining : d.Quantity.Required;
        var items = nest.Drawings
            .Where(d => !d.IsCutOff && Wanted(d) > 0)
            .Select(d => new NestItem
            {
                Drawing = d,
                Quantity = Wanted(d),
                Priority = d.Priority,
                RotationStart = d.Constraints.StartAngle,
                RotationEnd = d.Constraints.EndAngle,
                StepAngle = d.Constraints.StepAngle,
            })
            .ToList();
        if (items.Count == 0)
            return null;

        var options = stock ?? nest.PlateOptions;
        return new NestPipelineRequest(engineName, items,
            NestStockBuilder.FromTemplate(nest.PlateDefaults.CreateNew(), options),
            new NestJobOptions(maxPlates: MaxPlates,
                salvageRate: options.Count > 0 ? nest.SalvageRate : 0,
                minimumSalvageDimension: options.Count > 0 ? minimumSalvageDimension : 0)
            {
                // The engine has no machine defaults: a plate's saved cutting kerf, if any.
                CommonLineUsage = nest.CommonLineUsage,
                CommonLineKerf = nest.Plates.Select(p => p.CuttingParameters?.Kerf ?? 0)
                    .FirstOrDefault(k => double.IsFinite(k) && k > 0),
            });
    }

    /// <summary>
    /// Keeps <paramref name="result"/> only when it is valid, keepable and places something:
    /// <paramref name="commit"/> then attaches its sheets to the nest (on the nest's own
    /// thread) and returns them. Otherwise nothing is committed and the note says so.
    /// </summary>
    public static NestRenestResult Keep(NestPipelineResult result, Func<NestPipelineResult, IReadOnlyList<Plate>> commit)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(commit);
        if (!IsKeepable(result))
            return new NestRenestResult(false, 0, 0, result.Unplaced, NotKeptNote);

        var applied = commit(result);
        return Summarize(applied, result.Unplaced) with { Committed = StockIds(result, applied) };
    }

    private static List<(Plate Plate, string StockId)> StockIds(NestPipelineResult result,
        IReadOnlyList<Plate> applied) =>
        NestPipelineCommit.PairWithStock(result, applied)
            .Select(p => (p.Plate, p.Stock.Id))
            .ToList();

    // The copies the layout missed keep their own reasons; the ones it placed were not kept.
    private static IReadOnlyList<NestUnplacedPart> NotKept(NestPipelineRequest request,
        NestPipelineResult result)
    {
        var unplaced = result.Unplaced.ToList();
        foreach (var item in request.Items)
        {
            var listed = unplaced.Where(u => ReferenceEquals(u.Drawing, item.Drawing))
                .Sum(u => u.Count);
            if (item.Quantity > listed)
                unplaced.Add(new NestUnplacedPart(item.Drawing.Name, item.Drawing,
                    item.Quantity - listed, NestUnplacedReason.ProposalNotKeepable));
        }
        return unplaced;
    }

    private static bool IsKeepable(NestPipelineResult result) =>
        result.IsValid && result.CanKeep && result.Plates.Any(p => p.Parts.Count > 0);

    private static NestRenestResult Summarize(IReadOnlyList<Plate> applied,
        IReadOnlyList<NestUnplacedPart> unplaced)
    {
        var sheets = applied.Sum(p => p.Quantity);
        var area = applied.Sum(p => p.Area() * p.Quantity);
        var utilization = area > 0
            ? applied.Sum(p => p.Utilization() * p.Area() * p.Quantity) / area
            : 0;
        return new NestRenestResult(true, sheets, utilization, unplaced,
            Note(sheets, utilization, unplaced));
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
