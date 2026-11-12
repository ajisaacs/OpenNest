using System;
using System.Windows.Forms;
using OpenNest.Collections;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        private void EnableCheck()
        {
            NavigationEnableCheck();
            ToolbarEnableCheck();

            var hasValue = activeForm != null;

            mnuFileSave.Enabled = hasValue && !storage.IsSaving;
            btnSave.Enabled = hasValue && !storage.IsSaving;
            btnSaveAs.Enabled = hasValue && !storage.IsSaving;
            mnuFileSaveAs.Enabled = hasValue && !storage.IsSaving;
            mnuNestClear.Enabled = activeForm?.PlateManager.CanClearAll ?? false;
            mnuNestServerJobs.Visible = NestStorage.Settings.IsDatabaseMode;
            exportNestMenu.Enabled = hasValue;
            mnuFileExport.Enabled = hasValue;
            mnuFileExportAll.Enabled = hasValue;
            mnuFileExportNestReport.Enabled = hasValue && !storage.IsSaving;
            mnuFileViewNestReport.Enabled = mnuFileExportNestReport.Enabled;
            mnuFileViewPartPrintouts.Enabled = hasValue;
            mnuEdit.Visible = hasValue;
            mnuView.Visible = hasValue;
            mnuNest.Visible = hasValue;
            mnuPlate.Visible = hasValue;
            mnuWindow.Visible = hasValue;
            mnuToolsMeasureArea.Visible = hasValue;
            mnuToolsSaveCurrentAsDefaults.Visible = hasValue;

            toolStripMenuItem14.Visible = hasValue;
            mnuSetOffsetIncrement.Visible = hasValue;
            mnuSetRotationIncrement.Visible = hasValue;
            toolStripMenuItem15.Visible = hasValue;
        }

        private void NavigationEnableCheck(bool locked = false)
        {
            var plates = locked ? null : activeForm?.PlateManager;
            var hasPlates = plates != null && plates.Count > 0;

            mnuNestPreviousPlate.Enabled = plates != null && !plates.IsFirst;
            mnuNestNextPlate.Enabled = plates != null && !plates.IsLast;
            mnuNestFirstPlate.Enabled = hasPlates && !plates.IsFirst;
            mnuNestLastPlate.Enabled = hasPlates && !plates.IsLast;
        }

        // The plate commands need a plate to act on and, like the Nest and Plate menus, no
        // running job. Trim/Extend works on existing cut-offs; Sequence by Path needs two parts.
        private void ToolbarEnableCheck()
        {
            var plate = nestingInProgress ? null : activeForm?.PlateView?.Plate;
            var hasPlate = plate != null;
            var hasCutOffs = plate?.CutOffs.Count > 0;

            btnAutoNest.Enabled = hasPlate;
            btnShowRemnants.Enabled = hasPlate;
            btnCutOff.Enabled = hasPlate;
            btnTrimExtendCutOff.Enabled = hasCutOffs;
            mnuPlateTrimExtendCutOff.Enabled = hasCutOffs;
            btnCutPlanner.Enabled = hasPlate;
            btnSimulateCutting.Enabled = hasPlate;
            btnSequenceByPath.Enabled = plate?.Parts.Count >= 2;
            postButton.Enabled = hasPlate;
        }

        private void EditForm_PlateChanged(object sender, EventArgs e) => ToolbarEnableCheck();

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e) => ToolbarEnableCheck();

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e) => ToolbarEnableCheck();

        private void SetNestingLockout(bool locked)
        {
            nestingInProgress = locked;

            // Disable nesting-related menus and plate navigation while running
            mnuNest.Enabled = !locked;
            mnuPlate.Enabled = !locked;
            NavigationEnableCheck(locked);
            ToolbarEnableCheck();
        }

        private void EngineComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (engineComboBox.SelectedItem is string name)
            {
                EngineSelection.EngineName = name;
                engineComboBox.ToolTipText = EngineSelection.ToolTip(name);
            }
        }

        #region Statusbar Events

        private void LocationStatusLabel_Click(object sender, EventArgs e) =>
            statusBar.ToggleLocationMode();

        private void MaterialStatusLabel_Click(object sender, EventArgs e) =>
            EditNest_Click(sender, e);

        // Holds are read only on open, save and here; the status bar never polls.
        private void LedgerStatusLabel_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                _ = activeForm.HoldStatus.RefreshAsync();
        }

        // Either button lists the machines above the label, as Nest > Machine does.
        private void MachineStatusLabel_MouseUp(object sender, MouseEventArgs e)
        {
            if (activeForm == null || nestingInProgress || e.Button is not (MouseButtons.Left or MouseButtons.Right))
                return;

            var menu = new ContextMenuStrip();
            FillMachineItems(menu.Items);
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            var bounds = machineStatusLabel.Bounds;
            menu.Show(statusStrip1, new System.Drawing.Point(bounds.Left, bounds.Top), ToolStripDropDownDirection.AboveRight);
        }

        #endregion Statusbar Events
    }
}
