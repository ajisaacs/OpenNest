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

        private void MeasureArea_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSelectArea));
        }

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
            var form = new SetValueForm();
            form.Text = "Set Offset Increment";
            form.Value = activeForm.PlateView.OffsetIncrementDistance;

            if (form.ShowDialog() == DialogResult.OK)
                activeForm.PlateView.OffsetIncrementDistance = form.Value;
        }

        private void SetRotationIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            var form = new SetValueForm();
            form.Text = "Set Rotation Increment";
            form.Value = activeForm.PlateView.RotateIncrementAngle;
            form.Maximum = 360;

            if (form.ShowDialog() == DialogResult.OK)
                activeForm.PlateView.RotateIncrementAngle = form.Value;
        }

        private void Options_Click(object sender, EventArgs e)
        {
            var form = new OptionsForm();
            form.ShowDialog();
        }

        private void NestDefaults_Click(object sender, EventArgs e)
        {
            using (var form = new NestDefaultsForm(MachineSettings.LoadSavedNestDefaults(out _)))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                    form.GetDefaults().Save(NestDefaults.DefaultPath);
            }
        }

        private void SaveCurrentAsDefaults_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            NestDefaults
                .FromPlate(activeForm.Nest.Units, activeForm.PlateView.Plate)
                .Save(NestDefaults.DefaultPath);
        }

        private void MachineConfig_Click(object sender, EventArgs e)
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest",
                "Machines"
            );
            var provider = new LocalJsonProvider(appDataPath);
            provider.EnsureDefaults();
            using (var form = new MachineConfigForm(provider))
            {
                form.ShowDialog(this);
            }
        }

        private void AlignLeft_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Left);
        }

        private void AlignRight_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Right);
        }

        private void AlignTop_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Top);
        }

        private void AlignBottom_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Bottom);
        }

        private void AlignVertical_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Vertically);
        }

        private void AlignHorizontal_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Horizontally);
        }

        private void EvenlySpaceHorizontally_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceHorizontally);
        }

        private void EvenlySpaceVertically_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceVertically);
        }

        #endregion Tools Menu Events

        private void centerPartsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var plateCenter = this.activeForm.PlateView.Plate.BoundingBox(false).Center;
            var partsCenter = this.activeForm.PlateView.Parts.GetBoundingBox().Center;

            var offset = plateCenter - partsCenter;

            foreach (var part in this.activeForm.PlateView.Parts)
            {
                part.Offset(offset);
            }

            activeForm.PlateView.Invalidate();
        }
    }
}
