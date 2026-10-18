using System.Drawing;
using System.Windows.Forms;
using OpenNest.TabbedCutouts;

namespace OpenNest.Forms
{
    public class TabbedCutoutDialog : Form
    {
        private readonly NumericUpDown numTabs;
        private readonly NumericUpDown numTabWidth;

        public TabbedCutoutDialog()
        {
            Text = "Tabbed Cutout";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(300, 200);

            var font = new Font("Segoe UI", 9f);

            numTabs = AddRow("Tabs:", 12, TabbedCutout.MinTabCount, 50, 0, 1, TabbedCutout.DefaultTabCount);
            numTabWidth = AddRow("Tab width:", 44, 0.001m, 10, 4, 0.0625m, (decimal)TabbedCutout.DefaultTabWidth);

            Controls.Add(new Label
            {
                Text = "Tabs keep the slug attached. The hole is cut between tabs, "
                    + "with lead-ins from the slug and no lead-outs.",
                Location = new Point(12, 78),
                Size = new Size(260, 34),
                ForeColor = Color.DimGray,
                Font = font,
            });

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(102, 120),
                Size = new Size(80, 28),
                Font = font,
            };
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(190, 120),
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

        public int TabCount
        {
            get => (int)numTabs.Value;
            set => numTabs.Value = Clamp(numTabs, value);
        }

        public double TabWidth
        {
            get => (double)numTabWidth.Value;
            set => numTabWidth.Value = Clamp(numTabWidth, value);
        }

        public void LoadCutout(TabbedCutout cutout)
        {
            TabCount = cutout.TabCenters.Count == 0 ? TabbedCutout.DefaultTabCount : cutout.TabCenters.Count;
            TabWidth = cutout.TabWidth;
        }

        private static decimal Clamp(NumericUpDown input, double value) =>
            System.Math.Clamp((decimal)value, input.Minimum, input.Maximum);
    }
}
