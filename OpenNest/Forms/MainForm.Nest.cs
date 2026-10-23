using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Data;
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
            // Parts can be added without a plate change, so refresh the clear state on open.
            mnuNestClear.Enabled = activeForm?.PlateManager.CanClearAll ?? false;
            BuildMachineMenu();
        }

        // Nest > Machine is rebuilt each time the Nest menu opens.
        private void BuildMachineMenu()
        {
            mnuNestMachine.Enabled = activeForm != null;
            FillMachineItems(mnuNestMachine.DropDownItems);
        }

        // Lists the machines on file, checking the active nest's; one assigned but no longer on
        // file is shown checked and disabled so the nest still says what it was made for. Shared
        // by Nest > Machine and the status bar's machine label.
        private void FillMachineItems(ToolStripItemCollection items)
        {
            items.Clear();
            var nest = activeForm?.Nest;
            if (nest == null)
            {
                items.Add(new ToolStripMenuItem("(no nest open)") { Enabled = false });
                return;
            }

            var machines = MachineSettings.LoadMachines()
                .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var machine in machines)
            {
                var item = new ToolStripMenuItem(machine.Name) { Checked = machine.Id == nest.Machine?.Id };
                item.Click += (_, _) => AssignMachine(machine);
                items.Add(item);
            }
            if (nest.Machine is { } assigned && machines.All(m => m.Id != assigned.Id))
                items.Add(new ToolStripMenuItem($"{assigned.Name} (not on file)") { Checked = true, Enabled = false });
            if (items.Count == 0)
                items.Add(new ToolStripMenuItem("(no machines; see Tools > Machine Configuration)") { Enabled = false });
        }

        // Assigning a machine also puts every sheet on that machine's support grid and gives
        // the views its cut-off settings; plates' own cutting parameters are kept.
        private void AssignMachine(MachineConfig machine)
        {
            var editForm = activeForm;
            if (editForm == null || editForm.Nest.Machine?.Id == machine.Id)
                return;

            var nest = editForm.Nest;
            var views = ViewsOf(nest);
            if (IsPlateWorkBusy(views))
            {
                MessageBox.Show(this, "Finish or cancel the current nesting or plate action first.",
                    "Machine", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MachineSettings.AssignMachine(nest, machine);
            foreach (var view in views)
            {
                MachineSettings.ApplyCutOffDefaults(view, nest);
                view.Invalidate();
            }
            statusBar.UpdatePlate();
        }

        private async void EditNest_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            await activeForm.ShowNestInfoEditorAsync();
            statusBar.UpdatePlate(); // Material and thickness may have changed.
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

        private void SupportGrid_Click(object sender, EventArgs e)
        {
            var editForm = activeForm;
            if (editForm == null)
                return;

            var nest = editForm.Nest;
            var views = ViewsOf(nest);
            if (IsPlateWorkBusy(views))
            {
                MessageBox.Show(this, "Finish or cancel the current nesting or plate action first.",
                    "Support Grid", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var machine = MachineSettings.FindMachine(nest.Machine);
            Action<SupportGridSettings> save = machine == null
                ? null
                : settings => MachineSettings.SaveSupportGridSettings(machine.Id, settings);
            using var form = new SupportGridForm(nest.SupportBed, nest.Units, MachineSettings.LoadSupportGridSettings(machine),
                machine?.Name ?? nest.Machine?.Name, save);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            nest.SetSupportBed(form.Grid);
            foreach (var view in views)
                view.Invalidate();
        }

        private void RemoveEmptyPlates_Click(object sender, EventArgs e) => WithActive(f => f.Nest.Plates.RemoveEmptyPlates());

        private void ClearNest_Click(object sender, EventArgs e)
        {
            var editForm = activeForm;
            if (editForm == null)
                return;

            const string busy = "Finish or cancel the current nesting, save or plate action first.";
            if (storage.IsSaving)
            {
                MessageBox.Show(this, busy, "Clear Nest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryBeginPlateCommand(editForm.Nest, busy, "Clear Nest"))
                return;

            var result = MessageBox.Show(
                this,
                "Remove all plates and parts from this nest?\nOne empty plate will remain. This cannot be undone.",
                "Clear Nest",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2
            );

            if (result != DialogResult.Yes)
                return;

            editForm.ClearNest();
        }

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

        private void NestRemoveLeadIns_Click(object sender, EventArgs e) => WithActive(f => f.RemoveLeadInsAllPlates());

        #endregion Nest Menu Events
    }
}
