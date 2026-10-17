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

        public event EventHandler<Entity> BreakoffEdgePicked;
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
            if (e.Button == MouseButtons.Left && !IsPickingBendLine && !IsPickingBreakoff)
                draggedTab = HitTestTab(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggedTab is not { } tab || e.Button != MouseButtons.Left)
                return;
            draggedTab = null;
            RaiseTabDragged(tab, e.Location, finished: true);
        }

        private void HandleBreakoffMouseMove(MouseEventArgs e)
        {
            if (draggedTab is { } tab)
                RaiseTabDragged(tab, e.Location, finished: false);
            else if (e.Button == MouseButtons.None && !IsPickingBendLine && !IsPickingBreakoff)
                Cursor = HitTestTab(e.Location) != null ? Cursors.SizeAll : Cursors.Cross;
        }

        private bool HandleBreakoffClick(MouseEventArgs e)
        {
            if (!IsPickingBreakoff || e.Button != MouseButtons.Left)
                return false;
            var entity = HitTestEdge(e.Location);
            if (entity != null)
                BreakoffEdgePicked?.Invoke(this, entity);
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

            using var slotFill = new SolidBrush(Color.FromArgb(70, 0, 200, 255));
            using var slotPen = new Pen(Color.FromArgb(0, 200, 255), 1f);
            using var selectedSlotPen = new Pen(Color.White, 1.5f);
            using var tabPen = new Pen(Color.Red, 3f);
            using var handleBrush = new SolidBrush(Color.Red);
            using var handlePen = new Pen(Color.White, 1f);

            for (var b = 0; b < BreakoffLayouts.Count; b++)
            {
                var layout = BreakoffLayouts[b];
                if (layout is not { IsValid: true })
                    continue;
                var selected = b == SelectedBreakoffIndex;

                foreach (var slot in layout.Slots)
                {
                    var points = SlotOutline(slot);
                    if (points.Length < 3)
                        continue;
                    g.FillPolygon(slotFill, points);
                    g.DrawPolygon(selected ? selectedSlotPen : slotPen, points);
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
        }

        private PointF[] SlotOutline(IReadOnlyList<Entity> slot)
        {
            var points = new List<PointF>();
            foreach (var entity in slot)
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
