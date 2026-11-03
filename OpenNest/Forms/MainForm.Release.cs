using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine.Jobs;
using OpenNest.Forms.Commands;

namespace OpenNest.Forms
{
    /// <summary>
    /// Make Nests with Nest after release ticked nests here, where the operator can watch: each
    /// built nest opens in its own tab before it is saved and Auto Nest runs on it with the
    /// usual progress window and plan strip, one nest at a time in plan order. The release
    /// keeps only a valid layout, as headless (<see cref="NestRenest"/>), without the Keep or
    /// validation review. Stop in the progress window, or switching tabs, stops the release.
    /// Once saved, a tab is bound to its server record; a tab whose nest was not saved
    /// (refused, failed or stopped before upload) is closed.
    /// </summary>
    public partial class MainForm : IReleaseNestingHost
    {
        // Tabs Make Nests opened for nests it has not saved yet.
        private readonly List<EditNestForm> releaseTabs = new();

        /// <summary>
        /// Solves a released nest's Auto Nest behind the progress window; replaced by tests,
        /// which cannot wait on its modal loop.
        /// </summary>
        internal Func<EditNestForm, NestPipelineRequest, CancellationTokenSource, Task<NestPipelineResult>> SolveReleasedNest { get; set; }

        void IReleaseNestingHost.Begin()
        {
            releaseTabs.Clear();
            // The nests being released are not edited until they are saved.
            SetNestingLockout(true);
            BringForward();
        }

        async Task<SetReleaseRenest> IReleaseNestingHost.RenestAsync(Nest nest,
            ReleaseLedgerStock ledger, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(nest);
            token.ThrowIfCancellationRequested();
            LoadNest(nest, ActiveMdiChild?.WindowState ?? FormWindowState.Maximized);
            var tab = MdiChildren.OfType<EditNestForm>().Last(f => ReferenceEquals(f.Nest, nest));
            releaseTabs.Add(tab);
            tab.Activate();

            // A merged draft's sheets are already gone; whatever the new nest started with goes too.
            if (tab.PlateManager.CanClearAll)
                tab.ClearNest();
            nest.UpdateDrawingQuantities();
            var offer = ledger == null ? null : await ledger.OfferAsync(nest, token);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            // As for Auto Nest, switching tabs while it runs stops it.
            nestingCts = cts;
            NestRenestResult renested;
            try
            {
                // Ledger stock first, then the plate defaults: each stage is its own Auto Nest run.
                var solve = SolveReleasedNest
                    ?? ((t, r, c) => AutoNestCommand.SolveAsync(this, t, r, c));
                renested = await NestRenest.RunStagedAsync(nest, EngineSelection.EngineName,
                    offer?.Options, AutoNestForm.DefaultMinRemnantSize,
                    request => solve(tab, request, cts),
                    r =>
                    {
                        using var change = tab.BeginChange("Auto Nest");
                        change.MarkChanged();
                        var applied = NestPipelineCommit.ApplyToEmptyPlates(r, tab.PlateManager,
                            token: cts.Token);
                        change.Complete();
                        return applied;
                    }, cts.Token);
            }
            finally
            {
                nestingCts = null;
            }

            var ledgerNote = offer == null ? null : ledger.Assign(nest, offer, renested.Committed);
            foreach (var row in offer?.Rows ?? Array.Empty<LedgerStockRow>())
            {
                foreach (var (code, location) in row.ItemLocations)
                    tab.LedgerLocations[code] = location;
            }
            if (renested.Nested)
            {
                tab.UpdatePlateList();
                tab.PlateManager.LoadFirst();
            }
            tab.PlateView.ZoomToFit();
            return new SetReleaseRenest(renested.Nested,
                ReleasePanel.ReleaseNote(renested.Note, ledgerNote));
        }

        void IReleaseNestingHost.End(SetReleaseResult result)
        {
            // Saved nests were bound to their records and left the list; the rest were never saved.
            foreach (var tab in releaseTabs.ToList())
            {
                if (!tab.IsDisposed)
                    tab.Close();
            }
            releaseTabs.Clear();
            SetNestingLockout(false);

            var first = result?.Created.FirstOrDefault()?.Nest;
            MdiChildren.OfType<EditNestForm>().FirstOrDefault(f => first != null && ReferenceEquals(f.Nest, first))?.Activate();
            BringForward();
        }

        // A nest Make Nests is releasing is saved by the release; a Save now would make a second nest.
        private bool RefuseSaveDuringRelease(EditNestForm form)
        {
            if (!releaseTabs.Contains(form))
                return false;
            MessageBox.Show(this, $"'{form.Nest.Name}' is being released. Make Nests saves it when it finishes.",
                "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private void BringForward()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }
    }
}
