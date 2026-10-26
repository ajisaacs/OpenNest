using System;
using System.IO;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        #region Tools Menu Events

        private void MeasureArea_Click(object sender, EventArgs e) => WithActive(f => f.PlateView.SetAction(typeof(ActionSelectArea)));

        private void BestFitViewer_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var plate = activeForm.PlateView.Plate;
            var drawings = activeForm.Nest.Drawings;

            if (drawings.Count == 0)
            {
                MessageBox.Show(
                    "No drawings available.",
                    "Best-Fit Viewer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            using (var form = new BestFitViewerForm(drawings, plate, activeForm.Nest.Units))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.SelectedResult != null)
                {
                    var parts =
                        form.SelectedParts
                        ?? form.SelectedResult.BuildSourceParts(form.SelectedDrawing);
                    activeForm.PlateView.SetAction(typeof(ActionClone), parts);
                }
            }
        }

        private void ExpandSpacing_Click(object sender, EventArgs e)
        {
            var editForm = activeForm;
            if (editForm == null)
                return;

            // The search reads the plate on a worker, so a nest or fill that can still
            // add parts must finish first.
            if (IsExpandSpacingBusy(editForm))
            {
                ShowExpandSpacingNotice(
                    "Finish or cancel the current nesting or fill before Expand Spacing."
                );
                return;
            }

            // Complex layouts can search for minutes; run behind a Cancel window.
            var expanded = editForm.PlateView.ExpandSelected(work =>
                OperationProgressForm.Run(
                    this,
                    "Expand Spacing",
                    "Expanding spacing between the selected parts...",
                    work
                )
            );

            if (!expanded)
                ShowExpandSpacingNotice("Select at least two parts on the plate to expand.");
        }

        // Native tests capture this notice without opening a modal dialog in session zero.
        internal System.Action<string> ExpandSpacingNotice { get; set; }

        private void ShowExpandSpacingNotice(string message)
        {
            if (ExpandSpacingNotice != null)
                ExpandSpacingNotice(message);
            else
                MessageBox.Show(
                    this,
                    message,
                    "Expand Spacing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
        }

        private void PatternTile_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (activeForm.Nest.Drawings.Count == 0)
            {
                MessageBox.Show(
                    "No drawings available.",
                    "Pattern Tile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            using (var form = new PatternTileForm(activeForm.Nest))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
                    return;

                var result = form.Result;

                if (result.Target == PatternTileTarget.CurrentPlate)
                {
                    activeForm.PlateView.Plate.Parts.Clear();

                    foreach (var part in result.Parts)
                        activeForm.PlateView.Plate.Parts.Add(part);

                    activeForm.PlateView.ZoomToFit();
                }
                else
                {
                    var plate = activeForm.Nest.CreatePlate();
                    plate.Size = result.PlateSize;

                    foreach (var part in result.Parts)
                        plate.Parts.Add(part);

                    activeForm.PlateManager.LoadLast();
                }

                activeForm.Nest.UpdateDrawingQuantities();
            }
        }

        private void SetOffsetIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            using var form = new SetValueForm();
            form.Text = "Set Offset Increment";
            form.Value = activeForm.PlateView.OffsetIncrementDistance;

            if (form.ShowDialog(this) == DialogResult.OK)
                activeForm.PlateView.OffsetIncrementDistance = form.Value;
        }

        private void SetRotationIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            using var form = new SetValueForm();
            form.Text = "Set Rotation Increment";
            form.Value = activeForm.PlateView.RotateIncrementAngle;
            form.Maximum = 360;

            if (form.ShowDialog(this) == DialogResult.OK)
                activeForm.PlateView.RotateIncrementAngle = form.Value;
        }

        private void Options_Click(object sender, EventArgs e)
        {
            using var form = new OptionsForm();
            form.ShowDialog(this);
            RefreshLedgerHolds(); // The ledger address or key may have changed.
        }

        private void NestDefaults_Click(object sender, EventArgs e)
        {
            var defaults = MachineSettings.LoadSavedNestDefaults(out _);
            SharedSettings.WarnIfOffline(this);
            using (var form = new NestDefaultsForm(defaults, MachineSettings.LoadMachines(), SharedSettings.NestDefaultsSource()))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                    SaveNestDefaults(form.GetDefaults());
            }
        }

        private void SaveNestDefaults(NestDefaults defaults)
        {
            try
            {
                MachineSettings.SaveNestDefaults(defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "The nest defaults could not be saved.\n\n" + ex.Message, "Nest Defaults",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveCurrentAsDefaults_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var defaults = NestDefaults.FromPlate(activeForm.Nest.Units, activeForm.PlateView.Plate);
            // The nest's machine becomes the default; a nest without one keeps the saved choice.
            defaults.MachineId = activeForm.Nest.Machine?.Id ?? MachineSettings.LoadSavedNestDefaults(out _).MachineId;
            SaveNestDefaults(defaults);
        }

        private void MachineConfig_Click(object sender, EventArgs e)
        {
            var provider = MachineSettings.CreateMachineProvider(refresh: true);
            SharedSettings.WarnIfOffline(this);
            using (var form = new MachineConfigForm(provider))
            {
                var source = SharedSettings.MachineSource(provider);
                if (source.Length > 0)
                    form.Text += $" ({source})";
                form.ShowDialog(this);
            }
        }

        private void InitializeAlignMenu()
        {
            mnuPlateAlignLeft.Tag = AlignType.Left;
            mnuPlateAlignRight.Tag = AlignType.Right;
            mnuPlateAlignTop.Tag = AlignType.Top;
            mnuPlateAlignBottom.Tag = AlignType.Bottom;
            mnuPlateAlignHorizontal.Tag = AlignType.Horizontally;
            mnuPlateAlignVertically.Tag = AlignType.Vertically;
            mnuPlateEvenlySpaceHorizontal.Tag = AlignType.EvenlySpaceHorizontally;
            mnuPlateEvenlySpaceVertical.Tag = AlignType.EvenlySpaceVertically;
        }

        private void Align_Click(object sender, EventArgs e)
        {
            if (((ToolStripItem)sender).Tag is AlignType type)
                WithActive(f => f.PlateView.AlignSelected(type));
        }

        #endregion Tools Menu Events

        private void CenterParts_Click(object sender, EventArgs e)
        {
            var view = activeForm?.PlateView;
            if (view?.Plate == null || view.Parts.Count == 0)
                return;

            var offset = view.Plate.OffsetToCenterPartsInWorkArea();

            foreach (var part in view.Parts)
                part.Offset(offset);

            view.Invalidate();
        }
    }
}
