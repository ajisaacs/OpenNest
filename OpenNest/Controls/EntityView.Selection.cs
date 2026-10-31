using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public enum EntityAction
    {
        Cut,
        Etch,
        Display,
        Exclude,
        Include,
        AddRadiusDimension,
        AddDiameterDimension,
    }

    public sealed class EntityActionEventArgs : EventArgs
    {
        public EntityActionEventArgs(EntityAction action, IReadOnlyList<Entity> entities)
        {
            Action = action;
            Entities = entities;
        }

        public EntityAction Action { get; }
        public IReadOnlyList<Entity> Entities { get; }
    }

    public partial class EntityView
    {
        private const int WindowSelectThreshold = 4;
        private const int ArcSegments = 64;

        private readonly HashSet<Entity> selectedEntities = new HashSet<Entity>();
        private Point? windowStart;
        private Point windowEnd;
        private bool isWindowSelecting;
        private bool suppressNextClick;

        /// <summary>Role overrides by entity ID, for drawing; entities not listed keep their layer's role.</summary>
        public IReadOnlyDictionary<Guid, EntityRole> EntityRoles { get; set; }

        /// <summary>
        /// IDs of entities the user excluded one by one. They are drawn faintly so they can be
        /// selected and included again; entities hidden by a layer, color or line type filter are not.
        /// </summary>
        public IReadOnlySet<Guid> ExcludedIds { get; set; }

        public IReadOnlyCollection<Entity> SelectedEntities => selectedEntities;

        public event EventHandler<EntityActionEventArgs> EntityActionRequested;

        public void ClearSelection()
        {
            if (selectedEntities.Count == 0)
                return;
            selectedEntities.Clear();
            Invalidate();
        }

        private EntityRole RoleOf(Entity entity) => Converters.EntityRoles.Of(entity, EntityRoles);

        private bool IsExcluded(Entity entity) => ExcludedIds != null && ExcludedIds.Contains(entity.Id);

        // Entities the user can select: everything drawn, plus excluded ones (drawn faintly).
        private bool IsSelectable(Entity entity) =>
            entity is Line or Arc or Circle
            && entity.Layer is { IsVisible: true }
            && (entity.IsVisible || IsExcluded(entity));

        private void BeginWindowSelect(MouseEventArgs e)
        {
            windowStart = e.Location;
            windowEnd = e.Location;
            isWindowSelecting = false;
        }

        private void HandleSelectionMouseMove(MouseEventArgs e)
        {
            if (windowStart is not { } start || (e.Button & MouseButtons.Left) == 0)
                return;
            windowEnd = e.Location;
            if (!isWindowSelecting
                && (System.Math.Abs(e.X - start.X) > WindowSelectThreshold
                    || System.Math.Abs(e.Y - start.Y) > WindowSelectThreshold))
                isWindowSelecting = true;
            if (isWindowSelecting)
                Invalidate();
        }

        private void EndWindowSelect(MouseEventArgs e)
        {
            var start = windowStart;
            windowStart = null;
            if (!isWindowSelecting || start == null)
                return;
            isWindowSelecting = false;
            suppressNextClick = true;

            var a = PointControlToWorld(start.Value);
            var b = PointControlToWorld(e.Location);
            var rect = new Box(
                System.Math.Min(a.X, b.X),
                System.Math.Min(a.Y, b.Y),
                System.Math.Abs(a.X - b.X),
                System.Math.Abs(a.Y - b.Y));

            // Dragging right selects what lies inside the window; dragging left also selects
            // what the window crosses.
            var crossing = e.X < start.Value.X;
            var hits = Entities.Where(en => IsSelectable(en)
                && (crossing ? Crosses(en, rect) : rect.Contains(en.BoundingBox)));

            if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                selectedEntities.Clear();
            foreach (var entity in hits)
                selectedEntities.Add(entity);
            Invalidate();
        }

        /// <summary>Handles a click for selection; returns true when it was consumed.</summary>
        private bool HandleSelectionClick(MouseEventArgs e)
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return e.Button == MouseButtons.Left;
            }
            if (IsPicking)
                return false;

            var hit = HitTestEntity(e.Location);
            var additive = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;

            if (e.Button == MouseButtons.Left)
            {
                if (hit == null)
                {
                    if (!additive)
                        ClearSelection();
                    return true;
                }
                if (!additive)
                    selectedEntities.Clear();
                if (additive && selectedEntities.Contains(hit))
                    selectedEntities.Remove(hit);
                else
                    selectedEntities.Add(hit);
                Invalidate();
                return true;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (hit != null && !selectedEntities.Contains(hit))
                {
                    if (!additive)
                        selectedEntities.Clear();
                    selectedEntities.Add(hit);
                    Invalidate();
                }
                if (selectedEntities.Count == 0)
                    return false;
                ShowEntityContextMenu(e.Location);
                return true;
            }

            return false;
        }

        private void ShowEntityContextMenu(Point location)
        {
            var selection = selectedEntities.ToList();
            var roles = selection.Select(RoleOf).Distinct().ToList();
            var current = roles.Count == 1 ? roles[0] : (EntityRole?)null;
            var noun = selection.Count == 1 ? "entity" : $"{selection.Count} entities";

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripLabel(noun) { ForeColor = SystemColors.GrayText });
            AddRoleItem(menu, "Cut", EntityAction.Cut, current == EntityRole.Cut, selection);
            AddRoleItem(menu, "Etch", EntityAction.Etch, current == EntityRole.Etch, selection);
            AddRoleItem(menu, "Display Only", EntityAction.Display, current == EntityRole.Display, selection);
            menu.Items.Add(new ToolStripSeparator());

            if (selection.Any(en => !IsExcluded(en)))
            {
                var exclude = new ToolStripMenuItem("Exclude") { ShortcutKeyDisplayString = "Del" };
                exclude.Click += (s, e) => RaiseEntityAction(EntityAction.Exclude, selection);
                menu.Items.Add(exclude);
            }
            if (selection.Any(IsExcluded))
            {
                var include = new ToolStripMenuItem("Include");
                include.Click += (s, e) => RaiseEntityAction(EntityAction.Include, selection);
                menu.Items.Add(include);
            }

            var curves = selection.Where(en => !IsExcluded(en) && Dimensions.DimensionBuilder.IsRadialTarget(en)).ToList();
            if (curves.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                var radius = new ToolStripMenuItem("Add Radius Dimension");
                radius.Click += (s, e) => RaiseEntityAction(EntityAction.AddRadiusDimension, curves);
                menu.Items.Add(radius);
                var diameter = new ToolStripMenuItem("Add Diameter Dimension");
                diameter.Click += (s, e) => RaiseEntityAction(EntityAction.AddDiameterDimension, curves);
                menu.Items.Add(diameter);
            }

            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, location);
        }

        private void AddRoleItem(ContextMenuStrip menu, string text, EntityAction action, bool isChecked,
            List<Entity> selection)
        {
            var item = new ToolStripMenuItem(text) { Checked = isChecked };
            item.Click += (s, e) => RaiseEntityAction(action, selection);
            menu.Items.Add(item);
        }

        private void RaiseEntityAction(EntityAction action, IReadOnlyList<Entity> entities)
        {
            EntityActionRequested?.Invoke(this, new EntityActionEventArgs(action, entities));
            Invalidate();
        }

        private bool HandleSelectionKey(KeyEventArgs e)
        {
            if (IsPicking)
                return false;
            if (e.KeyCode == Keys.Escape && selectedEntities.Count > 0)
            {
                ClearSelection();
                return true;
            }
            if (e.KeyCode == Keys.Delete && selectedEntities.Count > 0)
            {
                RaiseEntityAction(EntityAction.Exclude, selectedEntities.ToList());
                return true;
            }
            return false;
        }

        private Entity HitTestEntity(Point controlPoint)
        {
            var worldPoint = PointControlToWorld(controlPoint);
            var tolerance = LengthGuiToWorld(6);
            Entity best = null;
            var bestDistance = double.MaxValue;

            foreach (var entity in Entities)
            {
                if (!IsSelectable(entity))
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

        private void DrawSelection(Graphics g)
        {
            if (selectedEntities.Count > 0)
            {
                using var selectedPen = new Pen(Color.FromArgb(255, 200, 60), 2f);
                foreach (var entity in selectedEntities)
                    DrawEntityShape(g, entity, selectedPen);
            }

            if (!isWindowSelecting || windowStart is not { } start)
                return;

            var crossing = windowEnd.X < start.X;
            var x = System.Math.Min(start.X, windowEnd.X) - origin.X;
            var y = System.Math.Min(start.Y, windowEnd.Y) - origin.Y;
            var w = System.Math.Abs(windowEnd.X - start.X);
            var h = System.Math.Abs(windowEnd.Y - start.Y);
            var tint = crossing ? Color.FromArgb(80, 200, 120) : Color.FromArgb(80, 140, 230);
            using var fill = new SolidBrush(Color.FromArgb(40, tint));
            using var border = new Pen(tint) { DashStyle = crossing ? DashStyle.Dash : DashStyle.Solid };
            g.FillRectangle(fill, x, y, w, h);
            g.DrawRectangle(border, x, y, w, h);
        }

        private static bool Crosses(Entity entity, Box rect)
        {
            var points = Polyline(entity);
            for (var i = 0; i < points.Count; i++)
            {
                if (rect.Contains(points[i]))
                    return true;
                if (i > 0 && SegmentCrossesBox(points[i - 1], points[i], rect))
                    return true;
            }
            return false;
        }

        private static List<Vector> Polyline(Entity entity)
        {
            switch (entity)
            {
                case Line line:
                    return new List<Vector> { line.StartPoint, line.EndPoint };
                case Arc arc:
                    {
                        var sweep = arc.SweepAngle();
                        var sign = arc.IsReversed ? -1.0 : 1.0;
                        return Enumerable.Range(0, ArcSegments + 1)
                            .Select(i => PointOnCircle(arc.Center, arc.Radius, arc.StartAngle + sign * sweep * i / ArcSegments))
                            .ToList();
                    }
                case Circle circle:
                    return Enumerable.Range(0, ArcSegments + 1)
                        .Select(i => PointOnCircle(circle.Center, circle.Radius, 2 * System.Math.PI * i / ArcSegments))
                        .ToList();
                default:
                    return new List<Vector>();
            }
        }

        private static Vector PointOnCircle(Vector center, double radius, double angle) =>
            new Vector(center.X + radius * System.Math.Cos(angle), center.Y + radius * System.Math.Sin(angle));

        private static bool SegmentCrossesBox(Vector a, Vector b, Box rect)
        {
            var corners = new[]
            {
                new Vector(rect.Left, rect.Bottom),
                new Vector(rect.Right, rect.Bottom),
                new Vector(rect.Right, rect.Top),
                new Vector(rect.Left, rect.Top),
            };
            for (var i = 0; i < 4; i++)
            {
                if (SegmentsIntersect(a, b, corners[i], corners[(i + 1) % 4]))
                    return true;
            }
            return false;
        }

        private static bool SegmentsIntersect(Vector p1, Vector p2, Vector q1, Vector q2)
        {
            static double Cross(Vector o, Vector a, Vector b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

            var d1 = Cross(q1, q2, p1);
            var d2 = Cross(q1, q2, p2);
            var d3 = Cross(p1, p2, q1);
            var d4 = Cross(p1, p2, q2);
            // The extents test rejects collinear segments that do not overlap.
            return d1 * d2 <= 0 && d3 * d4 <= 0
                && System.Math.Max(p1.X, p2.X) >= System.Math.Min(q1.X, q2.X)
                && System.Math.Max(q1.X, q2.X) >= System.Math.Min(p1.X, p2.X)
                && System.Math.Max(p1.Y, p2.Y) >= System.Math.Min(q1.Y, q2.Y)
                && System.Math.Max(q1.Y, q2.Y) >= System.Math.Min(p1.Y, p2.Y);
        }
    }
}
