using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Data.MaterialLedger;
using OpenNest.Geometry;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>
    /// The main window's status labels for the active nest document: status text, cursor
    /// location, selection bounds, nest machine and material, MaterialLedger holds of a server nest,
    /// and plate utilization. Plate number, size and
    /// quantity live in the plate view header instead. The location
    /// follows the mouse (the default, throttled), or only left clicks while click mode is on;
    /// the mode persists as a user setting.
    /// </summary>
    internal sealed class MainStatusBar
    {
        // Mouse-follow mode refreshes at most this often; each text change relayouts the strip.
        private const int LocationRefreshIntervalMs = 100;

        // Once the mouse rests this long, the label shows its final point.
        private const int LocationSettleMs = 30;

        private readonly ToolStripStatusLabel status;
        private readonly ToolStripStatusLabel location;
        private readonly ToolStripStatusLabel selection;
        private readonly ToolStripStatusLabel machine;
        private readonly ToolStripStatusLabel material;
        private readonly ToolStripStatusLabel plateUtil;
        private readonly ToolStripStatusLabel ledger;
        private readonly Timer locationSettleTimer;

        private EditNestForm form;
        private bool clickUpdatesLocation = !Settings.Default.StatusBarLocationFollowsMouse;
        private long lastLocationUpdate;

        public MainStatusBar(ToolStripStatusLabel status, ToolStripStatusLabel location,
            ToolStripStatusLabel selection, ToolStripStatusLabel machine, ToolStripStatusLabel material,
            ToolStripStatusLabel plateUtil, ToolStripStatusLabel ledger = null)
        {
            this.ledger = ledger;
            this.status = status;
            this.location = location;
            this.selection = selection;
            this.machine = machine;
            this.material = material;
            this.plateUtil = plateUtil;

            locationSettleTimer = new Timer { Interval = LocationSettleMs };
            locationSettleTimer.Tick += LocationSettleTimer_Tick;
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
            UpdateLedger();

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
            form.HoldStatus.Changed += HoldStatus_Changed;
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
            form.HoldStatus.Changed -= HoldStatus_Changed;
            form = null;
            UpdateLedger();
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
            UpdateLedger();
        }

        /// <summary>The hidden-by-default hold label MainForm inserts after the material label.</summary>
        internal static ToolStripStatusLabel CreateLedgerLabel() => new()
        {
            Name = "ledgerStatusLabel",
            BorderSides = ToolStripStatusLabelBorderSides.Left,
            Padding = new Padding(5, 0, 5, 0),
            Visible = false,
        };

        /// <summary>
        /// Shows the active nest's hold summary only while it applies (a saved server nest with
        /// a ledger configured); the tooltip lists the holds.
        /// </summary>
        public void UpdateLedger()
        {
            if (ledger == null)
                return;
            var holds = form?.HoldStatus;
            ledger.Visible = holds?.IsVisible == true;
            if (!ledger.Visible)
            {
                ledger.Text = string.Empty;
                ledger.ToolTipText = string.Empty;
                return;
            }

            var summary = holds.Summary;
            ledger.Text = summary?.Text ?? "Ledger: checking…";
            ledger.ForeColor = summary?.State is LedgerHoldState.Short or LedgerHoldState.Unavailable
                ? Color.DarkRed
                : SystemColors.ControlText;
            var tip = summary?.ToolTip(form.Nest.Units) ?? "Reading holds from MaterialLedger…";
            ledger.ToolTipText = tip + Environment.NewLine + (holds.IsRefreshing ? "Refreshing…" : "Click to refresh.");
        }

        public void UpdateLocation()
        {
            if (form == null)
            {
                location.Text = string.Empty;
                return;
            }

            var text = string.Format(
                "Location: [{0}, {1}]",
                form.PlateView.CurrentPoint.X.ToString("n4"),
                form.PlateView.CurrentPoint.Y.ToString("n4")
            );
            if (location.Text != text)
                location.Text = text;
        }

        public void UpdatePlate()
        {
            if (form == null)
            {
                machine.Text = string.Empty;
                material.Text = string.Empty;
                plateUtil.Text = string.Empty;
                return;
            }

            var nest = form.Nest;
            machine.Text = FormatMachine(nest.Machine);
            material.Text = FormatMaterial(nest.Material?.Name, nest.Thickness, nest.Units);

            plateUtil.Text = string.Format(
                "Util: {0:P1}",
                form.PlateView.Plate.Utilization()
            );
        }

        /// <summary>Status text for the nest's machine, e.g. "Machine: CL-980".</summary>
        internal static string FormatMachine(NestMachine machine)
        {
            if (machine == null)
                return "Machine: (none)";
            return "Machine: " + (string.IsNullOrWhiteSpace(machine.Name) ? "(unnamed)" : machine.Name.Trim());
        }

        /// <summary>Status text for the nest's material and thickness, e.g. "Material: A36 · 0.25 in".</summary>
        internal static string FormatMaterial(string name, double thickness, Units units)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(name))
                parts.Add(name.Trim());
            if (thickness > 0)
                parts.Add(thickness.ToString("0.####", CultureInfo.CurrentCulture) + " " + UnitsHelper.GetShortString(units));

            return "Material: " + (parts.Count == 0 ? "(none)" : string.Join(" · ", parts));
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
        /// Throttles mouse-follow updates: moves refresh the label at most once per interval,
        /// and a settle timer restarted by each skipped move shows the last point once the
        /// mouse rests. Moves drive the throttle themselves because WM_TIMER is starved while
        /// input and repaints are queued.
        /// </summary>
        private void PlateView_MouseMove(object sender, MouseEventArgs e)
        {
            locationSettleTimer.Stop();
            if (Environment.TickCount64 - lastLocationUpdate >= LocationRefreshIntervalMs)
            {
                UpdateThrottledLocation();
                return;
            }

            locationSettleTimer.Start();
        }

        private void LocationSettleTimer_Tick(object sender, EventArgs e)
        {
            locationSettleTimer.Stop();
            UpdateThrottledLocation();
        }

        private void UpdateThrottledLocation()
        {
            UpdateLocation();
            lastLocationUpdate = Environment.TickCount64;
        }

        private void StopLocationThrottle() => locationSettleTimer.Stop();

        private void PlateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                UpdateLocation();
        }

        private void PlateView_StatusChanged(object sender, EventArgs e) => status.Text = form.PlateView.Status;

        private void PlateView_SelectionChanged(object sender, EventArgs e) => UpdateSelection();

        private void HoldStatus_Changed(object sender, EventArgs e) => UpdateLedger();
    }
}
