using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Actions;

namespace OpenNest.Controls
{
    /// <summary>
    /// Right-click on a machine clamp in a <see cref="PlateView"/>: takes that clamp off the
    /// current sheet or puts it back (<see cref="Plate.SetClampRemoved"/>). Only in plate
    /// select mode, never while inspecting or previewing a proposal; the change itself is
    /// refused while a fill runs or <see cref="PlateView.CanEditClamps"/> says a nest run is
    /// using the clamps.
    /// </summary>
    internal sealed class ClampMenuHandler : IDisposable
    {
        private readonly PlateView view;
        private ContextMenuStrip menu;

        public ClampMenuHandler(PlateView view)
        {
            this.view = view;
        }

        /// <summary>Shows the menu when <paramref name="location"/> is on a rail clamp.</summary>
        public bool TryShow(Point location, double tolerance)
        {
            if (BuildMenu(location, tolerance) == null)
                return false;
            menu.Show(view, location);
            return true;
        }

        /// <summary>
        /// The menu for the rail clamp at <paramref name="location"/> (control coordinates),
        /// or null when there is none or clamps cannot be edited from here.
        /// </summary>
        internal ContextMenuStrip BuildMenu(Point location, double tolerance)
        {
            var plate = view.Plate;
            if (
                plate?.ClampRail == null
                || !view.AllowSelect
                || view.InspectionOnly
                || view.HasProposalDisplay
                || view.Actions.CurrentAction is not ActionSelect
            )
                return null;

            var clamp = plate.RailClampAt(view.PointControlToWorld(location), tolerance);
            if (clamp == null)
                return null;

            var removed = plate.RemovedClamps.Contains(clamp.Id);
            menu ??= new ContextMenuStrip();
            while (menu.Items.Count > 0)
                menu.Items[0].Dispose();
            var item = menu.Items.Add(
                removed ? $"Put {clamp.Id} Back on This Sheet" : $"Remove {clamp.Id} from This Sheet"
            );
            item.Click += (_, _) => Apply(plate, clamp.Id, !removed);
            if (view.ClampNest != null)
                menu.Items.Add("Edit Clamps", null, (_, _) => view.SetAction(typeof(ActionClampEdit)));
            return menu;
        }

        private void Apply(Plate plate, string id, bool remove)
        {
            // The view may have moved to another sheet while the menu was open.
            if (!ReferenceEquals(plate, view.Plate))
                return;

            // Clamps feed nesting and validation: never change them under a running operation.
            if (view.IsFillInProgress || view.CanEditClamps?.Invoke() == false)
            {
                MessageBox.Show(
                    view.FindForm(),
                    "Finish or cancel the current nesting or plate action first.",
                    "Clamps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            using var change = view.BeginChange(remove ? "Remove Clamp" : "Put Clamp Back");
            var changed = plate.SetClampRemoved(id, remove);
            if (changed)
                change.MarkChanged();
            change.Complete();
            if (!changed)
                return;

            view.Status = remove
                ? $"Removed {id} from this sheet (+{plate.ClampRail.RemovalMinutes:0.#} min cut time)"
                : $"Put {id} back on this sheet";
            view.Invalidate();
        }

        public void Dispose() => menu?.Dispose();
    }
}
