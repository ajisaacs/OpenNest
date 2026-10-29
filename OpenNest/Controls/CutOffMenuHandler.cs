using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Actions;

namespace OpenNest.Controls
{
    /// <summary>
    /// Right-click where two cut-offs meet in a <see cref="PlateView"/>, one running through and
    /// the other stopping at it: Swap Through-Cut makes the stopped one run through and stops the
    /// other at it (<see cref="CutOffTrim.Swap"/>). Only in plate select mode, never while
    /// inspecting or previewing a proposal.
    /// </summary>
    internal sealed class CutOffMenuHandler : IDisposable
    {
        private readonly PlateView view;
        private ContextMenuStrip menu;

        public CutOffMenuHandler(PlateView view)
        {
            this.view = view;
        }

        /// <summary>Shows the menu when <paramref name="location"/> is on a cut-off junction.</summary>
        public bool TryShow(Point location, double tolerance)
        {
            if (BuildMenu(location, tolerance) == null)
                return false;
            menu.Show(view, location);
            return true;
        }

        /// <summary>
        /// The menu for the junction at <paramref name="location"/> (control coordinates), or null
        /// when there is none or cut-offs cannot be edited from here.
        /// </summary>
        internal ContextMenuStrip BuildMenu(Point location, double tolerance)
        {
            var plate = view.Plate;
            if (
                plate?.CutOffs == null
                || !view.AllowSelect
                || view.InspectionOnly
                || view.HasProposalDisplay
                || view.Actions.CurrentAction is not ActionSelect
            )
                return null;

            if (CutOffTrim.JunctionAt(plate, view.PointControlToWorld(location), tolerance) is not var (through, stopper))
                return null;

            menu ??= new ContextMenuStrip();
            while (menu.Items.Count > 0)
                menu.Items[0].Dispose();
            menu.Items.Add("Swap Through-Cut", null, (_, _) => Swap(plate, through, stopper));
            return menu;
        }

        private void Swap(Plate plate, CutOff through, CutOff stopper)
        {
            // The view may have moved to another sheet while the menu was open.
            if (!ReferenceEquals(plate, view.Plate) || view.IsFillInProgress)
                return;

            var edit = CutOffTrim.Swap(plate, through, stopper, view.CutOffSettings);
            if (view.CutOffs.Apply(edit, "Swap Through-Cut"))
                view.Status = edit.Notice ?? "Swapped the through-cut";
            else
                view.Status = edit.Refusal;
        }

        public void Dispose() => menu?.Dispose();
    }
}
