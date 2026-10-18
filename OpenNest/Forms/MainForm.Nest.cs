using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Nest Menu Events

        private void Import_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Import();
        }

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var form = new ShapeLibraryForm(activeForm.Nest.Drawings.Select(d => d.Name));
            form.ShowDialog();

            var drawings = form.GetDrawings();
            if (drawings.Count == 0)
                return;

            drawings.ForEach(d => activeForm.Nest.Drawings.Add(d));
            activeForm.UpdateDrawingList();
        }

        private void DrawingLibrary_Click(object sender, EventArgs e)
        {
            activeForm?.AddFromDrawingLibrary();
        }

        private void NestMenu_Opening(object sender, EventArgs e)
        {
            // The drawing library lives on the nest server; File mode has nothing to browse.
            var databaseMode = NestStorage.Settings.IsDatabaseMode;
            mnuNest.DropDown.ShowItemToolTips = true;
            mnuNestDrawingLibrary.Enabled = activeForm != null && databaseMode;
            mnuNestDrawingLibrary.ToolTipText = databaseMode ? "" : NestStorage.DatabaseModeOnly;
        }

        private void EditNest_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ShowNestInfoEditor();
        }

        private void Clamps_Click(object sender, EventArgs e)
        {
            var editForm = activeForm;
            if (editForm == null)
                return;

            var nest = editForm.Nest;
            var views = ViewsOf(nest);
            if (IsPlateWorkBusy(views))
            {
                MessageBox.Show(
                    this,
                    "Finish or cancel the current nesting or plate action first.",
                    "Clamps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            var defaults = LoadSavedClampSettings(out _);
            using var form = new ClampsForm(nest.PlateDefaults.ClampRail, nest.Units, defaults);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            form.Editor.ApplyTo(nest);
            foreach (var view in views)
                view.Invalidate();
        }

        private void RemoveEmptyPlates_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Nest.Plates.RemoveEmptyPlates();
        }

        private void LoadFirstPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadFirst();
        }

        private void LoadLastPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadLast();
        }

        private void LoadPreviousPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadPrevious();
        }

        private void LoadNextPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadNext();
        }

        private RemnantViewerForm remnantViewer;

        private void ShowRemnants_Click(object sender, EventArgs e)
        {
            if (activeForm?.PlateView?.Plate == null)
                return;

            var plate = activeForm.PlateView.Plate;

            // Minimum remnant dimension = smallest part bbox dimension on the plate.
            var minDim = 0.0;
            var nest = activeForm.Nest;
            if (nest != null)
            {
                foreach (var drawing in nest.Drawings)
                {
                    var bbox = drawing.Program.BoundingBox();
                    var dim = System.Math.Min(bbox.Width, bbox.Length);
                    if (minDim == 0 || dim < minDim)
                        minDim = dim;
                }
            }

            var finder = RemnantFinder.FromPlate(plate);

            if (remnantViewer == null || remnantViewer.IsDisposed)
            {
                remnantViewer = new RemnantViewerForm();
                remnantViewer.Owner = this;

                // Position next to the main form's right edge.
                var screen = Screen.FromControl(this);
                remnantViewer.Location = new Point(
                    System.Math.Min(Right, screen.WorkingArea.Right - remnantViewer.Width),
                    Top
                );
            }

            remnantViewer.LoadRemnants(finder, minDim, activeForm.PlateView);
            remnantViewer.Show();
            remnantViewer.BringToFront();
        }

        private async void RunAutoNest_Click(object sender, EventArgs e)
        {
            var target = activeForm;
            if (target == null || target.IsDisposed || target.PlateView.Plate == null)
                return;
            if (!TryBeginPlateCommand(target.Nest,
                    "Finish or cancel the current plate operation before Auto Nest.", "Auto Nest"))
                return;

            using var form = new AutoNestForm(target.Nest);
            if (target.Nest.PlateOptions.Count > 0)
                form.LoadPlateOptions(target.Nest.PlateOptions, target.Nest.SalvageRate);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var items = form.GetNestItems();
            if (!items.Any(it => it.Quantity > 0))
                return;
            var plateOptions = default(List<PlateOption>);
            if (form.OptimizePlateSize && !form.TryGetPlateOptions(out plateOptions, out var stockError))
            {
                MessageBox.Show(this, stockError, "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var engineName = form.EngineName ?? EngineSelection.EngineName;
            EngineSelection.EngineName = engineName;
            engineComboBox.SelectedItem = engineName;
            using var cts = new CancellationTokenSource();
            nestingCts = cts;
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            var receivingProgress = true;
            var progress = new Progress<NestProgress>(p =>
            {
                if (!receivingProgress || target.IsDisposed || cts.IsCancellationRequested)
                    return;
                progressForm.UpdateProgress(p);
                if (p.IsOverallBest)
                    target.PlateView.SetActiveParts(p.BestParts);
                target.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });
            var jobProgress = JobEngineNest.CreateProgress(engineName, progress);
            SetNestingLockout(true);
            try
            {
                var request = new NestPipelineRequest(engineName, items,
                    NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                    new NestJobOptions(maxPlates: 100,
                        salvageRate: plateOptions?.Count > 0 ? form.SalvageRate : 0,
                        minimumSalvageDimension: form.MinRemnantSize));

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
                    progressForm.ShowDialog(this);
                var result = await solve;
                cts.Token.ThrowIfCancellationRequested();
                target.PlateView.ClearPreviewParts();
                target.PlateView.ActiveWorkArea = null;

                var allowInvalid = false;
                if (!result.IsValid)
                {
                    using var review = new NestValidationForm(result.Violations, result.CanKeep);
                    if (review.ShowDialog(this) != DialogResult.OK)
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
                    target.Nest.SalvageRate = form.SalvageRate;
                }
                target.PlateView.Invalidate();
                if (allowInvalid)
                {
                    target.OverlapDisplay = OverlapDisplayMode.Both;
                    await target.CheckOverlapsAsync();
                }
                // D8/D9: name every clamp Auto Nest pulled as a last resort and its added time.
                var removalNotices = applied
                    .Where(plate => plate.RemovedClamps.Count > 0)
                    .Select(plate => $"Sheet {target.Nest.Plates.IndexOf(plate) + 1}: remove "
                        + $"{string.Join(", ", plate.RemovedClamps.OrderBy(id => id, StringComparer.Ordinal))} "
                        + $"(+{plate.ClampRemovalMinutes:0.#} min)")
                    .ToList();
                if (removalNotices.Count > 0)
                    MessageBox.Show(this,
                        "Auto Nest removed clamps as a last resort so a part that would not "
                            + "otherwise fit could be nested:\n\n" + string.Join("\n", removalNotices),
                        "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (costs?.Basis == "supplied-cost")
                    MessageBox.Show(this, $"Gross sheet cost: {costs.GrossTotal:G}\nEstimated salvage credit: {costs.SalvageCredit:G}\nNet score: {costs.NetScore:G}",
                        "Auto Nest Cost", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Never drop demand silently: name every unplaced part and why (the drawing
                // list keeps showing the remaining quantities after this closes).
                if (result.Unplaced.Count > 0)
                    MessageBox.Show(this, $"{engineName} could not place every part:\n\n"
                            + string.Join("\n", result.Unplaced.Select(u => u.Describe())),
                        "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stop discards the entire proposal, including engines that ignore cancellation.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Nesting error: {ex.Message}", "Auto Nest",
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
                SetNestingLockout(false);
                nestingCts = null;
            }
        }

        private void SequenceAllPlates_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.AutoSequenceAllPlates();
        }

        private void ValidateNest_Click(object sender, EventArgs e)
        {
            var editForm = activeForm;
            if (editForm == null)
                return;

            if (!TryBeginPlateCommand(editForm.Nest,
                    "Finish or cancel the current nesting or plate action before validating.", "Validate Nest"))
                return;

            editForm.OpenValidationInspection();
        }

        private void PostProcessor_Click(object sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender;
            var postProcessor = menuItem.Tag as IPostProcessor;

            var editForm = activeForm;
            if (postProcessor == null || editForm == null)
                return;

            // Closed progress windows can still have a fill awaiting its final commit.
            // Keep the nest stable throughout verification and all owned modal dialogs.
            if (MdiChildren.OfType<EditNestForm>().Any(form => form.CuttingInspection != null))
            {
                MessageBox.Show(this, "Close cutting inspection before posting. Posting will run fresh checks.",
                    "Verify Nest Before Posting", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!TryBeginPlateCommand(editForm.Nest,
                    "Finish or cancel the current nesting or plate action before posting.",
                    "Verify Nest Before Posting"))
                return;

            var nest = editForm.Nest;

            if (postProcessor is IPostProcessorNestAware nestAware)
                nestAware.PrepareForNest(nest);

            if (postProcessor is IConfigurablePostProcessor configurable)
            {
                using var configForm = new PostProcessorConfigForm(configurable);
                if (configForm.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            using (var verification = new PostVerificationForm(nest, postProcessor))
            {
                if (verification.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            using var dialog = new SaveFileDialog();
            dialog.Filter = "CNC File (*.cnc) | *.cnc";
            dialog.FileName = nest.Name;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var path = dialog.FileName;

                if (postProcessor is IMultiFilePostProcessor multiFile)
                {
                    var files = multiFile.GetOutputFiles(nest, path);
                    if (!ConfirmOverwrite(files, path))
                        return;

                    try
                    {
                        postProcessor.Post(nest, path);
                    }
                    catch (Exception ex)
                        when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(
                            this,
                            ex.Message,
                            postProcessor.Name,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    if (files.Count > 1)
                        MessageBox.Show(
                            this,
                            $"Saved {files.Count} programs, one per sheet:\n\n"
                                + string.Join("\n", files.Select(Path.GetFileName))
                                + $"\n\nin {Path.GetDirectoryName(path)}",
                            postProcessor.Name,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    return;
                }

                postProcessor.Post(nest, path);
            }
        }

        /// <summary>
        /// The save dialog only checks the chosen name; ask before replacing any
        /// other existing file a multi-file post will write.
        /// </summary>
        private bool ConfirmOverwrite(IReadOnlyList<string> files, string chosenPath)
        {
            var existing = files
                .Where(f => File.Exists(f) && !string.Equals(f, chosenPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count == 0)
                return true;

            var answer = MessageBox.Show(
                this,
                "These files already exist and will be replaced:\n\n"
                    + string.Join("\n", existing.Select(Path.GetFileName))
                    + "\n\nReplace them?",
                "Confirm Save",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );
            return answer == DialogResult.Yes;
        }

        private void CalculateNestCutTime_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.CalculateNestCutTime();
        }

        private void NestAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.AssignLeadInsAllPlates();
        }

        private void NestRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RemoveLeadInsAllPlates();
        }

        #endregion Nest Menu Events
    }
}
