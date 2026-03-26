using OpenNest.Controls;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class RemnantViewerForm : Form
    {
        private ListView listView;
        private CheckBox filterCheckBox;
        private PlateView plateView;
        private RemnantFinder finder;
        private double minDim;
        private List<TieredRemnant> remnants = new();
        private int selectedIndex = -1;

        public RemnantViewerForm()
        {
            Text = "Remnants";
            Size = new System.Drawing.Size(360, 400);
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;

            filterCheckBox = new CheckBox
            {
                Text = "Filter by part size",
                Checked = true,
                Dock = DockStyle.Top,
                Padding = new Padding(4, 2, 0, 2),
            };
            filterCheckBox.CheckedChanged += FilterCheckBox_CheckedChanged;

            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
            };

            listView.Columns.Add("P", 28, HorizontalAlignment.Center);
            listView.Columns.Add("Size", 110, HorizontalAlignment.Left);
            listView.Columns.Add("Area", 65, HorizontalAlignment.Right);
            listView.Columns.Add("Location", 110, HorizontalAlignment.Left);

            listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            Controls.Add(listView);
            Controls.Add(filterCheckBox);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        public void LoadRemnants(RemnantFinder finder, double minDim, PlateView view)
        {
            plateView = view;
            this.finder = finder;
            this.minDim = minDim;

            Refresh();
        }

        private void FilterCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (finder != null)
                Refresh();
        }

        private new void Refresh()
        {
            var dim = filterCheckBox.Checked ? minDim : 0;
            remnants = finder.FindTieredRemnants(dim);
            selectedIndex = -1;

            listView.BeginUpdate();
            listView.Items.Clear();

            foreach (var tr in remnants)
            {
                var item = new ListViewItem(tr.Priority.ToString());
                item.SubItems.Add($"{tr.Box.Width:F2} x {tr.Box.Length:F2}");
                item.SubItems.Add($"{tr.Box.Area():F1}");
                item.SubItems.Add($"({tr.Box.X:F2}, {tr.Box.Y:F2})");

                switch (tr.Priority)
                {
                    case 0: item.BackColor = Color.FromArgb(220, 255, 220); break;
                    case 1: item.BackColor = Color.FromArgb(255, 255, 210); break;
                    default: item.BackColor = Color.FromArgb(255, 220, 220); break;
                }

                listView.Items.Add(item);
            }

            listView.EndUpdate();
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (plateView == null)
                return;

            if (listView.SelectedIndices.Count == 0)
            {
                selectedIndex = -1;
                plateView.DebugRemnants = null;
                plateView.DebugRemnantPriorities = null;
                return;
            }

            selectedIndex = listView.SelectedIndices[0];

            if (selectedIndex >= 0 && selectedIndex < remnants.Count)
            {
                var tr = remnants[selectedIndex];
                plateView.DebugRemnants = new List<Box> { tr.Box };
                plateView.DebugRemnantPriorities = new List<int> { tr.Priority };
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (plateView != null)
            {
                plateView.DebugRemnants = null;
                plateView.DebugRemnantPriorities = null;
            }

            base.OnFormClosing(e);
        }
    }
}
