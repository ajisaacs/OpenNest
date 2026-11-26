using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Engine.Jobs;

/// <summary>Which plates Auto Nest may use.</summary>
public enum NestPlateSource
{
    /// <summary>Fresh sheets from the template plate or the stock options (the original behavior).</summary>
    New,

    /// <summary>Only plates already in the nest: the free space beside parts already placed, then
    /// the empty plates. No plate is created.</summary>
    Current,

    /// <summary><see cref="Current"/> first, then new sheets for whatever remains.</summary>
    Both,
}

/// <summary>
/// One Auto Nest proposal over the nest's current plates, and new plates when allowed, shown and
/// kept as a whole. Nothing is committed until <see cref="CurrentPlatesNest.Commit"/>.
/// </summary>
public sealed class CurrentPlatesProposal
{
    internal CurrentPlatesProposal(LeftoverFillResult leftover, NestPipelineResult empty,
        IReadOnlyDictionary<string, Plate> plateByStockId, NestPipelineResult fresh,
        IReadOnlyList<NestUnplacedPart> unplaced, IReadOnlyDictionary<Plate, int> plateNumbers)
    {
        Leftover = leftover;
        Empty = empty;
        PlateByStockId = plateByStockId;
        New = fresh;
        Unplaced = unplaced;
        var sheets = new List<ProposedPlate>();
        foreach (var fill in leftover.Plates)
        {
            sheets.Add(new ProposedPlate(sheets.Count,
                DrawingJobMapper.FromPlate("existing", fill.Plate, 1), fill.Parts)
            {
                Occupants = fill.Occupants,
                Quantity = System.Math.Max(1, fill.Plate.Quantity),
                ExistingPlateNumber = plateNumbers.GetValueOrDefault(fill.Plate),
            });
        }
        if (empty != null)
            foreach (var proposed in empty.Plates.Where(p => p.Parts.Count > 0))
                sheets.Add(proposed with
                {
                    ExistingPlateNumber = plateNumbers.GetValueOrDefault(plateByStockId[proposed.Stock.Id]),
                });
        if (fresh != null)
            sheets.AddRange(fresh.Plates.Where(p => p.Parts.Count > 0));
        Sheets = sheets.AsReadOnly();
        Violations = leftover.Violations
            .Concat(empty?.Violations ?? Array.Empty<string>())
            .Concat(fresh?.Violations ?? Array.Empty<string>())
            .ToList().AsReadOnly();
        CanKeep = (empty?.CanKeep ?? true) && (fresh?.CanKeep ?? true);
    }

    /// <summary>Parts proposed beside the parts already on occupied plates.</summary>
    public LeftoverFillResult Leftover { get; }

    /// <summary>The whole-job result on the nest's empty plates; null when that stage did not run.</summary>
    public NestPipelineResult Empty { get; }

    /// <summary>The nest's empty plate each stock id of <see cref="Empty"/> stands for.</summary>
    public IReadOnlyDictionary<string, Plate> PlateByStockId { get; }

    /// <summary>The whole-job result on new sheets; null when that stage did not run.</summary>
    public NestPipelineResult New { get; }

    /// <summary>Every sheet the proposal places parts on: occupied plates, then empty plates, then
    /// new sheets.</summary>
    public IReadOnlyList<ProposedPlate> Sheets { get; }

    /// <summary>Layout problems from every stage.</summary>
    public IReadOnlyList<string> Violations { get; }

    public bool IsValid => Violations.Count == 0;

    /// <summary>True when every whole-job stage can be kept.</summary>
    public bool CanKeep { get; }

    /// <summary>Every requested copy the whole proposal leaves unplaced.</summary>
    public IReadOnlyList<NestUnplacedPart> Unplaced { get; }

    /// <summary>True when any stage places a part.</summary>
    public bool PlacesAnything => Sheets.Any(s => s.Parts.Count > 0);
}

/// <summary>
/// Auto Nest on the nest's current plates (<see cref="NestPlateSource.Current"/> and
/// <see cref="NestPlateSource.Both"/>), in stages that each ask only for what the earlier ones left:
/// the free space beside parts already placed (<see cref="PlateLeftoverFill"/>), then the nest's
/// empty plates as one whole-job solve with one sheet per plate, then, for Both, new sheets.
/// Whole-job placements only ever go onto empty plates. <see cref="NestPlateSource.New"/> does
/// not come here.
/// </summary>
public static class CurrentPlatesNest
{
    /// <summary>
    /// The nest's empty plates Auto Nest may use as current plates, in nest order: no parts, no
    /// cut-offs, and not the untouched trailing sheet the nest keeps ready for new work.
    /// </summary>
    public static IReadOnlyList<Plate> EmptyPlates(Nest nest, PlateManager manager)
    {
        ArgumentNullException.ThrowIfNull(nest);
        return nest.Plates
            .Where(p => p.Parts.Count == 0 && p.CutOffs.Count == 0
                && !(manager?.IsUntouchedSentinel(p) ?? false))
            .ToList();
    }

