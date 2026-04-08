using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class CuttingParametersDialog : Form
    {
        private readonly CuttingPanel cuttingPanel;

        public CuttingParametersDialog()
        {
            Text = "Cutting Parameters";
            Size = new Size(400, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            cuttingPanel = new CuttingPanel
            {
                Dock = DockStyle.Fill
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(80, 28),
                Location = new Point(220, 6)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 28),
                Location = new Point(305, 6)
            };

            buttonPanel.Controls.Add(btnOk);
            buttonPanel.Controls.Add(btnCancel);

            Controls.Add(cuttingPanel);
            Controls.Add(buttonPanel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
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
