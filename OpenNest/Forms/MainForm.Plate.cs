using System;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Plate Menu Events

        private void SetAsNestDefault_Click(object sender, EventArgs e) => WithActive(f => f.SetCurrentPlateAsNestDefault());

        private void AddPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Nest.CreatePlate();
            NavigationEnableCheck();
        }

        private void EditPlate_Click(object sender, EventArgs e) => WithActive(f => f.EditPlate());

        private void RemovePlate_Click(object sender, EventArgs e) => WithActive(f => f.RemoveCurrentPlate());

        private void ResizeToFitParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ResizePlateToFitParts();
            statusBar.UpdatePlate();
        }

        private void RotateCw_Click(object sender, EventArgs e) => WithActive(f => f.RotateCw());

        private void RotateCcw_Click(object sender, EventArgs e) => WithActive(f => f.RotateCcw());

        private void Rotate180_Click(object sender, EventArgs e) => WithActive(f => f.Rotate180());

        private void OpenInExternalCad_Click(object sender, EventArgs e) => WithActive(f => f.OpenCurrentPlate());

        private void CalculatePlateCutTime_Click(object sender, EventArgs e) => WithActive(f => f.CalculateCurrentPlateCutTime());

        private void SimulateCutting_Click(object sender, EventArgs e) => WithActive(f => f.SimulateCutting());

        private void AutoSequenceCurrentPlate_Click(object sender, EventArgs e) => WithActive(f => f.AutoSequenceCurrentPlate());

        private void ManualSequenceParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null || activeForm.PlateView.Plate.Parts.Count < 2)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSetSequence));
        }

        private void SequenceByPath_Click(object sender, EventArgs e)
        {
            if (activeForm == null || activeForm.PlateView.Plate.Parts.Count < 2)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSequenceByPath));
        }

        private void CutOff_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.SetAction(typeof(ActionCutOff)));

        private void EditClamps_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.SetAction(typeof(ActionClampEdit)));

        private void AutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: false);

        private void NestAutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: true);

        private void ShowAutomaticCutOff(bool allPlates)
        {
            var editForm = activeForm;
            var view = editForm?.PlateView;
            if (view?.Plate == null)
                return;

            var nest = editForm.Nest;
            var views = ViewsOf(nest);
            bool IsBusy() => IsPlateWorkBusy(views);
            if (IsBusy())
            {
                MessageBox.Show(this, "Finish or cancel the current nesting or plate action first.",
                    "Automatic Scrap Cutoffs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                foreach (var plateView in views)
                    plateView.SetAction(typeof(ActionSelect));
                using var form = allPlates
                    ? new AutomaticCutOffForm(view, nest, IsBusy)
                    : new AutomaticCutOffForm(view, nest.Units, IsBusy);
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Automatic Scrap Cutoffs",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!view.IsDisposed)
                    view.ClearPreviewParts();
            }
        }

        private void PlatePlanCutting_Click(object sender, EventArgs e) => ShowCuttingPlan(allPlates: false);

        private void NestPlanCutting_Click(object sender, EventArgs e) => ShowCuttingPlan(allPlates: true);

        // Native tests capture this notice without opening a modal dialog in session zero.
        internal System.Action<string, string> CuttingPlanNotice { get; set; }

        private void ShowCuttingPlanNotice(string message, string caption)
        {
            if (CuttingPlanNotice != null)
                CuttingPlanNotice(message, caption);
            else
                MessageBox.Show(this, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowCuttingPlan(bool allPlates)
        {
            if (MdiChildren.OfType<EditNestForm>().Any(form => form.CuttingInspection != null && !ReferenceEquals(form, activeForm)))
                return;
            var editForm = activeForm;
            if (editForm?.PlateView?.Plate == null)
                return;

            var nest = editForm.Nest;
            var views = ViewsOf(nest);
            if (IsCuttingPlanBusy(views))
            {
                ShowCuttingPlanNotice("Finish or cancel the current nesting or plate action first.", "Plan Cutting");
                return;
            }

            try
            {
                foreach (var plateView in views)
                    plateView.SetAction(typeof(ActionSelect));
                editForm.OpenCuttingInspection(allPlates);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Plan Cutting", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PlatePlaceLeadIn_Click(object sender, EventArgs e) => WithActive(f => f.PlaceLeadIn_Click(sender, e));

        private void PlateRemoveLeadIns_Click(object sender, EventArgs e) => WithActive(f => f.RemoveLeadIns_Click(sender, e));

        #endregion Plate Menu Events
    }
}
