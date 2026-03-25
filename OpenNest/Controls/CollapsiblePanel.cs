using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class CollapsiblePanel : Panel
    {
        private readonly Panel headerPanel;
        private readonly Label headerLabel;
        private readonly Label chevronLabel;
        private readonly Panel contentPanel;
        private bool isExpanded;
        private int expandedHeight;

        public CollapsiblePanel()
        {
            isExpanded = true;
            expandedHeight = 200;

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(240, 240, 240),
                Cursor = Cursors.Hand
            };

            chevronLabel = new Label
            {
                Text = "▾",
                AutoSize = false,
                Size = new Size(20, 28),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f)
            };

            headerLabel = new Label
            {
                Text = "Section",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            headerPanel.Controls.Add(headerLabel);
            headerPanel.Controls.Add(chevronLabel);
            headerPanel.Click += (s, e) => Toggle();
            headerLabel.Click += (s, e) => Toggle();
            chevronLabel.Click += (s, e) => Toggle();

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            Controls.Add(contentPanel);
            Controls.Add(headerPanel);
        }

        public string HeaderText
        {
            get => headerLabel.Text;
            set => headerLabel.Text = value;
        }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                isExpanded = value;
                UpdateLayout();
            }
        }

        public int ExpandedHeight
        {
            get => expandedHeight;
            set
            {
                expandedHeight = value;
                if (isExpanded) Height = value;
            }
        }

        public Panel ContentPanel => contentPanel;

        public void Toggle()
        {
            isExpanded = !isExpanded;
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            contentPanel.Visible = isExpanded;
            chevronLabel.Text = isExpanded ? "▾" : "▸";
            Height = isExpanded ? expandedHeight : headerPanel.Height;
        }
    }
}
