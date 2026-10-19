using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        private void EnableCheck()
        {
            NavigationEnableCheck();

            var hasValue = activeForm != null;

            mnuFileSave.Enabled = hasValue && !storage.IsSaving;
            btnSave.Enabled = hasValue && !storage.IsSaving;
            btnSaveAs.Enabled = hasValue && !storage.IsSaving;
            mnuFileSaveAs.Enabled = hasValue && !storage.IsSaving;
            exportNestMenu.Enabled = hasValue;
            mnuFileExport.Enabled = hasValue;
            mnuFileExportAll.Enabled = hasValue;
            mnuFileExportNestReport.Enabled = hasValue && !storage.IsSaving;
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

        private void SetNestingLockout(bool locked)
        {
            nestingInProgress = locked;

            // Disable nesting-related menus and plate navigation while running
            mnuNest.Enabled = !locked;
            mnuPlate.Enabled = !locked;
            NavigationEnableCheck(locked);
        }

        private void EngineComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (engineComboBox.SelectedItem is string name)
                EngineSelection.EngineName = name;
        }

        #region Statusbar Events

        private void LocationStatusLabel_Click(object sender, EventArgs e) =>
            statusBar.ToggleLocationMode();

        private void MaterialStatusLabel_Click(object sender, EventArgs e) =>
            EditNest_Click(sender, e);

        #endregion Statusbar Events
    }
}
