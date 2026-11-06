using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;

namespace OpenNest.Controls
{
    /// <summary>
    /// Right-click on a part in a <see cref="PlateView"/>: joins the selected parts on a common
    /// line or removes their groups (see docs/common-line-cutting.md). The commands are the
    /// Plate &gt; Arrange ones, supplied by the host through <see cref="PlateView.JoinCommonLineCommand"/>
    /// and <see cref="PlateView.RemoveCommonLineCommand"/>. Only in plate select mode, never
    /// while inspecting or previewing a proposal.
    /// </summary>
    internal sealed class PartMenuHandler : IDisposable
    {
        private readonly PlateView view;
        private ContextMenuStrip menu;

        public PartMenuHandler(PlateView view)
        {
            this.view = view;
        }

        /// <summary>Shows the menu when <paramref name="location"/> is on a selected part.</summary>
        public bool TryShow(Point location)
        {
            if (BuildMenu(location) == null)
                return false;
            menu.Show(view, location);
            return true;
        }

        /// <summary>
        /// The menu for the selected part at <paramref name="location"/> (control coordinates),
        /// or null when there is none, the host offers no commands or parts cannot be edited.
        /// </summary>
        internal ContextMenuStrip BuildMenu(Point location)
        {
            var plate = view.Plate;
            if (
                plate == null
                || view.InspectionOnly
                || view.IsFillInProgress
                || !view.AllowSelect
                || view.HasProposalDisplay
                || view.Actions.CurrentAction is not ActionSelect
                || (view.JoinCommonLineCommand == null && view.RemoveCommonLineCommand == null)
            )
                return null;

            var part = view.GetPartAtControlPoint(location);
            if (part == null || !view.SelectedParts.Contains(part))
                return null;

            var selected = view.SelectedParts.Select(p => p.BasePart).ToList();
            var groups = selected.Select(plate.CommonLineGroupOf).ToList();
            // Join needs two units to bring together: two loose parts, or a group and anything outside it.
            var units = groups.Where(g => g != null).Distinct().Count() + groups.Count(g => g == null);
            var anyGrouped = groups.Any(g => g != null);

            menu ??= new ContextMenuStrip();
            while (menu.Items.Count > 0)
                menu.Items[0].Dispose();
            if (view.JoinCommonLineCommand is { } join)
                menu.Items.Add("Join Common Line", null, (_, _) => join()).Enabled = units >= 2;
            if (view.RemoveCommonLineCommand is { } remove)
                menu.Items.Add("Remove Common Line", null, (_, _) => remove()).Enabled = anyGrouped;
            return menu;
        }

        public void Dispose() => menu?.Dispose();
    }
}