    /// <summary>
    /// Runs the stages <paramref name="source"/> calls for. <paramref name="fill"/> runs the
    /// leftover fill (typically off the UI thread), <paramref name="solve"/> each whole-job
    /// request; <paramref name="currentOptions"/> gives the empty-plate stage's options for that
    /// many sheets (no salvage: the sheets are already the nest's); <paramref name="newPlatesRequest"/> builds the new-sheet request for the demand
    /// left (Both only). The nest is not changed.
    /// </summary>
    public static async Task<CurrentPlatesProposal> RunAsync(
        Nest nest,
        PlateManager manager,
        NestPlateSource source,
        IReadOnlyList<NestItem> items,
        string engineName,
        string fillStrategy,
        Func<int, NestJobOptions> currentOptions,
        Func<IReadOnlyList<NestItem>, NestPipelineRequest> newPlatesRequest,
        Func<Func<IProgress<NestProgress>, LeftoverFillResult>, Task<LeftoverFillResult>> fill,
        Func<NestPipelineRequest, Task<NestPipelineResult>> solve,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(solve);
        if (source == NestPlateSource.New)
            throw new ArgumentOutOfRangeException(nameof(source), "New plates use the ordinary Auto Nest request.");
        if (source == NestPlateSource.Both)
            ArgumentNullException.ThrowIfNull(newPlatesRequest);

        var plateNumbers = new Dictionary<Plate, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < nest.Plates.Count; i++)
            plateNumbers[nest.Plates[i]] = i + 1;

        var remaining = Copy(items);
        // Every plate, in nest order, so the fill names plates by their nest number; it skips
        // plates without parts itself.
        var plates = nest.Plates.ToList();
        var leftover = LeftoverFillResult.Empty;
        if (plates.Any(PlateLeftoverFill.HasParts) && remaining.Count > 0)
        {
            var plan = remaining;
            leftover = await fill(progress => PlateLeftoverFill.Run(plates, plan, fillStrategy, progress, token));
            token.ThrowIfCancellationRequested();
            remaining = Subtract(remaining, leftover.Placed());
        }

        NestPipelineResult empty = null;
        var plateByStockId = new Dictionary<string, Plate>(StringComparer.Ordinal);
        var emptyPlates = EmptyPlates(nest, manager);
        if (emptyPlates.Count > 0 && remaining.Count > 0)
        {
            var stock = new List<NestPlateStock>();
            foreach (var plate in emptyPlates)
            {
                var id = $"current-{plateNumbers[plate]}";
                plateByStockId[id] = plate;
                stock.Add(DrawingJobMapper.FromPlate(id, plate, 1));
            }
            var options = currentOptions?.Invoke(stock.Count)
                ?? new NestJobOptions(maxPlates: stock.Count, salvageRate: 0);
            empty = await solve(new NestPipelineRequest(engineName, remaining, stock, options));
            token.ThrowIfCancellationRequested();
            if (empty.CanKeep)
                remaining = Subtract(remaining, Placed(empty));
        }

        NestPipelineResult fresh = null;
        if (source == NestPlateSource.Both && remaining.Count > 0)
        {
            var request = newPlatesRequest(remaining);
            if (request != null)
            {
                fresh = await solve(request);
                token.ThrowIfCancellationRequested();
                if (fresh.CanKeep)
                    remaining = Subtract(remaining, Placed(fresh));
            }
        }

