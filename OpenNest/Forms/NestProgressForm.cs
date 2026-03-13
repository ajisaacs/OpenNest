using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class NestProgressForm : Form
    {
        private readonly CancellationTokenSource cts;

        private Label phaseValue;
        private Label plateValue;
        private Label partsValue;
        private Label densityValue;
        private Label remnantValue;
        private Label plateLabel;
        private Button stopButton;
        private TableLayoutPanel table;

        public NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)
        {
            this.cts = cts;
            InitializeLayout(showPlateRow);
        }

        private void InitializeLayout(bool showPlateRow)
        {
            Text = "Nesting Progress";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Size = new Size(280, showPlateRow ? 210 : 190);

            table = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8)
            };

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            phaseValue = AddRow(table, "Phase:");
            plateValue = AddRow(table, "Plate:");
            partsValue = AddRow(table, "Parts:");
            densityValue = AddRow(table, "Density:");
            remnantValue = AddRow(table, "Remnant:");

            if (!showPlateRow)
            {
                plateLabel = FindLabel(table, "Plate:");
                if (plateLabel != null)
                    SetRowVisible(plateLabel, plateValue, false);
            }

            stopButton = new Button
            {
                Text = "Stop",
                Width = 80,
                Anchor = AnchorStyles.None,
                Margin = new Padding(0, 8, 0, 8)
            };

            stopButton.Click += StopButton_Click;

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 0, 8, 0)
            };

            buttonPanel.Controls.Add(stopButton);

            Controls.Add(buttonPanel);
            Controls.Add(table);

            // Reverse order since Dock.Top stacks bottom-up
            Controls.SetChildIndex(table, 0);
            Controls.SetChildIndex(buttonPanel, 1);
        }

        private Label AddRow(TableLayoutPanel table, string labelText)
        {
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = labelText,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(4)
            };

            var value = new Label
            {
                Text = "\u2014",
                AutoSize = true,
                Margin = new Padding(4)
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(value, 1, row);

            return value;
        }

        private Label FindLabel(TableLayoutPanel table, string text)
        {
            foreach (Control c in table.Controls)
            {
                if (c is Label l && l.Text == text)
                    return l;
            }

            return null;
        }

        private void SetRowVisible(Label label, Label value, bool visible)
        {
            label.Visible = visible;
            value.Visible = visible;
        }

        public void UpdateProgress(NestProgress progress)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            phaseValue.Text = FormatPhase(progress.Phase);
            plateValue.Text = progress.PlateNumber.ToString();
            partsValue.Text = progress.BestPartCount.ToString();
            densityValue.Text = progress.BestDensity.ToString("P1");
            remnantValue.Text = $"{progress.UsableRemnantArea:F1} sq in";
        }

        public void ShowCompleted()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            phaseValue.Text = "Done";
            stopButton.Text = "Close";
            stopButton.Enabled = true;
            stopButton.Click -= StopButton_Click;
            stopButton.Click += (s, e) => Close();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            stopButton.Text = "Stopping...";
            stopButton.Enabled = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!cts.IsCancellationRequested)
                cts.Cancel();

            base.OnFormClosing(e);
        }

        private static string FormatPhase(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Linear: return "Trying rotations...";
                case NestPhase.RectBestFit: return "Trying best fit...";
                case NestPhase.Pairs: return "Trying pairs...";
                case NestPhase.Remainder: return "Filling remainder...";
                default: return phase.ToString();
            }
        }
    }
}
