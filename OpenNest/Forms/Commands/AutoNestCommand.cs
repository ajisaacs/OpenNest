using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.CommonLines;
using OpenNest.Data.MaterialLedger;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Auto Nest from the confirmed dialog to the committed plates: owned modal progress with
    /// the best plan so far as a sheet strip, whole-job solve, validation review, a Keep/Discard
    /// review of the final sheets (skipped when the user picked the layout in the progress
    /// window), commit to empty plates and the result notices. The plate view
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
        private List<PlateOption> savedOptions;
        private IReadOnlyList<LedgerStockRow> ledgerRows = Array.Empty<LedgerStockRow>();
        private bool nestOnServer;
        private CommonLineUsage commonLineUsage;
        private NestPlateSource plateSource;

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

        /// <summary>
        /// The cutting kerf common-line groups are made at: the current plate's for the nest's
        /// material and thickness (the machine's cutting defaults). Zero, which makes no groups,
        /// when it is unknown.
        /// </summary>
        internal static double CommonLineKerf(EditNestForm target)
        {
            var kerf = MachineSettings.CuttingParametersFor(target.PlateView.Plate, target.Nest)?.Kerf ?? 0;
            return double.IsFinite(kerf) && kerf > 0 ? kerf : 0;
        }

        /// <summary>The engine chosen in the dialog, or the current selection when it named none.</summary>
        public string EngineName { get; }

        /// <summary>
        /// Shows the Auto Nest dialog for <paramref name="target"/>. Returns null when it is
        /// canceled, nothing has a quantity, or the plate-size options are invalid.
        /// </summary>
        public static AutoNestCommand Prompt(IWin32Window owner, EditNestForm target)
        {
            using var form = new AutoNestForm(target.Nest)
            {
                ServerNestId = target.Document.RemoteId,
                ServerNestingAvailable = NestStorage.Settings.IsDatabaseMode,
            };
            if (target.Nest.PlateOptions.Count > 0)
                form.LoadPlateOptions(target.Nest.PlateOptions, target.Nest.SalvageRate);
            if (target.Nest.MinimumRemnantDimension > 0)
                form.MinRemnantSize = target.Nest.MinimumRemnantDimension;
            if (form.ShowDialog(owner) != DialogResult.OK)
                return null;

            var items = form.GetNestItems();
            if (!items.Any(it => it.Quantity > 0))
                return null;
            var plateOptions = default(List<PlateOption>);
            var savedOptions = default(List<PlateOption>);
            if (form.OptimizePlateSize
                && !form.TryGetPlateOptions(out plateOptions, out savedOptions, out var stockError))
            {
                MessageBox.Show(owner, stockError, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return new AutoNestCommand(owner, target, items, plateOptions,
                form.EngineName ?? EngineSelection.EngineName, form.SalvageRate, form.MinRemnantSize)
            {
                savedOptions = savedOptions,
                ledgerRows = form.OptimizePlateSize ? form.LedgerRows : Array.Empty<LedgerStockRow>(),
                nestOnServer = form.NestOnServer,
                commonLineUsage = form.CommonLineUsage,
                plateSource = form.PlateSource,
            };
        }

        /// <summary>
        /// Solves and commits the job. Canceling <paramref name="cts"/> discards the whole
        /// proposal; failures are reported to the user rather than thrown.
        /// </summary>
        public async Task RunAsync(CancellationTokenSource cts)
        {
            if (plateSource != NestPlateSource.New)
            {
                await RunOnCurrentPlatesAsync(cts);
                return;
            }
            try
            {
                var request = new NestPipelineRequest(EngineName, items,
                    NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                    new NestJobOptions(maxPlates: 100,
                        salvageRate: plateOptions?.Count > 0 ? salvageRate : 0,
                        minimumSalvageDimension: minRemnantSize)
                    {
                        CommonLineUsage = commonLineUsage,
                        CommonLineKerf = CommonLineKerf(target),
                    });
                NestPipelineResult result;
                ServerNestDecision server = null;
                var picked = false;
                if (nestOnServer)
                {
                    var solved = await ServerAutoNest.SolveAsync(owner, target, request, cts);
                    result = solved.Result;
                    server = solved.Job == null ? null : new ServerNestDecision(solved.Job);
                }
                else
                {
                    (result, picked) = await SolveAndPickAsync(owner, target, request, cts, chooseLayout: true);
                }
                cts.Token.ThrowIfCancellationRequested();

                await ReviewAndCommitAsync(owner, target, result, server, applied =>
                {
                    AssignLedgerStock(result, applied);
                    target.Nest.CommonLineUsage = commonLineUsage;
                    if (plateOptions?.Count > 0)
                    {
                        // Ledger rows and quantities are request-local inventory; the nest keeps
                        // only the hand-typed offers.
                        if (savedOptions?.Count > 0)
                            target.Nest.PlateOptions = savedOptions;
                        target.Nest.SalvageRate = salvageRate;
                        target.Nest.MinimumRemnantDimension = minRemnantSize;
                    }
                }, cts.Token, layoutPicked: picked);
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
        /// The tail every Auto Nest proposal shares: the validation review when it broke layout
        /// rules, the Keep/Discard review, then the commit to empty plates and the result notices.
        /// A server job's Keep is recorded on the server first (<paramref name="server"/>), and only
        /// commits when the server accepts it, so no result is kept twice; its Discard is recorded
        /// too. <paramref name="applying"/> runs inside the commit's undo record. Returns the plates
        /// committed, or null when nothing was kept.
        /// </summary>
        /// <param name="layoutPicked">The user already chose this layout in the progress window:
        /// the Keep/Discard review is skipped, and any unplaced parts are named after the commit
        /// instead. The validation review still runs when the layout broke rules.</param>
        internal static async Task<IReadOnlyList<Plate>> ReviewAndCommitAsync(IWin32Window owner, EditNestForm target,
            NestPipelineResult result, ServerNestDecision server, Action<IReadOnlyList<Plate>> applying, CancellationToken token,
            bool layoutPicked = false)
        {
            var allowInvalid = false;
            if (!result.IsValid)
            {
                using var review = new NestValidationForm(result.Violations, result.CanKeep);
                if (review.ShowDialog(owner) != DialogResult.OK)
                {
                    await (server?.DiscardAsync() ?? Task.CompletedTask);
                    return null;
                }
                allowInvalid = true;
            }

            if (!layoutPicked)
            {
                var costs = result.IsValid && result.CanKeep ? NestCostSummary.FromAccepted(result) : null;
                var costText = costs?.Basis == "supplied-cost"
                    ? $"Gross sheet cost: {costs.GrossTotal:G}; estimated salvage credit: {costs.SalvageCredit:G}; net score: {costs.NetScore:G}"
                    : null;
                using var review = new NestReviewForm(result, target.PlateView.ColorScheme, costText);
                if (review.ShowDialog(owner) != DialogResult.OK)
                {
                    await (server?.DiscardAsync() ?? Task.CompletedTask);
                    return null;
                }
            }
            if (server != null && !await server.KeepAsync(owner))
                return null;
            if (target.IsDisposed)
                return null;

            IReadOnlyList<Plate> applied;
            // Recorded from the commit only: a stopped or discarded run records nothing.
            using (var change = target.BeginChange(Caption))
            {
                change.MarkChanged();
                applied = NestPipelineCommit.ApplyToEmptyPlates(result, target.PlateManager,
                    allowInvalid, token);
                applying?.Invoke(applied);
                if (applied.Count > 0)
                    target.PlateManager.LoadAt(target.Nest.Plates.IndexOf(applied[0]));
                change.Complete();
            }
            target.PlateView.Invalidate();
            if (allowInvalid)
            {
                target.OverlapDisplay = OverlapDisplayMode.Both;
                await target.CheckOverlapsAsync();
            }
            // Never drop demand silently: without the review, name what was left out here.
            if (layoutPicked && result.Unplaced.Count > 0)
                MessageBox.Show(owner, UnplacedNotice(result), Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowResultNotices(owner, target, applied);
            return applied;
        }

        internal static string UnplacedNotice(NestPipelineResult result) =>
            $"Not placed ({result.Unplaced.Sum(u => u.Count)}):\n\n"
                + string.Join("\n", result.Unplaced.Select(u => u.Describe()));

        /// <summary>
        /// Solves <paramref name="request"/> off the UI thread behind the owned modal progress
        /// window, with the best plan so far as a sheet strip; its Stop cancels
        /// <paramref name="cts"/>. Nothing is committed: the caller keeps or discards the result.
        /// Make Nests uses it too, to nest each released nest in its tab as Auto Nest would.
        /// </summary>
        /// <param name="chooseLayout">Lets the user use any of the top layouts shown: one picked
        /// during the run stops the search, and a finished run with more than one waits for the
        /// pick. The layout used is validated and bound like the engine's own result. Off, the
        /// engine's result is returned as soon as it is known.</param>
        /// <param name="choiceTimeout">With <paramref name="chooseLayout"/>, how long a finished
        /// run with more than one layout waits for the pick before using its own result (Make
        /// Nests, which goes on to the next nest); picking a card stops the countdown. Null waits
        /// until the user picks or discards.</param>
        internal static async Task<NestPipelineResult> SolveAsync(IWin32Window owner, EditNestForm target,
            NestPipelineRequest request, CancellationTokenSource cts, bool chooseLayout = false,
            TimeSpan? choiceTimeout = null) =>
            (await SolveAndPickAsync(owner, target, request, cts, chooseLayout, choiceTimeout)).Result;

        /// <summary>
        /// <see cref="SolveAsync"/>, also telling whether the user picked the layout in the
        /// progress window (Picked) rather than having the run's result taken for them.
        /// </summary>
        internal static async Task<(NestPipelineResult Result, bool Picked)> SolveAndPickAsync(IWin32Window owner,
            EditNestForm target, NestPipelineRequest request, CancellationTokenSource cts, bool chooseLayout = false,
            TimeSpan? choiceTimeout = null)
        {
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            // Using a layout stops the search only; the caller's source still discards everything.
            using var search = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            progressForm.ShowPlanStrip(target.PlateView.ColorScheme, chooseLayout ? search.Cancel : null);
            var receivingProgress = true;
            bool Receiving() => receivingProgress && !target.IsDisposed && !search.IsCancellationRequested;
            var progress = new Progress<NestProgress>(p =>
            {
                if (Receiving())
                    progressForm.UpdateProgress(p);
            });
            var layouts = new Progress<NestLayoutReport>(report =>
            {
                if (Receiving())
                    progressForm.AddLayout(report.Layout, report.Best);
            });
            var jobProgress = JobEngineNest.CreateLayoutProgress(request.EngineName, progress, layouts);

            async Task<(NestPipelineResult, bool)> RunSolveAsync()
            {
                try
                {
                    NestPipelineResult result = null;
                    try
                    {
                        result = await Task.Run(() => NestPipeline.Run(request, jobProgress, search.Token));
                    }
                    catch (OperationCanceledException)
                        when (progressForm.ChosenLayout != null && !cts.IsCancellationRequested)
                    {
                        // The user stopped the search to use a layout it had already found.
                    }
                    receivingProgress = false;
                    if (result != null
                        && !await progressForm.ChooseLayoutAsync(new NestLayoutChoice(result.Plates, _ => result),
                            choiceTimeout))
                    {
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }
                    var chosen = progressForm.ChosenLayout;
                    return chosen == null
                        ? (result, false)
                        : (await Task.Run(() => chosen.Accept(cts.Token)), progressForm.LayoutPickedByUser);
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

        /// <summary>
        /// Auto Nest on the nest's current plates (and new sheets for Both): the free space beside
        /// placed parts, the empty plates, then new sheets, shown together in one review and kept in
        /// one undo record. Always on this PC: the server lays out new plates only.
        /// </summary>
        private async Task RunOnCurrentPlatesAsync(CancellationTokenSource cts)
        {
            try
            {
                var kerf = CommonLineKerf(target);
                var proposal = await CurrentPlatesNest.RunAsync(target.Nest, target.PlateManager, plateSource,
                    items, EngineName, EngineSelection.FillStrategy,
                    sheets => new NestJobOptions(maxPlates: sheets, salvageRate: 0,
                        minimumSalvageDimension: minRemnantSize)
                    {
                        CommonLineUsage = commonLineUsage,
                        CommonLineKerf = kerf,
                    },
                    remaining => new NestPipelineRequest(EngineName, remaining.ToList(),
                        NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                        new NestJobOptions(maxPlates: 100,
                            salvageRate: plateOptions?.Count > 0 ? salvageRate : 0,
                            minimumSalvageDimension: minRemnantSize)
                        {
                            CommonLineUsage = commonLineUsage,
                            CommonLineKerf = kerf,
                        }),
                    plan => FillLeftoverAsync(plan, cts),
                    async request => (await SolveAndPickAsync(owner, target, request, cts)).Result,
                    cts.Token);
                cts.Token.ThrowIfCancellationRequested();

                if (!proposal.PlacesAnything)
                {
                    var why = plateSource == NestPlateSource.Current
                        && CurrentPlatesNest.EmptyPlates(target.Nest, target.PlateManager).Count == 0
                        && !target.Nest.Plates.Any(PlateLeftoverFill.CanFill)
                            ? "The nest has no current plate Auto Nest can use: no empty plate you have set up, "
                                + "and no rectangular plate with parts and without cut-offs."
                            : plateSource == NestPlateSource.Current
                                ? "Auto Nest placed nothing on the current plates."
                                : "Auto Nest placed nothing on the current plates or new sheets.";
                    if (proposal.Unplaced.Count > 0)
                        why += "\n\n" + $"Not placed ({proposal.Unplaced.Sum(u => u.Count)}):\n"
                            + string.Join("\n", proposal.Unplaced.Select(u => u.Describe()));
                    MessageBox.Show(owner, why, Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                await ReviewAndCommitAsync(proposal, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stop discards the entire proposal.
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Nesting error: {ex.Message}", Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Runs the leftover fill off the UI thread behind a cancellable progress window.</summary>
        private async Task<LeftoverFillResult> FillLeftoverAsync(
            Func<IProgress<NestProgress>, LeftoverFillResult> plan, CancellationTokenSource cts)
        {
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            var receiving = true;
            var progress = new Progress<NestProgress>(p =>
            {
                if (receiving && !target.IsDisposed)
                    progressForm.UpdateProgress(p);
            });
            async Task<LeftoverFillResult> RunFillAsync()
            {
                try
                {
                    return await Task.Run(() => plan(progress));
                }
                finally
                {
                    receiving = false;
                    progressForm.ShowCompleted();
                    progressForm.Close();
                }
            }

            var fill = RunFillAsync();
            if (!fill.IsCompleted)
                progressForm.ShowDialog(owner);
            return await fill;
        }

        /// <summary>
        /// The current-plates counterpart of the shared review: the validation review when a stage
        /// broke layout rules, the Keep/Discard review of every sheet (parts already placed shown in
        /// one color), then one commit in one undo record.
        /// </summary>
        private async Task ReviewAndCommitAsync(CurrentPlatesProposal proposal, CancellationToken token)
        {
            var allowInvalid = false;
            if (!proposal.IsValid)
            {
                using var validation = new NestValidationForm(proposal.Violations, proposal.CanKeep);
                if (validation.ShowDialog(owner) != DialogResult.OK)
                    return;
                allowInvalid = true;
            }
            using (var review = new NestReviewForm($"Review Auto Nest — {EngineName}", proposal.Sheets,
                proposal.IsValid ? 0 : proposal.Violations.Count, proposal.Unplaced,
                target.PlateView.ColorScheme, null))
            {
                if (review.ShowDialog(owner) != DialogResult.OK)
                    return;
            }
            if (target.IsDisposed)
                return;

            IReadOnlyList<Plate> applied;
            using (var change = target.BeginChange(Caption))
            {
                change.MarkChanged();
                applied = CurrentPlatesNest.Commit(proposal, target.Nest, target.PlateManager, allowInvalid, token,
                    out var newPlates);
                if (proposal.New != null)
                    AssignLedgerStock(proposal.New, newPlates);
                target.Nest.CommonLineUsage = commonLineUsage;
                if (proposal.New != null && plateOptions?.Count > 0)
                {
                    if (savedOptions?.Count > 0)
                        target.Nest.PlateOptions = savedOptions;
                    target.Nest.SalvageRate = salvageRate;
                    target.Nest.MinimumRemnantDimension = minRemnantSize;
                }
                if (applied.Count > 0)
                    target.PlateManager.LoadAt(target.Nest.Plates.IndexOf(applied[0]));
                change.Complete();
            }
            target.PlateView.Invalidate();
            target.UpdatePlateList();
            if (allowInvalid)
            {
                target.OverlapDisplay = OverlapDisplayMode.Both;
                await target.CheckOverlapsAsync();
            }
            ShowResultNotices(owner, target, applied);
        }

        // Only a committed proposal gets codes: every return above leaves them as they were.
        private void AssignLedgerStock(NestPipelineResult result, IReadOnlyList<Plate> applied)
        {
            LedgerStockAssignment.Assign(target.Nest.Plates,
                NestPipelineCommit.PairWithStock(result, applied).Select(p => (p.Plate, p.Stock.Id)),
                ledgerRows);
            foreach (var row in ledgerRows)
            {
                foreach (var (code, location) in row.ItemLocations)
                    target.LedgerLocations[code] = location;
            }
            target.UpdatePlateList();
        }

        // Costs and unplaced parts were shown in the review; clamp removals exist only once kept.
        private static void ShowResultNotices(IWin32Window owner, EditNestForm target, IReadOnlyList<Plate> applied)
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