        // The last whole-job stage names what it left and why; with none, what the leftover fill
        // left had no sheet to go on.
        var unplaced = fresh?.Unplaced ?? empty?.Unplaced
            ?? remaining.Select(i => new NestUnplacedPart(i.Drawing.Name, i.Drawing, i.Quantity,
                NestUnplacedReason.StockExhausted)).ToList();
        return new CurrentPlatesProposal(leftover, empty, plateByStockId, fresh, unplaced, plateNumbers);
    }

    /// <summary>
    /// Commits <paramref name="proposal"/> as one batch: the leftover parts onto their occupied
    /// plates, the empty-plate layouts onto those exact plates, then new sheets onto the untouched
    /// trailing sheet and new plates, never onto an empty plate the user set up (it was offered as
    /// a current plate). Every check runs before anything changes, and nothing after them can
    /// fail; a plate that changed since the proposal refuses the whole commit. Returns every
    /// plate that received parts, and in <paramref name="newPlates"/> those of the new-sheet
    /// stage, in its order.
    /// </summary>
    public static IReadOnlyList<Plate> Commit(CurrentPlatesProposal proposal, Nest nest, PlateManager manager,
        bool allowInvalid, CancellationToken token, out IReadOnlyList<Plate> newPlates)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(manager);
        token.ThrowIfCancellationRequested();
        if (!proposal.CanKeep || (!proposal.IsValid && !allowInvalid))
            throw new InvalidOperationException("The nesting result cannot be committed without a keepable layout and explicit consent to its violations.");
        foreach (var fill in proposal.Leftover.Plates)
        {
            if (fill.Plate.Parts.Count != fill.Occupants.Count
                || fill.Plate.Parts.Where((part, i) => !ReferenceEquals(part, fill.Occupants[i])).Any()
                || !PlateLeftoverFill.CanFill(fill.Plate))
                throw new InvalidOperationException("A plate changed after the nesting proposal was made; run Auto Nest again.");
        }
        if (proposal.Empty != null)
            NestPipelineCommit.EnsureStockPlates(proposal.Empty, proposal.PlateByStockId, allowInvalid, token);
        if (proposal.New != null)
            NestPipelineCommit.EnsureCanApply(proposal.New, allowInvalid, token);
        token.ThrowIfCancellationRequested();

        var applied = new List<Plate>();
        newPlates = Array.Empty<Plate>();
        manager.BeginBatch();
        try
        {
            foreach (var fill in proposal.Leftover.Plates)
            {
                fill.Plate.Parts.AddRange(fill.Parts);
                applied.Add(fill.Plate);
            }
            // Checked above; no token here, so a late cancel cannot leave half a commit.
            if (proposal.Empty != null)
                applied.AddRange(NestPipelineCommit.ApplyToStockPlates(proposal.Empty,
                    proposal.PlateByStockId, allowInvalid, CancellationToken.None));
            if (proposal.New != null)
            {
                var sentinel = nest.Plates.Count > 0 && manager.IsUntouchedSentinel(nest.Plates[^1])
                    ? nest.Plates[^1] : null;
                newPlates = NestPipelineCommit.ApplyToPlates(proposal.New, () =>
                {
                    if (sentinel != null && sentinel.Parts.Count == 0)
                        return sentinel;
                    return nest.CreatePlate();
                }, allowInvalid, CancellationToken.None);
                applied.AddRange(newPlates);
            }
        }
        finally
        {
            manager.EndBatch();
        }
        return applied.AsReadOnly();
    }

    private static List<NestItem> Copy(IEnumerable<NestItem> items) => items
        .Where(i => i?.Drawing != null && i.Quantity > 0)
        .Select(i => With(i, i.Quantity))
        .ToList();

    private static NestItem With(NestItem item, int quantity) => new()
    {
        Drawing = item.Drawing,
        Quantity = quantity,
        Priority = item.Priority,
        RotationStart = item.RotationStart,
        RotationEnd = item.RotationEnd,
        StepAngle = item.StepAngle,
    };

    private static List<NestItem> Subtract(List<NestItem> items, IReadOnlyDictionary<Drawing, int> placed)
    {
        var left = new List<NestItem>();
        var used = new Dictionary<Drawing, int>(placed, ReferenceEqualityComparer.Instance);
        foreach (var item in items)
        {
            var take = System.Math.Min(item.Quantity, used.GetValueOrDefault(item.Drawing));
            used[item.Drawing] = used.GetValueOrDefault(item.Drawing) - take;
            if (item.Quantity - take > 0)
                left.Add(With(item, item.Quantity - take));
        }
        return left;
    }

    /// <summary>Physical copies of each drawing a whole-job result places.</summary>
    private static IReadOnlyDictionary<Drawing, int> Placed(NestPipelineResult result)
    {
        var placed = new Dictionary<Drawing, int>(ReferenceEqualityComparer.Instance);
        foreach (var plate in result.Plates)
            foreach (var part in plate.Parts)
                placed[part.BaseDrawing] = placed.GetValueOrDefault(part.BaseDrawing) + plate.Quantity;
        return placed;
    }
}
