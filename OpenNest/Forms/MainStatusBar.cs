using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Geometry;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>
    /// The main window's status labels for the active nest document: status text, cursor
    /// location, selection bounds and plate index/size/quantity/utilization. The location
    /// follows the mouse (the default, throttled), or only left clicks while click mode is on;
    /// the mode persists as a user setting.
    /// </summary>
    internal sealed class MainStatusBar
    {
        // Mouse-follow mode refreshes at most this often; each text change relayouts the strip.
        private const int LocationRefreshIntervalMs = 100;

        private readonly ToolStripStatusLabel status;
        private readonly ToolStripStatusLabel location;
        private readonly ToolStripStatusLabel selection;
        private readonly ToolStripStatusLabel plateIndex;
        private readonly ToolStripStatusLabel plateSize;
        private readonly ToolStripStatusLabel plateQty;
        private readonly ToolStripStatusLabel plateUtil;
        private readonly Timer locationTimer;

        private EditNestForm form;
        private bool clickUpdatesLocation = !Settings.Default.StatusBarLocationFollowsMouse;
        private bool locationPending;

        public MainStatusBar(ToolStripStatusLabel status, ToolStripStatusLabel location,
            ToolStripStatusLabel selection, ToolStripStatusLabel plateIndex, ToolStripStatusLabel plateSize,
            ToolStripStatusLabel plateQty, ToolStripStatusLabel plateUtil)
        {
            this.status = status;
            this.location = location;
            this.selection = selection;
            this.plateIndex = plateIndex;
            this.plateSize = plateSize;
            this.plateQty = plateQty;
            this.plateUtil = plateUtil;

            locationTimer = new Timer { Interval = LocationRefreshIntervalMs };
            locationTimer.Tick += LocationTimer_Tick;
        }

        /// <summary>
        /// Follows <paramref name="editForm"/>'s plate view after detaching from the previous
        /// document; null clears every label.
        /// </summary>
        public void Bind(EditNestForm editForm)
        {
            Unbind();
            form = editForm;
            UpdatePlate();
            UpdateLocation();

            if (form == null)
            {
                status.Text = "";
                selection.Text = "";
                return;
            }

            ApplyLocationMode();
            UpdateSelection();
            var view = form.PlateView;
            view.StatusChanged += PlateView_StatusChanged;
            view.SelectionChanged += PlateView_SelectionChanged;
            view.PartAdded += PlateView_PartAdded;
            view.PartRemoved += PlateView_PartRemoved;
            status.Text = view.Status;
        }

        public void Unbind()
        {
            StopLocationThrottle();
            if (form == null)
                return;

            var view = form.PlateView;
            view.MouseMove -= PlateView_MouseMove;
            view.MouseClick -= PlateView_MouseClick;
            view.StatusChanged -= PlateView_StatusChanged;
            view.SelectionChanged -= PlateView_SelectionChanged;
            view.PartAdded -= PlateView_PartAdded;
            view.PartRemoved -= PlateView_PartRemoved;
            form = null;
        }

        public void ToggleLocationMode()
        {
            clickUpdatesLocation = !clickUpdatesLocation;
            Settings.Default.StatusBarLocationFollowsMouse = !clickUpdatesLocation;
            Settings.Default.Save();
            ApplyLocationMode();
        }

        public void UpdateAll()
        {
            UpdateLocation();
            UpdatePlate();
            UpdateSelection();
        }

        public void UpdateLocation()
        {
            if (form == null)
            {
                location.Text = string.Empty;
                return;
            }

            location.Text = string.Format(
                "Location: [{0}, {1}]",
                form.PlateView.CurrentPoint.X.ToString("n4"),
                form.PlateView.CurrentPoint.Y.ToString("n4")
            );
        }

        public void UpdatePlate()
        {
            if (form == null)
            {
                plateIndex.Text = string.Empty;
                plateSize.Text = string.Empty;
                plateQty.Text = string.Empty;
                plateUtil.Text = string.Empty;
                return;
            }

            // Same numbering as the plate header: the trailing empty "new plate" workspace isn't counted.
            var plates = form.Nest.Plates;
            var shown = PlateDisplayNumbering.DisplayedPlateNumber(plates, form.PlateManager.CurrentIndex);
            plateIndex.Text = shown == null
                ? "Plate: new"
                : string.Format("Plate: {0} of {1}", shown.Value, PlateDisplayNumbering.DisplayedPlateCount(plates));

            plateSize.Text = string.Format("Size: {0}", form.PlateView.Plate.Size);

            plateQty.Text = string.Format(
                "Qty: {0}",
                form.PlateView.Plate.Quantity
            );

            plateUtil.Text = string.Format(
                "Util: {0:P1}",
                form.PlateView.Plate.Utilization()
            );
        }

        public void UpdateSelection()
        {
            if (form == null || form.PlateView.SelectedParts.Count == 0)
            {
                selection.Text = string.Empty;
                return;
            }

            var selected = form.PlateView.SelectedParts;

            if (selected.Count == 1)
            {
                var box = selected[0].BoundingBox;
                selection.Text = string.Format(
                    "Selected: [{0}, {1}] {2} x {3}",
                    box.X.ToString("n4"),
                    box.Y.ToString("n4"),
                    box.Width.ToString("n4"),
                    box.Length.ToString("n4")
                );
            }
            else
            {
                var bounds = selected.Select(p => p.BasePart).ToList().GetBoundingBox();
                selection.Text = string.Format(
                    "Selected ({0}): [{1}, {2}] {3} x {4}",
                    selected.Count,
                    bounds.X.ToString("n4"),
                    bounds.Y.ToString("n4"),
                    bounds.Width.ToString("n4"),
                    bounds.Length.ToString("n4")
                );
            }
        }

        private void ApplyLocationMode()
        {
            if (form == null)
                return;

            StopLocationThrottle();
            var view = form.PlateView;
            // Detach both first so repeated calls never stack handlers.
            view.MouseMove -= PlateView_MouseMove;
            view.MouseClick -= PlateView_MouseClick;
            if (clickUpdatesLocation)
            {
                location.ForeColor = Color.Gray;
                view.MouseClick += PlateView_MouseClick;
            }
            else
            {
                location.ForeColor = Color.Black;
                view.MouseMove += PlateView_MouseMove;
            }
        }

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e) => UpdatePlate();

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e) => UpdatePlate();

        /// <summary>
        /// Throttles mouse-follow updates: the first move shows at once, later moves inside
        /// the interval collapse into one trailing update so the label settles on the last point.
        /// </summary>
        private void PlateView_MouseMove(object sender, MouseEventArgs e)
        {
            if (locationTimer.Enabled)
            {
                locationPending = true;
                return;
            }

            UpdateLocation();
            locationTimer.Start();
        }

        private void LocationTimer_Tick(object sender, EventArgs e)
        {
            if (!locationPending)
            {
                locationTimer.Stop();
                return;
            }

            locationPending = false;
            UpdateLocation();
        }

        private void StopLocationThrottle()
        {
            locationTimer.Stop();
            locationPending = false;
        }

        private void PlateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                UpdateLocation();
        }

        private void PlateView_StatusChanged(object sender, EventArgs e) => status.Text = form.PlateView.Status;

        private void PlateView_SelectionChanged(object sender, EventArgs e) => UpdateSelection();
    }
}
