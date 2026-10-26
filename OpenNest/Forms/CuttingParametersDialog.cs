using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;

namespace OpenNest.Forms
{
    public class CuttingParametersDialog : Form
    {
        private readonly CuttingPanel cuttingPanel;

        public CuttingParametersDialog()
        {
            Text = "Cutting Parameters";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            cuttingPanel = new CuttingPanel { Dock = DockStyle.Fill };

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(80, 28),
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 28),
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(8),
            };
            buttonPanel.Paint += (s, e) => e.Graphics.DrawLine(SystemPens.ControlLight, 0, 0, buttonPanel.Width, 0);
            buttonPanel.Controls.Add(btnCancel);
            buttonPanel.Controls.Add(btnOk);

            Controls.Add(cuttingPanel);
            Controls.Add(buttonPanel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            // Fit the whole panel so nothing scrolls, unless the screen is too short for it.
            var content = cuttingPanel.GetPreferredSize(Size.Empty);
            var maxHeight = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80;
            ClientSize = new Size(
                System.Math.Max(380, content.Width + SystemInformation.VerticalScrollBarWidth),
                System.Math.Min(maxHeight, content.Height + buttonPanel.GetPreferredSize(Size.Empty).Height)
            );
        }

        public void LoadParameters(CuttingParameters parameters)
        {
            cuttingPanel.LoadFromParameters(parameters);
        }

        public CuttingParameters GetParameters()
        {
            return cuttingPanel.BuildParameters();
        }
    }
}
