using System;
using System.Drawing;
using System.Linq;
using OpenNest.Actions;
using OpenNest.Diagnostics;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Edit Menu Events

        private void EditCut_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);

                using var change = activeForm.BeginChange("Cut");
                change.MarkChanged();
                foreach (var part in partsToClone)
                    activeForm.PlateView.Plate.Parts.Remove(part);
                change.Complete();
            }
        }

        private void EditCopy_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);
            }
        }

        private void EditSelectAll_Click(object sender, EventArgs e) => WithActive(f => f.SelectAllParts());

        #endregion Edit Menu Events

        #region View Menu Events

        private void OverlapStateChanged(object sender, EventArgs e) => UpdateOverlapMenu();
        private void OverlapMenu_Opening(object sender, EventArgs e) => UpdateOverlapMenu();

        private void UpdateOverlapMenu()
        {
            var hasPlate = activeForm != null && !activeForm.IsDisposed && activeForm.PlateView.Plate != null;
            mnuViewOverlapCheck.Enabled = hasPlate;
            mnuOverlapCheckActive.Enabled = hasPlate;
            mnuOverlapCancel.Enabled = hasPlate && activeForm.PlateView.IsOverlapCheckRunning;
            mnuOverlapDisplay.Enabled = hasPlate;
            mnuOverlapOff.Enabled = hasPlate;
            mnuOverlapAreas.Enabled = hasPlate;
            mnuOverlapCentroids.Enabled = hasPlate;
            mnuOverlapBoth.Enabled = hasPlate;
            mnuOverlapOff.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Off;
            mnuOverlapAreas.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Areas;
            mnuOverlapCentroids.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Centroids;
            mnuOverlapBoth.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Both;
        }

        private async void CheckOverlaps_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form != null)
                await form.CheckOverlapsAsync();
        }

        private void CancelOverlapCheck_Click(object sender, EventArgs e) => activeForm?.CancelOverlapCheck();

        private void OverlapOff_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Off;
        }

        private void OverlapAreas_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Areas;
        }

        private void OverlapCentroids_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Centroids;
        }

        private void OverlapBoth_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Both;
        }

        private void ToggleDrawRapids_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleRapid();
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
        }

        private void ToggleDrawPiercePoints_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.TogglePiercePoints();
            mnuViewDrawPiercePoints.Checked = activeForm.PlateView.DrawPiercePoints;
        }

        private void ToggleDrawBounds_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleDrawBounds();
            mnuViewDrawBounds.Checked = activeForm.PlateView.DrawBounds;
        }

        private void ToggleDrawOffset_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleDrawOffset();
            mnuViewDrawOffset.Checked = activeForm.PlateView.DrawOffset;
        }

        private void ToggleDrawCutDirection_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleCutDirection();
            mnuViewDrawCutDirection.Checked = activeForm.PlateView.DrawCutDirection;
        }

        private void ToggleDrawSupport_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleSupport();
            mnuViewDrawSupport.Checked = activeForm.PlateView.DrawSupport;
        }

        private void ZoomToArea_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.SetAction(typeof(ActionZoomWindow)));

        private void ZoomToFit_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.ZoomToFit());

        private void ZoomToPlate_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.ZoomToPlate());

        private void ZoomToSelected_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.ZoomToSelected());

        private void ZoomIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var pt = new Point(activeForm.PlateView.Width / 2, activeForm.PlateView.Height / 2);

            activeForm.PlateView.ZoomToControlPoint(pt, ZoomInFactor);
        }

        private void ZoomOut_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var pt = new Point(activeForm.PlateView.Width / 2, activeForm.PlateView.Height / 2);

            activeForm.PlateView.ZoomToControlPoint(pt, ZoomOutFactor);
        }

        #endregion View Menu Events
    }
}
