using OpenNest.Engine.Jobs;
using M = System.Math;

namespace OpenNest.Engine.Astra;

/// <summary>Independent configuration-space contact packing with bounded stock-plan search.</summary>
public sealed class AstraNestingEngine : INestingEngine
{
    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        NestJobValidator.Validate(job);
        var parts = GeometryPreparation.Prepare(job, token);
        var fit = parts.Select(p => job.Plates.Select(s => p.Variants.Any(v =>
            v.Width <= s.Size.Length - s.EdgeSpacing.Left - s.EdgeSpacing.Right + 1e-9 &&
            v.Height <= s.Size.Width - s.EdgeSpacing.Top - s.EdgeSpacing.Bottom + 1e-9)).ToArray()).ToArray();
        var placer = new ContactPlacer(parts, new ContactGeometry(), token);
        var initial = new Plan(new int[parts.Length], new int[job.Plates.Count], new List<SheetTrial>(), 0);
        var frontier = new List<Plan> { initial };
        var best = initial;
        Plan? complete = IsComplete(initial) ? initial : null;
        var trials = new Dictionary<string, SheetTrial>(StringComparer.Ordinal);
        var priorities = job.Parts.Select(p => p.Priority).Distinct().OrderDescending().ToArray();
        var evaluated = 0;
        var unitCosts = Enumerable.Repeat(double.PositiveInfinity, parts.Length).ToArray();
        while (frontier.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var children = new Dictionary<string, Plan>(StringComparer.Ordinal);
            foreach (var state in frontier)
            {
                if (state.Sheets.Count >= (job.Options.MaxPlates ?? int.MaxValue)) continue;
                var lowerBound = LowerBound(state);
                if (complete != null && lowerBound >= complete.Cost - 1e-7) continue;
                var flexibility = Enumerable.Range(0, parts.Length).Select(p =>
                    Enumerable.Range(0, job.Plates.Count).Count(s => fit[p][s] && Available(state, s))).ToArray();
                for (var s = 0; s < job.Plates.Count; s++)
                {
                    if (!Available(state, s)) continue;
                    // Search breadth is work-count bounded, never elapsed-time dependent.
                    // Past this budget, continue filling greedily instead of abandoning demand.
                    var modes = evaluated < 24 ? 2 : 1;
                    for (var mode = 0; mode < modes; mode++)
                    {
                        token.ThrowIfCancellationRequested();
                        var key = $"{s}/{mode}/{string.Join(',', state.Counts)}/{string.Join(',', flexibility)}";
                        if (!trials.TryGetValue(key, out var trial))
                        {
                            progress?.Report(new(NestJobStage.EvaluatingCandidate, job.Plates[s].Id,
                                state.Sheets.Count, 0, 0));
                            trial = placer.Pack(s, job.Plates[s], state.Counts, flexibility, mode);
                            if (trials.Count >= 256) trials.Clear();
                            trials[key] = trial;
                            evaluated++;
                        }
                        if (trial.Shapes.Count == 0) continue;
                        var sheetCost = job.Plates[s].Size.Length * job.Plates[s].Size.Width;
                        for (var p = 0; p < parts.Length; p++)
                        {
                            var delivered = trial.Counts[p] - state.Counts[p];
                            if (delivered > 0) unitCosts[p] = M.Min(unitCosts[p], sheetCost / delivered);
                        }
                        var used = (int[])state.Used.Clone(); used[s]++;
                        var sheets = new List<SheetTrial>(state.Sheets) { trial };
                        var next = new Plan(trial.Counts, used, sheets,
                            state.Cost + job.Plates[s].Size.Length * job.Plates[s].Size.Width);
                        if (BetterFulfillment(next, best)) best = next;
                        if (IsComplete(next))
                        {
                            if (complete == null || next.Cost < complete.Cost - 1e-7 ||
                                (M.Abs(next.Cost - complete.Cost) < 1e-7 && next.Sheets.Count < complete.Sheets.Count)) complete = next;
                            continue;
                        }
                        var stateKey = $"{string.Join(',', next.Counts)}/{string.Join(',', next.Used)}";
                        if (!children.TryGetValue(stateKey, out var prior) || next.Cost < prior.Cost)
                            children[stateKey] = next;
                    }
                }
            }
            var ranked = children.Values.Where(p => complete == null || LowerBound(p) < complete.Cost - 1e-7)
                .OrderBy(Estimate).ThenByDescending(PlacedArea).ThenBy(p => p.Cost).ToList();
            frontier = new List<Plan>();
            if (ranked.Count > 0)
            {
                frontier.Add(ranked[0]);
                // A material-only lower bound favors cheap small-sheet prefixes and
                // can discard every high-throughput plan. Preserve one progress leader.
                var leader = ranked.OrderByDescending(PlacedArea).ThenBy(p => p.Cost).First();
                if (!ReferenceEquals(leader, ranked[0])) frontier.Add(leader);
                foreach (var candidate in ranked)
                {
                    if (frontier.Count >= (evaluated < 64 ? 3 : 2)) break;
                    if (!frontier.Contains(candidate)) frontier.Add(candidate);
                }
            }
        }
        var selected = complete ?? best;
        var counts = new int[parts.Length];
        var plates = new List<NestJobPlateResult>();
        foreach (var sheet in selected.Sheets)
        {
            token.ThrowIfCancellationRequested();
            var stock = job.Plates[sheet.StockIndex];
            var x = (stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length) + stock.EdgeSpacing.Left;
            var y = (stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width) + stock.EdgeSpacing.Bottom;
            var placements = sheet.Shapes.Select(p => new NestJobPlacement(job.Parts[p.Variant.Part].Id,
                counts[p.Variant.Part]++, x + p.X - p.Variant.OriginX,
                y + p.Y - p.Variant.OriginY, p.Variant.Angle)).ToArray();
            plates.Add(new(plates.Count, stock, placements));
            progress?.Report(new(NestJobStage.PlateCommitted, stock.Id, plates.Count - 1,
                plates.Count, counts.Sum()));
        }
        token.ThrowIfCancellationRequested();
        var reason = complete != null ? NestJobStopReason.Completed :
            selected.Sheets.Count >= (job.Options.MaxPlates ?? int.MaxValue) ? NestJobStopReason.PlateLimitReached :
            !Enumerable.Range(0, job.Plates.Count).Any(s => Available(selected, s)) ? NestJobStopReason.StockExhausted :
            NestJobStopReason.NoPlacementFound;
        return new(complete != null ? NestJobStatus.Complete : NestJobStatus.Incomplete, reason, plates,
            job.Parts.Select((p, i) => new PartFulfillment(p.Id, p.Quantity, counts[i], p.Quantity - counts[i])),
            job.Plates.Select((s, i) => new StockUsage(s.Id, selected.Used[i], s.Quantity - selected.Used[i])));

