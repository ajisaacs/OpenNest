using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Auto Nest from the confirmed dialog to the committed plates: owned modal progress with
    /// the best plan so far as a sheet strip, whole-job solve, validation review, a Keep/Discard
    /// review of the final sheets, commit to empty plates and the result notices. The plate view
    /// never shows Auto Nest parts before they are kept: the result is a set of sheets, not
    /// parts on the current plate.
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
            try
            {
                var request = new NestPipelineRequest(EngineName, items,
                    NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                    new NestJobOptions(maxPlates: 100,
                        salvageRate: plateOptions?.Count > 0 ? salvageRate : 0,
                        minimumSalvageDimension: minRemnantSize));
                var result = await SolveAsync(owner, target, request, cts);
                cts.Token.ThrowIfCancellationRequested();

                var allowInvalid = false;
                if (!result.IsValid)
                {
                    using var review = new NestValidationForm(result.Violations, result.CanKeep);
                    if (review.ShowDialog(owner) != DialogResult.OK)
                        return;
                    allowInvalid = true;
                }

                var costs = result.IsValid && result.CanKeep ? NestCostSummary.FromAccepted(result) : null;
                var costText = costs?.Basis == "supplied-cost"
                    ? $"Gross sheet cost: {costs.GrossTotal:G}; estimated salvage credit: {costs.SalvageCredit:G}; net score: {costs.NetScore:G}"
                    : null;
                using (var review = new NestReviewForm(result, target.PlateView.ColorScheme, costText))
                {
                    if (review.ShowDialog(owner) != DialogResult.OK)
                        return;
                }

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
                ShowResultNotices(applied);
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
        }

        /// <summary>
        /// Solves <paramref name="request"/> off the UI thread behind the owned modal progress
        /// window, with the best plan so far as a sheet strip; its Stop cancels
        /// <paramref name="cts"/>. Nothing is committed: the caller keeps or discards the result.
        /// Make Nests uses it too, to nest each released nest in its tab as Auto Nest would.
        /// </summary>
        internal static async Task<NestPipelineResult> SolveAsync(IWin32Window owner, EditNestForm target,
            NestPipelineRequest request, CancellationTokenSource cts)
        {
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            progressForm.ShowPlanStrip(target.PlateView.ColorScheme);
            var receivingProgress = true;
            bool Receiving() => receivingProgress && !target.IsDisposed && !cts.IsCancellationRequested;
            var progress = new Progress<NestProgress>(p =>
            {
                if (Receiving())
                    progressForm.UpdateProgress(p);
            });
            var plans = new Progress<IReadOnlyList<ProposedPlate>>(sheets =>
            {
                if (Receiving())
                    progressForm.UpdatePlan(sheets);
            });
            var jobProgress = JobEngineNest.CreateProgress(request.EngineName, progress, plans);

            async Task<NestPipelineResult> RunSolveAsync()
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

            var solve = RunSolveAsync();
            // Owned modal progress holds drawings/plates stable through solve and cancellation.
            if (!solve.IsCompleted)
                progressForm.ShowDialog(owner);
            return await solve;
        }

        // Costs and unplaced parts were shown in the review; clamp removals exist only once kept.
        private void ShowResultNotices(IReadOnlyList<Plate> applied)
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
        }
    }
}
