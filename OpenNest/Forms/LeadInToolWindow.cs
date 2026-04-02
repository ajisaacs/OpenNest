using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class LeadInToolWindow : Form
    {
        private readonly CuttingPanel cuttingPanel;

        public CuttingPanel CuttingPanel => cuttingPanel;

        public event EventHandler ParametersChanged
        {
            add => cuttingPanel.ParametersChanged += value;
            remove => cuttingPanel.ParametersChanged -= value;
        }

        public event EventHandler AutoAssignClicked
        {
            add => cuttingPanel.AutoAssignClicked += value;
            remove => cuttingPanel.AutoAssignClicked -= value;
        }

        public LeadInToolWindow()
        {
            Text = "Lead-In Properties";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(300, 400);
            Size = new Size(400, 580);

            cuttingPanel = new CuttingPanel
            {
                Dock = DockStyle.Fill,
                ShowAutoAssign = true
            };
            Controls.Add(cuttingPanel);

            RestorePosition();
        }

        public CuttingParameters BuildParameters() => cuttingPanel.BuildParameters();

        public void LoadFromParameters(CuttingParameters p) => cuttingPanel.LoadFromParameters(p);

        public ContourType? ActiveContourType
        {
            get => cuttingPanel.ActiveContourType;
            set => cuttingPanel.ActiveContourType = value;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }

            SavePosition();
            base.OnFormClosing(e);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (WindowState == FormWindowState.Normal)
                SavePosition();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Normal)
                SavePosition();
        }

        private void SavePosition()
        {
            if (WindowState != FormWindowState.Normal)
                return;

            Properties.Settings.Default.LeadInToolWindowLocation = Location;
            Properties.Settings.Default.LeadInToolWindowSize = Size;
            Properties.Settings.Default.Save();
        }

        private void RestorePosition()
        {
            var savedLocation = Properties.Settings.Default.LeadInToolWindowLocation;
            var savedSize = Properties.Settings.Default.LeadInToolWindowSize;

            if (savedSize.Width > 0 && savedSize.Height > 0)
                Size = savedSize;

            if (savedLocation.X != 0 || savedLocation.Y != 0)
            {
                var screen = Screen.FromPoint(savedLocation);
                if (screen.WorkingArea.Contains(savedLocation))
                    Location = savedLocation;
                else
                    CenterToParent();
            }
        }
    }
}
