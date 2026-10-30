using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Breakoffs;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public sealed class TabDragEventArgs : EventArgs
    {
        public TabDragEventArgs(int breakoff, int tab, double center, bool finished)
        {
            Breakoff = breakoff;
            Tab = tab;
            Center = center;
            Finished = finished;
        }

        public int Breakoff { get; }
        public int Tab { get; }

        /// <summary>The new tab center as a distance along the break line, already clamped.</summary>
        public double Center { get; }

        public bool Finished { get; }
    }

    public partial class EntityView
    {
        private const int TabHandleRadius = 7;

        private bool isPickingBreakoff;
        private (int Breakoff, int Tab)? draggedTab;

        /// <summary>Resolved breakoffs to draw, in breakoff order; null entries are not drawn.</summary>
        public IReadOnlyList<BreakoffLayout> BreakoffLayouts { get; set; } = Array.Empty<BreakoffLayout>();

        public int SelectedBreakoffIndex { get; set; } = -1;

        /// <summary>Where each break cut is entered and the direction into its scrap, in world coordinates.</summary>
        public IReadOnlyList<(Vector Entry, Vector ScrapDirection)> BreakCutEntries { get; set; } =
            Array.Empty<(Vector, Vector)>();

        /// <summary>A line or arc was clicked while picking a joining line; the point is where.</summary>
        public event EventHandler<(Entity Entity, Vector Point)> BreakoffEdgePicked;
        public event EventHandler<TabDragEventArgs> TabDragged;

        public bool IsPickingBreakoff
        {
            get => isPickingBreakoff;
            set
            {
                isPickingBreakoff = value;
                Cursor = value ? Cursors.Hand : Cursors.Cross;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || IsPicking)
                return;
            draggedTab = HitTestTab(e.Location);
            if (draggedTab == null)
                draggedCutoutTab = HitTestCutoutTab(e.Location);
            if (draggedTab == null && draggedCutoutTab == null)
                draggedDimension = HitTestDimension(e.Location);
            if (draggedTab == null && draggedCutoutTab == null && draggedDimension == null)
                BeginWindowSelect(e);
            else
                suppressNextClick = true; // A tab press or drag does not change the selection.
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            EndWindowSelect(e);
            draggedDimension = null;
            if (draggedCutoutTab is { } cutoutTab)
            {
                draggedCutoutTab = null;
                RaiseCutoutTabDragged(cutoutTab, e.Location, finished: true);
            }
            if (draggedTab is not { } tab)
                return;
            draggedTab = null;
            RaiseTabDragged(tab, e.Location, finished: true);
        }

        private void HandleBreakoffMouseMove(MouseEventArgs e)
        {
            if (draggedTab is { } tab)
                RaiseTabDragged(tab, e.Location, finished: false);
            else if (draggedCutoutTab is { } cutoutTab)
                RaiseCutoutTabDragged(cutoutTab, e.Location, finished: false);
            else if (draggedDimension is { } dimension)
                RaiseDimensionDragged(dimension, e.Location);
            else if (e.Button == MouseButtons.None && !IsPicking)
            {
                if (HitTestTab(e.Location) != null || HitTestCutoutTab(e.Location) != null)
                    Cursor = Cursors.SizeAll;
                else if (HitTestDimension(e.Location) is { } hover)
                    Cursor = DimensionCursor(hover);
                else
                    Cursor = Cursors.Cross;
            }
        }

        private bool HandleBreakoffClick(MouseEventArgs e)
        {
            if (!IsPickingBreakoff || e.Button != MouseButtons.Left)
                return false;
            var entity = HitTestEdge(e.Location);
            if (entity != null)
                BreakoffEdgePicked?.Invoke(this, (entity, PointControlToWorld(e.Location)));
            return true;
        }

        private void RaiseTabDragged((int Breakoff, int Tab) tab, Point location, bool finished)
        {
            var layout = BreakoffLayouts.ElementAtOrDefault(tab.Breakoff);
            if (layout is not { IsValid: true } || tab.Tab >= layout.TabCenters.Count)
                return;
            var center = layout.ClampTabCenter(tab.Tab, layout.DistanceOf(PointControlToWorld(location)));
            TabDragged?.Invoke(this, new TabDragEventArgs(tab.Breakoff, tab.Tab, center, finished));
        }

        private (int, int)? HitTestTab(Point location)
        {
            for (var b = 0; b < BreakoffLayouts.Count; b++)
            {
                var layout = BreakoffLayouts[b];
                if (layout is not { IsValid: true })
                    continue;
                for (var t = 0; t < layout.TabCenters.Count; t++)
                {
                    var handle = PointWorldToControl(layout.PointAt(layout.TabCenters[t]));
                    var dx = handle.X - location.X;
                    var dy = handle.Y - location.Y;
                    if (dx * dx + dy * dy <= TabHandleRadius * TabHandleRadius)
                        return (b, t);
                }
            }
            return null;
        }

        private Entity HitTestEdge(Point controlPoint)
        {
            var worldPoint = PointControlToWorld(controlPoint);
            var tolerance = LengthGuiToWorld(6);
            Entity best = null;
            var bestDistance = double.MaxValue;
            foreach (var entity in Entities)
            {
                if (entity is not (Line or Arc) || !entity.IsVisible || entity.Layer?.IsVisible == false)
                    continue;
                var distance = worldPoint.DistanceTo(entity.ClosestPointTo(worldPoint));
                if (distance < tolerance && distance < bestDistance)
                {
                    best = entity;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void DrawBreakoffs(Graphics g)
        {
            if (BreakoffLayouts.Count == 0)
                return;

            using var scrapFill = new SolidBrush(Color.FromArgb(55, 255, 170, 0));
            using var selectedScrapFill = new SolidBrush(Color.FromArgb(95, 255, 170, 0));
            using var cutPen = new Pen(Color.FromArgb(0, 200, 255), 2.5f);
            using var leadPen = new Pen(Color.FromArgb(0, 200, 255), 1.5f);
            using var tabPen = new Pen(Color.Red, 3f);
            using var handleBrush = new SolidBrush(Color.Red);
            using var handlePen = new Pen(Color.White, 1f);

            for (var b = 0; b < BreakoffLayouts.Count; b++)
            {
                var layout = BreakoffLayouts[b];
                if (layout is not { IsValid: true })
                    continue;
                var selected = b == SelectedBreakoffIndex;

                var scrap = Outline(layout.Scrap);
                if (scrap.Length >= 3)
                    g.FillPolygon(selected ? selectedScrapFill : scrapFill, scrap);

                foreach (var cut in layout.Cuts)
                {
                    var points = Outline(cut);
                    if (points.Length >= 2)
                        g.DrawLines(cutPen, points);
                }

                foreach (var center in layout.TabCenters)
                {
                    var from = PointWorldToGraph(layout.PointAt(center - layout.TabWidth / 2));
                    var to = PointWorldToGraph(layout.PointAt(center + layout.TabWidth / 2));
                    g.DrawLine(tabPen, from, to);
                    var handle = PointWorldToGraph(layout.PointAt(center));
                    var size = selected ? TabHandleRadius : TabHandleRadius - 2;
                    var rect = new RectangleF(handle.X - size, handle.Y - size, size * 2, size * 2);
                    g.FillEllipse(handleBrush, rect);
                    g.DrawEllipse(handlePen, rect);
                }
            }

            DrawEntryMarks(g, leadPen);
        }

        // A short stroke from the pierce side into each cut's entry, a fixed size on screen.
        private void DrawEntryMarks(Graphics g, Pen pen)
        {
            foreach (var (entry, scrap) in BreakCutEntries)
            {
                var at = PointWorldToGraph(entry);
                var pierce = PointWorldToGraph(entry + scrap * LengthGuiToWorld(12));
                g.DrawLine(pen, pierce, at);
                g.DrawEllipse(pen, pierce.X - 2.5f, pierce.Y - 2.5f, 5f, 5f);
            }
        }

        private PointF[] Outline(IReadOnlyList<Entity> path)
        {
            var points = new List<PointF>();
            foreach (var entity in path)
            {
                if (entity is Arc arc)
                    points.AddRange(arc.ToPoints(System.Math.Max(2, arc.SegmentsForTolerance(LengthGuiToWorld(0.5f))))
                        .Select(PointWorldToGraph));
                else if (entity is Line line)
                {
                    points.Add(PointWorldToGraph(line.StartPoint));
                    points.Add(PointWorldToGraph(line.EndPoint));
                }
            }
            return points.ToArray();
        }
    }
}