        bool Available(Plan p, int s) => p.Used[s] < (job.Plates[s].Quantity ?? int.MaxValue);
        bool IsComplete(Plan p) => parts.Select((part, i) => p.Counts[i] == part.Requirement.Quantity).All(v => v);
        double PlacedArea(Plan p) => parts.Select((part, i) => p.Counts[i] * part.Area).Sum();
        double LowerBound(Plan p) => p.Cost + parts.Select((part, i) =>
            (part.Requirement.Quantity - p.Counts[i]) * part.Area).Sum();
        double Estimate(Plan p)
        {
            var projected = 0.0;
            for (var i = 0; i < parts.Length; i++)
                if (double.IsFinite(unitCosts[i])) projected = M.Max(projected,
                    (parts[i].Requirement.Quantity - p.Counts[i]) * unitCosts[i]);
            return M.Max(LowerBound(p), p.Cost + projected);
        }
        bool BetterFulfillment(Plan a, Plan b)
        {
            foreach (var priority in priorities)
            {
                var ac = parts.Select((p, i) => p.Requirement.Priority == priority ? a.Counts[i] : 0).Sum();
                var bc = parts.Select((p, i) => p.Requirement.Priority == priority ? b.Counts[i] : 0).Sum();
                if (ac != bc) return ac > bc;
            }
            return a.Cost < b.Cost;
        }
    }

    private sealed record Plan(int[] Counts, int[] Used, List<SheetTrial> Sheets, double Cost);
}
