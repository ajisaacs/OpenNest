using System;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Plate Menu Events

        private void SetAsNestDefault_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.SetCurrentPlateAsNestDefault();
        }

        private void AddPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Nest.CreatePlate();
            NavigationEnableCheck();
        }

        private void EditPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.EditPlate();
        }

        private void RemovePlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RemoveCurrentPlate();
        }

        private void ResizeToFitParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ResizePlateToFitParts();
            UpdatePlateStatus();
        }

        private void RotateCw_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RotateCw();
        }

        private void RotateCcw_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RotateCcw();
        }

        private void Rotate180_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Rotate180();
        }

        private void OpenInExternalCad_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.OpenCurrentPlate();
        }

        private void CalculatePlateCutTime_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.CalculateCurrentPlateCutTime();
        }

        private void AutoSequenceCurrentPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.AutoSequenceCurrentPlate();
        }

        private void ManualSequenceParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null || activeForm.PlateView.Plate.Parts.Count < 2)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSetSequence));
        }

        private void CutOff_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.PlateView.SetAction(typeof(ActionCutOff));
        }

        private void AutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: false);

        private void NestAutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: true);

        private void ShowAutomaticCutOff(bool allPlates)
        {
            var editForm = activeForm;
            var view = editForm?.PlateView;
            if (view?.Plate == null)
                return;

            var nest = editForm.Nest;
            var views = MdiChildren.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView).ToArray();
            // A closed progress window can still have a fill awaiting completion/commit.
            bool IsBusy() => nestingInProgress || Application.OpenForms.OfType<NestProgressForm>().Any()
                || views.Any(v => v.IsFillInProgress || v.Actions.CurrentAction?.IsBusy() == true);
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

        internal bool IsCuttingPlanBusy(PlateView[] views) => nestingInProgress
            || Application.OpenForms.OfType<NestProgressForm>().Any()
            || views.Any(v => v.IsFillInProgress || v.Actions.CurrentAction?.IsBusy() == true);

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
            var views = MdiChildren.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView).ToArray();
            // A closed progress window can still have a fill awaiting completion/commit.
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

        private void PlateAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.AssignLeadIns_Click(sender, e);
        }

        private void PlatePlaceLeadIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlaceLeadIn_Click(sender, e);
        }

        private void PlateRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RemoveLeadIns_Click(sender, e);
        }

        #endregion Plate Menu Events
    }
}
