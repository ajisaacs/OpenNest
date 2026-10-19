using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Engine.Fill;
using OpenNest.Forms.Commands;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Nest Menu Events

        private void Import_Click(object sender, EventArgs e) => WithActive(f => f.Import());

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            using var form = new ShapeLibraryForm(activeForm.Nest.Drawings.Select(d => d.Name));
            form.ShowDialog(this);

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

        private async void EditNest_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                await activeForm.ShowNestInfoEditorAsync();
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

            var defaults = MachineSettings.LoadSavedClampSettings(out _);
            using var form = new ClampsForm(nest.PlateDefaults.ClampRail, nest.Units, defaults);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            form.Editor.ApplyTo(nest);
            foreach (var view in views)
                view.Invalidate();
        }

        private void RemoveEmptyPlates_Click(object sender, EventArgs e) => WithActive(f => f.Nest.Plates.RemoveEmptyPlates());

        private void LoadFirstPlate_Click(object sender, EventArgs e) => WithActive(f => f.PlateManager.LoadFirst());

        private void LoadLastPlate_Click(object sender, EventArgs e) => WithActive(f => f.PlateManager.LoadLast());

        private void LoadPreviousPlate_Click(object sender, EventArgs e) => WithActive(f => f.PlateManager.LoadPrevious());

        private void LoadNextPlate_Click(object sender, EventArgs e) => WithActive(f => f.PlateManager.LoadNext());

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

            var command = AutoNestCommand.Prompt(this, target);
            if (command == null)
                return;
            EngineSelection.EngineName = command.EngineName;
            engineComboBox.SelectedItem = command.EngineName;
            using var cts = new CancellationTokenSource();
            nestingCts = cts;
            SetNestingLockout(true);
            try
            {
                await command.RunAsync(cts);
            }
            finally
            {
                SetNestingLockout(false);
                nestingCts = null;
            }
        }

        private void SequenceAllPlates_Click(object sender, EventArgs e) => WithActive(f => f.AutoSequenceAllPlates());

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

            new PostCommand(this, editForm.Nest, postProcessor).Run();
        }

        private void CalculateNestCutTime_Click(object sender, EventArgs e) => WithActive(f => f.CalculateNestCutTime());

        private void NestAssignLeadIns_Click(object sender, EventArgs e) => WithActive(f => f.AssignLeadInsAllPlates());

        private void NestRemoveLeadIns_Click(object sender, EventArgs e) => WithActive(f => f.RemoveLeadInsAllPlates());

        #endregion Nest Menu Events
    }
}
