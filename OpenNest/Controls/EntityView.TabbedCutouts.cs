using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Geometry;
using OpenNest.TabbedCutouts;

namespace OpenNest.Controls
{
    public sealed class CutoutTabDragEventArgs : EventArgs
    {
        public CutoutTabDragEventArgs(int cutout, int tab, double center, bool finished)
        {
            Cutout = cutout;
            Tab = tab;
            Center = center;
            Finished = finished;
        }

        public int Cutout { get; }
        public int Tab { get; }

        /// <summary>The new tab center as a distance along the hole's contour, already clamped.</summary>
        public double Center { get; }

        public bool Finished { get; }
    }

    public partial class EntityView
    {
        private bool isPickingTabbedCutout;
        private (int Cutout, int Tab)? draggedCutoutTab;

        /// <summary>Resolved tabbed cutouts to draw, in cutout order; invalid entries are not drawn.</summary>
        public IReadOnlyList<TabbedCutoutLayout> TabbedCutoutLayouts { get; set; } = Array.Empty<TabbedCutoutLayout>();

        public int SelectedTabbedCutoutIndex { get; set; } = -1;

        /// <summary>A line, arc or circle was clicked while picking a hole; the point is where.</summary>
        public event EventHandler<(Entity Entity, Vector Point)> TabbedCutoutPicked;
        public event EventHandler<CutoutTabDragEventArgs> CutoutTabDragged;

        public bool IsPickingTabbedCutout
        {
            get => isPickingTabbedCutout;
            set
            {
                isPickingTabbedCutout = value;
                Cursor = value ? Cursors.Hand : Cursors.Cross;
            }
        }

        private bool IsPicking => IsPickingBendLine || IsPickingBreakoff || IsPickingTabbedCutout;

        private bool HandleTabbedCutoutClick(MouseEventArgs e) =>
            HandlePickClick(e, IsPickingTabbedCutout, entity => entity is Line or Arc or Circle, TabbedCutoutPicked);

        private void RaiseCutoutTabDragged((int Cutout, int Tab) tab, Point location, bool finished)
        {
            var layout = TabbedCutoutLayouts.ElementAtOrDefault(tab.Cutout);
            if (layout is not { IsValid: true } || tab.Tab >= layout.TabCenters.Count)
                return;
            var center = layout.ClampTabCenter(tab.Tab, layout.DistanceOf(PointControlToWorld(location)));
            CutoutTabDragged?.Invoke(this, new CutoutTabDragEventArgs(tab.Cutout, tab.Tab, center, finished));
        }

        private (int, int)? HitTestCutoutTab(Point location) =>
            HitTestTabHandle(location, TabbedCutoutLayouts.Count, c => TabbedCutoutLayouts[c] is { IsValid: true } layout
                ? layout.TabCenters.Select(layout.PointAt)
                : Enumerable.Empty<Vector>());

        private void DrawTabbedCutouts(Graphics g)
        {
            if (TabbedCutoutLayouts.Count == 0)
                return;

            using var slugFill = new SolidBrush(Color.FromArgb(55, 255, 170, 0));
            using var selectedSlugFill = new SolidBrush(Color.FromArgb(95, 255, 170, 0));
            using var cutPen = new Pen(Color.FromArgb(0, 200, 255), 2.5f);
            using var leadPen = new Pen(Color.FromArgb(0, 200, 255), 1.5f);
            using var tabPen = new Pen(Color.Red, 3f);
            using var handleBrush = new SolidBrush(Color.Red);
            using var handlePen = new Pen(Color.White, 1f);

            for (var c = 0; c < TabbedCutoutLayouts.Count; c++)
            {
                var layout = TabbedCutoutLayouts[c];
                if (layout is not { IsValid: true })
                    continue;
                var selected = c == SelectedTabbedCutoutIndex;

                var slug = Outline(layout.Loop);
                if (slug.Length >= 3)
                    g.FillPolygon(selected ? selectedSlugFill : slugFill, slug);

                foreach (var piece in layout.Pieces)
                {
                    var points = Outline(piece);
                    if (points.Length >= 2)
                        g.DrawLines(cutPen, points);
                }

                foreach (var tab in layout.Tabs)
                {
                    var points = Outline(tab);
                    if (points.Length >= 2)
                        g.DrawLines(tabPen, points);
                }

                foreach (var center in layout.TabCenters)
                {
                    DrawTabHandle(g, handleBrush, handlePen, layout.PointAt(center), selected);
                }

                // Each piece is pierced in the slug, at its start.
                foreach (var (entry, slugDirection) in layout.Entries)
                    DrawEntryMark(g, leadPen, entry, slugDirection);
            }
        }
    }
}
