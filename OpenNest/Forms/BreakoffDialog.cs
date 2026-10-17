using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Breakoffs;

namespace OpenNest.Forms
{
    public class BreakoffDialog : Form
    {
        private readonly NumericUpDown numTabs;
        private readonly NumericUpDown numTabWidth;
        private readonly NumericUpDown numSlotWidth;

        public BreakoffDialog()
        {
            Text = "Breakoff Tabs";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(300, 230);

            var font = new Font("Segoe UI", 9f);

            numTabs = AddRow("Tabs:", 12, Breakoff.MinTabCount, 50, 0, 1, Breakoff.DefaultTabCount);
            numTabWidth = AddRow("Tab width:", 44, 0.001m, 10, 4, 0.0625m, (decimal)Breakoff.DefaultTabWidth);
            numSlotWidth = AddRow("Slot width:", 76, 0.001m, 10, 4, 0.0625m, (decimal)Breakoff.DefaultSlotWidth);

            Controls.Add(new Label
            {
                Text = "Tabs at both ends are included. Slots are cut into the breakoff between tabs.",
                Location = new Point(12, 108),
                Size = new Size(260, 34),
                ForeColor = Color.DimGray,
                Font = font,
            });

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(102, 150),
                Size = new Size(80, 28),
                Font = font,
            };
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(190, 150),
                Size = new Size(80, 28),
                Font = font,
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.AddRange(new Control[] { btnOk, btnCancel });

            NumericUpDown AddRow(string label, int y, decimal min, decimal max, int decimals, decimal step, decimal value)
            {
                Controls.Add(new Label { Text = label, Location = new Point(12, y + 3), AutoSize = true, Font = font });
                var input = new NumericUpDown
                {
                    Location = new Point(140, y),
                    Width = 130,
                    Font = font,
                    Minimum = min,
                    Maximum = max,
                    DecimalPlaces = decimals,
                    Increment = step,
                    Value = value,
                };
                Controls.Add(input);
                return input;
            }
        }

        public int TabCount => (int)numTabs.Value;

        public double TabWidth => (double)numTabWidth.Value;

        public double SlotWidth => (double)numSlotWidth.Value;

        public void LoadBreakoff(Breakoff breakoff)
        {
            numTabs.Value = Clamp(numTabs, breakoff.TabCenters.Count == 0 ? Breakoff.DefaultTabCount : breakoff.TabCenters.Count);
            numTabWidth.Value = Clamp(numTabWidth, breakoff.TabWidth);
            numSlotWidth.Value = Clamp(numSlotWidth, breakoff.SlotWidth);
        }

        private static decimal Clamp(NumericUpDown input, double value) =>
            System.Math.Clamp((decimal)value, input.Minimum, input.Maximum);
    }
}
