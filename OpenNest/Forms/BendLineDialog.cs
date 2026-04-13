using OpenNest.Bending;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class BendLineDialog : Form
    {
        private readonly ComboBox cboDirection;
        private readonly NumericUpDown numAngle;
        private readonly NumericUpDown numRadius;
        private readonly CheckBox chkRadius;

        public BendLineDialog()
        {
            Text = "Bend Line Properties";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(260, 200);

            var font = new Font("Segoe UI", 9f);

            // Direction
            var lblDir = new Label { Text = "Direction:", Location = new Point(12, 15), AutoSize = true, Font = font };
            cboDirection = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(100, 12),
                Width = 130,
                Font = font
            };
            cboDirection.Items.AddRange(new object[] { "Down", "Up" });
            cboDirection.SelectedIndex = 0;

            // Angle
            var lblAngle = new Label { Text = "Angle:", Location = new Point(12, 47), AutoSize = true, Font = font };
            numAngle = new NumericUpDown
            {
                Location = new Point(100, 44),
                Width = 130,
                Font = font,
                Minimum = 0,
                Maximum = 180,
                DecimalPlaces = 1,
                Value = 90
            };

            // Radius (with checkbox to enable)
            chkRadius = new CheckBox { Text = "Radius:", Location = new Point(12, 79), AutoSize = true, Font = font };
            numRadius = new NumericUpDown
            {
                Location = new Point(100, 76),
                Width = 130,
                Font = font,
                Minimum = 0,
                Maximum = 25,
                DecimalPlaces = 3,
                Increment = 0.0625m,
                Enabled = false
            };
            chkRadius.CheckedChanged += (s, e) => numRadius.Enabled = chkRadius.Checked;

            // Buttons
            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(62, 120),
                Size = new Size(80, 28),
                Font = font
            };
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(150, 120),
                Size = new Size(80, 28),
                Font = font
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] {
                lblDir, cboDirection,
                lblAngle, numAngle,
                chkRadius, numRadius,
                btnOk, btnCancel
            });
        }

        public BendDirection Direction => cboDirection.SelectedIndex == 0
            ? BendDirection.Down
            : BendDirection.Up;

        public double BendAngle => (double)numAngle.Value;

        public double? BendRadius => chkRadius.Checked ? (double)numRadius.Value : null;

        public void LoadBend(Bend bend)
        {
            cboDirection.SelectedIndex = bend.Direction == BendDirection.Up ? 1 : 0;
            if (bend.Angle.HasValue)
                numAngle.Value = (decimal)bend.Angle.Value;
            if (bend.Radius.HasValue)
            {
                chkRadius.Checked = true;
                numRadius.Value = (decimal)bend.Radius.Value;
            }
        }
    }
}
