using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Auto Nest from the confirmed dialog to the committed plates: owned modal progress,
    /// whole-job solve, validation review, commit to empty plates and the result notices.
    /// The caller owns the busy guard, the nesting lockout and the cancellation source.
    /// </summary>
    internal sealed class AutoNestCommand
    {
        private const string Caption = "Auto Nest";

        private readonly IWin32Window owner;
        private readonly EditNestForm target;
        private readonly List<NestItem> items;
        private readonly List<PlateOption> plateOptions;
        private readonly double salvageRate;
        private readonly double minRemnantSize;

        private AutoNestCommand(IWin32Window owner, EditNestForm target, List<NestItem> items,
            List<PlateOption> plateOptions, string engineName, double salvageRate, double minRemnantSize)
        {
            this.owner = owner;
            this.target = target;
            this.items = items;
            this.plateOptions = plateOptions;
            EngineName = engineName;
            this.salvageRate = salvageRate;
            this.minRemnantSize = minRemnantSize;
        }

        /// <summary>The engine chosen in the dialog, or the current selection when it named none.</summary>
        public string EngineName { get; }

        /// <summary>
        /// Shows the Auto Nest dialog for <paramref name="target"/>. Returns null when it is
        /// cancelled, nothing has a quantity, or the plate-size options are invalid.
        /// </summary>
        public static AutoNestCommand Prompt(IWin32Window owner, EditNestForm target)
        {
            using var form = new AutoNestForm(target.Nest);
            if (target.Nest.PlateOptions.Count > 0)
                form.LoadPlateOptions(target.Nest.PlateOptions, target.Nest.SalvageRate);
            if (form.ShowDialog(owner) != DialogResult.OK)
                return null;

            var items = form.GetNestItems();
            if (!items.Any(it => it.Quantity > 0))
                return null;
            var plateOptions = default(List<PlateOption>);
            if (form.OptimizePlateSize && !form.TryGetPlateOptions(out plateOptions, out var stockError))
            {
                MessageBox.Show(owner, stockError, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return new AutoNestCommand(owner, target, items, plateOptions,
                form.EngineName ?? EngineSelection.EngineName, form.SalvageRate, form.MinRemnantSize);
        }

        /// <summary>
        /// Solves and commits the job. Cancelling <paramref name="cts"/> discards the whole
        /// proposal; failures are reported to the user rather than thrown.
        /// </summary>
        public async Task RunAsync(CancellationTokenSource cts)
        {
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            var receivingProgress = true;
            // Everything shown so far; the view zooms out only when a trial sheet falls outside it.
            var shown = target.PlateView.Plate.BoundingBox(false);
            var progress = new Progress<NestProgress>(p =>
            {
                if (!receivingProgress || target.IsDisposed || cts.IsCancellationRequested)
                    return;
                progressForm.UpdateProgress(p);
                if (p.IsOverallBest)
                {
                    target.PlateView.SetActiveParts(p.BestParts, p.PreviewSheet);
                    var sheet = p.PreviewSheet?.BoundingBox(false);
                    if (sheet != null && !shown.Contains(sheet))
                    {
                        var left = System.Math.Min(shown.Left, sheet.Left);
                        var bottom = System.Math.Min(shown.Bottom, sheet.Bottom);
                        shown = new Box(left, bottom, System.Math.Max(shown.Right, sheet.Right) - left,
                            System.Math.Max(shown.Top, sheet.Top) - bottom);
                        target.PlateView.ZoomToArea(shown);
                    }
                }
                target.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });
            var jobProgress = JobEngineNest.CreateProgress(EngineName, progress);
            try
            {
                var request = new NestPipelineRequest(EngineName, items,
                    NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                    new NestJobOptions(maxPlates: 100,
                        salvageRate: plateOptions?.Count > 0 ? salvageRate : 0,
                        minimumSalvageDimension: minRemnantSize));

                async Task<NestPipelineResult> SolveAsync()
                {
                    try
                    {
                        return await Task.Run(() => NestPipeline.Run(request, jobProgress, cts.Token));
                    }
                    finally
                    {
                        receivingProgress = false;
                        progressForm.ShowCompleted();
                        progressForm.Close();
                    }
                }

                var solve = SolveAsync();
                // Owned modal progress holds drawings/plates stable through solve and cancellation.
                if (!solve.IsCompleted)
                    progressForm.ShowDialog(owner);
                var result = await solve;
                cts.Token.ThrowIfCancellationRequested();
                target.PlateView.ClearPreviewParts();
                target.PlateView.ActiveWorkArea = null;

                var allowInvalid = false;
                if (!result.IsValid)
                {
                    using var review = new NestValidationForm(result.Violations, result.CanKeep);
                    if (review.ShowDialog(owner) != DialogResult.OK)
                        return;
                    allowInvalid = true;
                }

                var costs = result.IsValid && result.CanKeep ? NestCostSummary.FromAccepted(result) : null;
                var applied = NestPipelineCommit.ApplyToEmptyPlates(result, target.PlateManager,
                    allowInvalid, cts.Token);
                if (applied.Count > 0)
                    target.PlateManager.LoadAt(target.Nest.Plates.IndexOf(applied[0]));
                if (plateOptions?.Count > 0)
                {
                    target.Nest.PlateOptions = plateOptions;
                    target.Nest.SalvageRate = salvageRate;
                }
                target.PlateView.Invalidate();
                if (allowInvalid)
                {
                    target.OverlapDisplay = OverlapDisplayMode.Both;
                    await target.CheckOverlapsAsync();
                }
                ShowResultNotices(result, applied, costs);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stop discards the entire proposal, including engines that ignore cancellation.
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Nesting error: {ex.Message}", Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                receivingProgress = false;
                if (!target.IsDisposed)
                {
                    target.PlateView.ClearPreviewParts();
                    target.PlateView.ActiveWorkArea = null;
                }
            }
        }

        private void ShowResultNotices(NestPipelineResult result, IReadOnlyList<Plate> applied,
            NestCostSummary costs)
        {
            // D8/D9: name every clamp Auto Nest pulled as a last resort and its added time.
            var removalNotices = applied
                .Where(plate => plate.RemovedClamps.Count > 0)
                .Select(plate => $"Sheet {target.Nest.Plates.IndexOf(plate) + 1}: remove "
                    + $"{string.Join(", ", plate.RemovedClamps.OrderBy(id => id, StringComparer.Ordinal))} "
                    + $"(+{plate.ClampRemovalMinutes:0.#} min)")
                .ToList();
            if (removalNotices.Count > 0)
                MessageBox.Show(owner,
                    "Auto Nest removed clamps as a last resort so a part that would not "
                        + "otherwise fit could be nested:\n\n" + string.Join("\n", removalNotices),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (costs?.Basis == "supplied-cost")
                MessageBox.Show(owner, $"Gross sheet cost: {costs.GrossTotal:G}\nEstimated salvage credit: {costs.SalvageCredit:G}\nNet score: {costs.NetScore:G}",
                    "Auto Nest Cost", MessageBoxButtons.OK, MessageBoxIcon.Information);
            // Never drop demand silently: name every unplaced part and why (the drawing
            // list keeps showing the remaining quantities after this closes).
            if (result.Unplaced.Count > 0)
                MessageBox.Show(owner, $"{EngineName} could not place every part:\n\n"
                        + string.Join("\n", result.Unplaced.Select(u => u.Describe())),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
