using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Geometry;

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

        private void UpdateLocationStatus()
        {
            if (activeForm == null)
            {
                locationStatusLabel.Text = string.Empty;
                return;
            }

            locationStatusLabel.Text = string.Format(
                "Location: [{0}, {1}]",
                activeForm.PlateView.CurrentPoint.X.ToString("n4"),
                activeForm.PlateView.CurrentPoint.Y.ToString("n4")
            );
        }

        private void UpdatePlateStatus()
        {
            if (activeForm == null)
            {
                plateIndexStatusLabel.Text = string.Empty;
                plateSizeStatusLabel.Text = string.Empty;
                plateQtyStatusLabel.Text = string.Empty;
                plateUtilStatusLabel.Text = string.Empty;
                return;
            }

            plateIndexStatusLabel.Text = string.Format(
                "Plate: {0} of {1}",
                activeForm.PlateManager.CurrentIndex + 1,
                activeForm.PlateManager.Count
            );

            plateSizeStatusLabel.Text = string.Format("Size: {0}", activeForm.PlateView.Plate.Size);

            plateQtyStatusLabel.Text = string.Format(
                "Qty: {0}",
                activeForm.PlateView.Plate.Quantity
            );

            plateUtilStatusLabel.Text = string.Format(
                "Util: {0:P1}",
                activeForm.PlateView.Plate.Utilization()
            );
        }

        private void UpdateSelectionStatus()
        {
            if (activeForm == null || activeForm.PlateView.SelectedParts.Count == 0)
            {
                selectionStatusLabel.Text = string.Empty;
                return;
            }

            var selected = activeForm.PlateView.SelectedParts;

            if (selected.Count == 1)
            {
                var box = selected[0].BoundingBox;
                selectionStatusLabel.Text = string.Format(
                    "Selected: [{0}, {1}] {2} x {3}",
                    box.X.ToString("n4"),
                    box.Y.ToString("n4"),
                    box.Width.ToString("n4"),
                    box.Length.ToString("n4")
                );
            }
            else
            {
                var bounds = selected.Select(p => p.BasePart).ToList().GetBoundingBox();
                selectionStatusLabel.Text = string.Format(
                    "Selected ({0}): [{1}, {2}] {3} x {4}",
                    selected.Count,
                    bounds.X.ToString("n4"),
                    bounds.Y.ToString("n4"),
                    bounds.Width.ToString("n4"),
                    bounds.Length.ToString("n4")
                );
            }
        }

        private void UpdateStatus()
        {
            UpdateLocationStatus();
            UpdatePlateStatus();
            UpdateSelectionStatus();
        }

        private void EngineComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (engineComboBox.SelectedItem is string name)
                EngineSelection.EngineName = name;
        }

        private void UpdateLocationMode()
        {
            if (activeForm == null)
                return;

            if (clickUpdateLocation)
            {
                clickUpdateLocation = true;
                locationStatusLabel.ForeColor = Color.Gray;
                activeForm.PlateView.MouseMove -= PlateView_MouseMove;
                activeForm.PlateView.MouseClick += PlateView_MouseClick;
            }
            else
            {
                clickUpdateLocation = false;
                locationStatusLabel.ForeColor = Color.Black;
                activeForm.PlateView.MouseMove += PlateView_MouseMove;
                activeForm.PlateView.MouseClick -= PlateView_MouseClick;
            }
        }

        #region Statusbar Events

        private void LocationStatusLabel_Click(object sender, EventArgs e)
        {
            clickUpdateLocation = !clickUpdateLocation;
            UpdateLocationMode();
        }

        #endregion Statusbar Events

        #region PlateView Events

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e) =>
            UpdatePlateStatus();

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e) =>
            UpdatePlateStatus();

        private void PlateView_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateLocationStatus();
        }

        private void PlateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                UpdateLocationStatus();
        }

        private void PlateView_StatusChanged(object sender, EventArgs e)
        {
            statusLabel1.Text = activeForm.PlateView.Status;
        }

        private void PlateView_SelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionStatus();
        }

        #endregion PlateView Events
    }
}
