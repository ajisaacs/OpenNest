using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Controls
{
    /// <summary>
    /// Shows one drawing zoomed to fit. With annotations set, it also draws each shape
    /// parameter as a dimension on the drawing (or as a note chip along the bottom) and
    /// raises <see cref="LabelClicked"/> when one is clicked, as Edit Clamps does on the plate.
    /// Dragging a dimension's label moves the dimension; the wheel zooms, the middle button
    /// pans, and F or a middle double-click fits the view again. Until the view is zoomed or
    /// panned by hand, it refits whenever the drawing or its dimensions change.
    /// </summary>
    public class ShapePreviewControl : PlateView
    {
        // Screen sizes, in pixels.
        private const float Gap = 18f;
        private const float ExtensionOverrun = 5f;
        private const float ArrowLength = 8f;
        private const float ArrowHalfWidth = 3f;
        private const float LeaderClearance = 20f;
        private const float Shoulder = 12f;
        private const float LabelPadding = 3f;
        private const float EdgePadding = 10f;
        private const float ChipSpacing = 6f;
        private const int DragThreshold = 3;
        // How close a dragged linear dimension may come to the drawing's bounds.
        private const float MinimumGap = 6f;

        private static readonly Color DimensionColor = Color.FromArgb(235, 230, 120, 20);
        private static readonly Color HoverColor = Color.FromArgb(255, 140, 0);
        private static readonly Color ActiveColor = Color.FromArgb(220, 30, 110, 200);

        private string[] infoLines;
        private Vector shapeOffset;
        private IReadOnlyList<Annotation> annotations = Array.Empty<Annotation>();
        private IReadOnlyList<Note> notes = Array.Empty<Note>();
        private AnnotationLayout layout;
        private string hoverKey;
        private string activeKey;
        private bool userView;

        // Each dimension's drag offset in pixels, keyed by its property; kept until the shape changes.
        private readonly Dictionary<string, PointF> dragOffsets = new();
        private string pressedKey;
        private string clickKey;
        private string dragKey;
        private Point pressPoint;
        private PointF pressOffset;

        public ShapePreviewControl()
        {
            DrawOrigin = false;
            DrawBounds = false;
            AllowPan = true;
            AllowSelect = false;
            AllowZoom = true;
            AllowDrop = false;
            BackColor = ColorScheme.Default.LibraryBackgroundColor;
        }

        /// <summary>Raised with a label's key when an enabled dimension or note is clicked.</summary>
        public event EventHandler<string> LabelClicked;

        /// <summary>Raised when labels move on screen: zoom, pan, fit or a dimension drag.</summary>
        public event EventHandler ViewChanged;

        /// <summary>True once the view was zoomed or panned by hand; fitting clears it.</summary>
        internal bool IsUserView => userView;

        /// <summary>A dimension to draw: its text, and the count text after it when it has one.</summary>
        public sealed record Annotation(ShapeDimension Dimension, string Text, string CountText = null);

        /// <summary>A parameter with no place on the drawing, shown as a chip along the bottom.</summary>
        public sealed record Note(string Key, string Text, bool Enabled = true);

        /// <summary>The key of the label being edited, drawn highlighted; null for none.</summary>
        public string ActiveKey
        {
            get => activeKey;
            set
            {
                activeKey = value;
                Invalidate();
            }
        }

        public void SetInfo(params string[] lines)
        {
            infoLines = lines;
            Invalidate();
        }

        /// <summary>Sets the dimensions and notes; dimension points are in the shown drawing's own coordinates.</summary>
        public void SetAnnotations(IReadOnlyList<Annotation> dimensions, IReadOnlyList<Note> chips)
        {
            annotations = dimensions ?? Array.Empty<Annotation>();
            notes = chips ?? Array.Empty<Note>();
            hoverKey = null;
            layout = null;
            if (Plate.Parts.Count > 0)
                FitUnlessUserView(false);
            Invalidate();
        }

        /// <summary>The keys of the labels on screen, in drawing order (dimensions, then notes).</summary>
        internal IReadOnlyList<string> LabelKeys => CurrentLayout().Labels.Select(l => l.Key).ToList();

        /// <summary>Where the label for <paramref name="key"/> is on the control, or null when none is shown.</summary>
        public Rectangle? LabelBounds(string key)
        {
            var label = CurrentLayout().Labels.FirstOrDefault(l => l.Key == key);
            return label == null ? null : Rectangle.Round(label.Bounds);
        }

        /// <summary>
        /// Shows <paramref name="drawing"/>. A new shape (<paramref name="newShape"/>) fits the
        /// view and puts dragged dimensions back; a changed parameter keeps a hand-set view.
        /// </summary>
        public void ShowDrawing(Drawing drawing, bool newShape = true)
        {
            if (newShape)
            {
                userView = false;
                dragOffsets.Clear();
            }
            pressedKey = clickKey = dragKey = null;
            Plate.Parts.Clear();
            Plate.Size = new Geometry.Size(0, 0);
            layout = null;

            if (drawing?.Program != null)
            {
                AddPartFromDrawing(drawing, Vector.Zero);
                // The part is centered on the origin: the shift from drawing to view coordinates.
                var shown = Plate.Parts[0].BoundingBox;
                var source = drawing.Program.BoundingBox();
                shapeOffset = new Vector(shown.X - source.X, shown.Y - source.Y);
                FitUnlessUserView();
            }
            else
            {
                annotations = Array.Empty<Annotation>();
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Plate.Parts.Count > 0)
                FitUnlessUserView(false);
        }

        private void FitUnlessUserView(bool redraw = true)
        {
            if (!userView)
            {
                Fit(redraw);
                return;
            }
            layout = null;
            if (redraw)
                Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Fits the drawing and its dimensions, and stops keeping a hand-set view.</summary>
        public override void ZoomToFit(bool redraw = true)
        {
            userView = false;
            Fit(redraw);
        }

        /// <summary>
        /// Fits the drawing with room around it for its dimensions: lays them out, measures how
        /// far they reach past the drawing and fits again until that stops changing.
        /// </summary>
        private void Fit(bool redraw)
        {
            if (Plate.Parts.Count == 0 || Width <= 0 || Height <= 0)
            {
                base.ZoomToFit(redraw);
                return;
            }

            var box = Plate.Parts[0].BoundingBox;
            if (box.Length <= 0 || box.Width <= 0)
            {
                base.ZoomToFit(redraw);
                return;
            }

            var top = EdgePadding + InfoHeight();
            var bottom = EdgePadding + ChipRowsHeight();
            float left = EdgePadding, right = EdgePadding;
            float reachLeft = 0, reachRight = 0, reachTop = 0, reachBottom = 0;

            for (var pass = 0; pass < 3; pass++)
            {
                var ml = left + reachLeft;
                var mr = right + reachRight;
                var mt = top + reachTop;
                var mb = bottom + reachBottom;
                var w = System.Math.Max(Width - ml - mr, 20f);
                var h = System.Math.Max(Height - mt - mb, 20f);
                var scale = (float)System.Math.Min(w / box.Length, h / box.Width);
                if (!(scale > 0) || float.IsInfinity(scale))
                    break;

                ViewScale = scale;
                origin.X = ml + (w - (float)box.Length * scale) / 2 - (float)box.X * scale;
                origin.Y = mt + (h - (float)box.Width * scale) / 2 + (float)(box.Y + box.Width) * scale;
                UpdateMatrix();

                layout = BuildLayout();
                var shape = ShapeRect();
                var reach = layout.DimensionBounds;
                if (reach.IsEmpty)
                    break;

                reachLeft = System.Math.Max(0, shape.Left - reach.Left);
                reachRight = System.Math.Max(0, reach.Right - shape.Right);
                reachTop = System.Math.Max(0, shape.Top - reach.Top);
                reachBottom = System.Math.Max(0, reach.Bottom - shape.Bottom);
            }

            layout = null;
            if (redraw)
                Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Zoom only: the plate view's Shift+wheel would turn selected parts.
            if (Plate.Parts.Count == 0)
                return;
            var steps = System.Math.Max(1, System.Math.Abs(e.Delta / 120));
            var factor = (float)System.Math.Pow(e.Delta > 0 ? ZoomInFactor : ZoomOutFactor, steps);
            ZoomToControlPoint(e.Location, factor, false);
            userView = true;
            layout = null;
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                var moved = e.Location != lastPoint;
                base.OnMouseMove(e); // pans
                if (moved && Plate.Parts.Count > 0)
                {
                    userView = true;
                    layout = null;
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            if (pressedKey != null && e.Button == MouseButtons.Left)
            {
                if (dragKey == null
                    && System.Math.Abs(e.X - pressPoint.X) <= DragThreshold
                    && System.Math.Abs(e.Y - pressPoint.Y) <= DragThreshold)
                    return;
                dragKey = pressedKey;
                dragOffsets[dragKey] = new PointF(pressOffset.X + e.X - pressPoint.X, pressOffset.Y + e.Y - pressPoint.Y);
                Cursor = Cursors.SizeAll;
                layout = null;
                Invalidate();
                ViewChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            Cursor = hit == null ? Cursors.Default : hit.DragKey != null ? Cursors.SizeAll : Cursors.Hand;
            if (hit?.Key != hoverKey)
            {
                hoverKey = hit?.Key;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverKey != null)
            {
                hoverKey = null;
                Invalidate();
            }
        }

        /// <summary>
        /// A press on a dimension's label waits for the release: a click opens it for editing,
        /// a drag moves the dimension instead. A note opens at once; it has no drag.
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            var hit = e.Button == MouseButtons.Left ? HitTest(e.Location) : null;
            if (hit == null)
            {
                base.OnMouseDown(e);
                return;
            }

            if (!Focused)
                Focus();
            if (hit.DragKey == null)
            {
                LabelClicked?.Invoke(this, hit.Key);
                return;
            }
            pressedKey = hit.DragKey;
            clickKey = hit.Key;
            dragKey = null;
            pressPoint = e.Location;
            pressOffset = dragOffsets.TryGetValue(pressedKey, out var offset) ? offset : PointF.Empty;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || pressedKey == null)
            {
                base.OnMouseUp(e);
                return;
            }

            var clicked = dragKey == null ? clickKey : null;
            pressedKey = clickKey = dragKey = null;
            layout = null;
            Invalidate();
            if (clicked != null)
                LabelClicked?.Invoke(this, clicked);
        }

        /// <summary>Moves a dimension <paramref name="offset"/> pixels from its own place, as dragging its label does.</summary>
        internal void SetDragOffset(string property, PointF offset)
        {
            dragOffsets[property] = offset;
            layout = null;
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private Label HitTest(Point location) =>
            CurrentLayout().Labels.FirstOrDefault(l => l.Enabled && l.Bounds.Contains(location));

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            e.Graphics.TranslateTransform(origin.X, origin.Y);
            Renderer.DrawPlate(e.Graphics);
            Renderer.DrawLibraryParts(e.Graphics, ColorScheme.Default);
            e.Graphics.ResetTransform();

            layout = BuildLayout();
            PaintAnnotations(e.Graphics, layout);
            PaintInfo(e.Graphics);
        }

        private void PaintInfo(Graphics g)
        {
            if (infoLines == null)
                return;

            var lineHeight = Font.GetHeight(g) + 1;
            var y = 4f;
            using var text = new SolidBrush(ColorScheme.Default.LibraryOutlineColor);

            foreach (var line in infoLines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;
                g.DrawString(line, Font, text, 4, y);
                y += lineHeight;
            }
        }

        private float InfoHeight()
        {
            var count = infoLines?.Count(l => !string.IsNullOrEmpty(l)) ?? 0;
            return count == 0 ? 0 : 4 + count * (Font.Height + 1);
        }

        private float ChipRowsHeight()
        {
            if (notes.Count == 0)
                return 0;
            var rows = LayoutChips().Select(c => c.Bounds.Top).Distinct().Count();
            return rows * (LabelHeight + ChipSpacing);
        }

        #region Layout

        private sealed class Label
        {
            public string Key;
            public string Text;
            public RectangleF Bounds;
            public bool Enabled = true;
            public bool IsNote;

            /// <summary>The property of the dimension a drag on this label moves; null for notes.</summary>
            public string DragKey;
        }

        private sealed class AnnotationLayout
        {
            public readonly List<(PointF A, PointF B)> Lines = new();
            public readonly List<(PointF Tip, PointF From)> Arrows = new();

            /// <summary>Dotted reference circles, such as a flange's bolt circle.</summary>
            public readonly List<(PointF Center, float Radius)> Circles = new();
            public readonly List<Label> Labels = new();

            /// <summary>Everything the dimensions draw (not the notes), in control pixels.</summary>
            public RectangleF DimensionBounds
            {
                get
                {
                    var rects = Lines.Select(l => RectangleF.FromLTRB(
                            System.Math.Min(l.A.X, l.B.X), System.Math.Min(l.A.Y, l.B.Y),
                            System.Math.Max(l.A.X, l.B.X), System.Math.Max(l.A.Y, l.B.Y)))
                        .Concat(Labels.Where(l => !l.IsNote).Select(l => l.Bounds))
                        .ToList();
                    return rects.Count == 0 ? RectangleF.Empty : rects.Aggregate(RectangleF.Union);
                }
            }
        }

        private AnnotationLayout CurrentLayout() => layout ??= BuildLayout();

        private float LabelHeight => Font.Height + LabelPadding;

        private SizeF LabelSize(string text) =>
            new(TextRenderer.MeasureText(text, Font, System.Drawing.Size.Empty, TextFormatFlags.NoPadding).Width + 2 * LabelPadding,
                LabelHeight);

        private PointF ToScreen(Vector shapePoint)
        {
            var graph = PointWorldToGraph(shapePoint.X + shapeOffset.X, shapePoint.Y + shapeOffset.Y);
            return new PointF(graph.X + origin.X, graph.Y + origin.Y);
        }

        private RectangleF ShapeRect()
        {
            var box = Plate.Parts[0].BoundingBox;
            var graphTopLeft = PointWorldToGraph(box.X, box.Y + box.Width);
            return new RectangleF(graphTopLeft.X + origin.X, graphTopLeft.Y + origin.Y,
                LengthWorldToGui(box.Length), LengthWorldToGui(box.Width));
        }

        private AnnotationLayout BuildLayout()
        {
            var result = new AnnotationLayout();
            if (Plate.Parts.Count == 0)
                return result;

            var shape = ShapeRect();
            foreach (var annotation in annotations)
            {
                var d = annotation.Dimension;
                switch (d.Kind)
                {
                    case ShapeDimensionKind.Linear:
                        LayoutLinear(result, annotation, shape);
                        break;
                    case ShapeDimensionKind.Inline:
                        LayoutInline(result, annotation);
                        break;
                    case ShapeDimensionKind.Leader:
                        LayoutLeader(result, annotation, shape);
                        break;
                }
            }

            result.Labels.AddRange(LayoutChips());
            return result;
        }

        private static float Dot(PointF p, PointF d) => p.X * d.X + p.Y * d.Y;

        private static PointF Along(PointF p, PointF d, float t) => new(p.X + d.X * t, p.Y + d.Y * t);

        private void LayoutLinear(AnnotationLayout result, Annotation annotation, RectangleF shape)
        {
            var d = annotation.Dimension;
            var side = new PointF((float)d.Side.X, (float)-d.Side.Y);
            var a = ToScreen(d.Start);
            var b = ToScreen(d.End);
            var extreme = new[]
            {
                new PointF(shape.Left, shape.Top), new PointF(shape.Right, shape.Top),
                new PointF(shape.Left, shape.Bottom), new PointF(shape.Right, shape.Bottom),
            }.Max(c => Dot(c, side));

            // A drag moves the line out (or back, never onto the drawing) and the label along it.
            var drag = DragOffset(d.Property);
            var gap = System.Math.Max(Gap + Dot(drag, side), MinimumGap);
            var along = new PointF(-side.Y, side.X);

            var lineA = Along(a, side, extreme - Dot(a, side) + gap);
            var lineB = Along(b, side, extreme - Dot(b, side) + gap);
            if (d.DrawsCircle)
                result.Circles.Add((new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2),
                    (float)System.Math.Sqrt(Dot(new PointF(b.X - a.X, b.Y - a.Y), new PointF(b.X - a.X, b.Y - a.Y))) / 2));
            result.Lines.Add((Along(a, side, 3), Along(lineA, side, ExtensionOverrun)));
            result.Lines.Add((Along(b, side, 3), Along(lineB, side, ExtensionOverrun)));
            AddDimensionLine(result, lineA, lineB);
            AddLabelAlong(result, annotation, lineA, lineB, along, Dot(drag, along));
        }

        private PointF DragOffset(string property) =>
            dragOffsets.TryGetValue(property, out var offset) ? offset : PointF.Empty;

        /// <summary>
        /// The labels centered on the line from <paramref name="a"/> to <paramref name="b"/>,
        /// moved <paramref name="shift"/> pixels along <paramref name="along"/>. Moved past an
        /// end, the line runs on to them.
        /// </summary>
        private void AddLabelAlong(AnnotationLayout result, Annotation annotation, PointF a, PointF b, PointF along, float shift)
        {
            var center = Along(new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2), along, shift);
            var (low, high) = Dot(a, along) <= Dot(b, along) ? (a, b) : (b, a);
            if (Dot(center, along) < Dot(low, along))
                result.Lines.Add((low, center));
            else if (Dot(center, along) > Dot(high, along))
                result.Lines.Add((high, center));
            AddLabels(result, annotation, center, 0.5f);
        }

        private void LayoutInline(AnnotationLayout result, Annotation annotation)
        {
            var a = ToScreen(annotation.Dimension.Start);
            var b = ToScreen(annotation.Dimension.End);
            AddDimensionLine(result, a, b);
            // The line stays on its points; a drag slides only the label along it.
            var length = (float)System.Math.Sqrt(Dot(new PointF(b.X - a.X, b.Y - a.Y), new PointF(b.X - a.X, b.Y - a.Y)));
            var along = length > 0 ? new PointF((b.X - a.X) / length, (b.Y - a.Y) / length) : new PointF(1, 0);
            AddLabelAlong(result, annotation, a, b, along, Dot(DragOffset(annotation.Dimension.Property), along));
        }

        private void LayoutLeader(AnnotationLayout result, Annotation annotation, RectangleF shape)
        {
            var d = annotation.Dimension;
            var p = ToScreen(d.Start);
            var dir = new PointF((float)d.Side.X, (float)-d.Side.Y);

            // Run out of the drawing's bounds, then a short level shoulder to the label.
            var exitX = dir.X > 0 ? (shape.Right - p.X) / dir.X : dir.X < 0 ? (shape.Left - p.X) / dir.X : float.MaxValue;
            var exitY = dir.Y > 0 ? (shape.Bottom - p.Y) / dir.Y : dir.Y < 0 ? (shape.Top - p.Y) / dir.Y : float.MaxValue;
            var length = System.Math.Max(System.Math.Min(exitX, exitY), 0) + LeaderClearance;
            // A drag moves the label end anywhere; the leader swings to follow.
            var drag = DragOffset(d.Property);
            var end = Along(p, dir, length);
            end = new PointF(end.X + drag.X, end.Y + drag.Y);
            var right = end.X >= p.X;
            var shoulder = new PointF(end.X + (right ? Shoulder : -Shoulder), end.Y);

            if (!AddHoleDiameter(result, d, end))
            {
                result.Lines.Add((p, end));
                result.Arrows.Add((p, end));
            }
            result.Lines.Add((end, shoulder));
            AddLabels(result, annotation, new PointF(shoulder.X + (right ? 2 : -2), shoulder.Y), right ? 0 : 1);
        }

        /// <summary>
        /// For a hole's diameter leader, the line from <paramref name="end"/> straight through the
        /// hole's center to its far edge, with arrows pointing out onto both edges; false (nothing added) for
        /// other leaders, or when the label end sits inside the hole.
        /// </summary>
        private bool AddHoleDiameter(AnnotationLayout result, ShapeDimension d, PointF end)
        {
            if (d.HoleCenter is not { } holeCenter || d.HoleRadius <= 0)
                return false;

            var center = ToScreen(holeCenter);
            var radius = LengthWorldToGui(d.HoleRadius);
            var toEnd = new PointF(end.X - center.X, end.Y - center.Y);
            var distance = (float)System.Math.Sqrt(Dot(toEnd, toEnd));
            if (distance <= radius)
                return false;

            var u = new PointF(toEnd.X / distance, toEnd.Y / distance);
            var near = Along(center, u, radius);
            var far = Along(center, u, -radius);
            result.Lines.Add((far, end));
            // Both arrows point out from the center onto the hole's edges.
            result.Arrows.Add((near, center));
            result.Arrows.Add((far, center));
            return true;
        }

        private static void AddDimensionLine(AnnotationLayout result, PointF a, PointF b)
        {
            result.Lines.Add((a, b));
            result.Arrows.Add((a, b));
            result.Arrows.Add((b, a));
        }

        /// <summary>
        /// The value label, and the count label joined after it, at <paramref name="anchor"/>
        /// (vertically centered; <paramref name="alignX"/> 0 starts there, 0.5 centers, 1 ends there).
        /// </summary>
        private void AddLabels(AnnotationLayout result, Annotation annotation, PointF anchor, float alignX)
        {
            var d = annotation.Dimension;
            var main = LabelSize(annotation.Text);
            var hasCount = d.CountProperty != null && annotation.CountText != null;
            var count = hasCount ? LabelSize(annotation.CountText) : SizeF.Empty;
            var total = main.Width + count.Width;
            var x = anchor.X - total * alignX;
            var y = anchor.Y - main.Height / 2;

            result.Labels.Add(new Label
            {
                Key = d.Property,
                DragKey = d.Property,
                Text = annotation.Text,
                Bounds = new RectangleF(x, y, main.Width, main.Height),
            });
            if (hasCount)
                result.Labels.Add(new Label
                {
                    Key = d.CountProperty,
                    DragKey = d.Property,
                    Text = annotation.CountText,
                    Bounds = new RectangleF(x + main.Width, y, count.Width, count.Height),
                });
        }

        private IEnumerable<Label> LayoutChips()
        {
            var height = LabelHeight;
            var rows = new List<List<Label>>();
            var x = EdgePadding;
            foreach (var note in notes)
            {
                var size = LabelSize(note.Text);
                if (rows.Count == 0 || (x + size.Width > Width - EdgePadding && rows[^1].Count > 0))
                {
                    rows.Add(new List<Label>());
                    x = EdgePadding;
                }
                rows[^1].Add(new Label
                {
                    Key = note.Key,
                    Text = note.Text,
                    Enabled = note.Enabled,
                    IsNote = true,
                    Bounds = new RectangleF(x, 0, size.Width, height),
                });
                x += size.Width + ChipSpacing;
            }

            // Rows stack up from the bottom edge, the first row on top.
            for (var i = 0; i < rows.Count; i++)
            {
                var y = Height - EdgePadding - (rows.Count - i) * (height + ChipSpacing) + ChipSpacing;
                foreach (var label in rows[i])
                {
                    label.Bounds.Y = y;
                    yield return label;
                }
            }
        }

        #endregion

        #region Painting

        private void PaintAnnotations(Graphics g, AnnotationLayout drawn)
        {
            using var line = new Pen(DimensionColor, 1f);
            foreach (var (a, b) in drawn.Lines)
                g.DrawLine(line, a, b);

            using var dotted = new Pen(DimensionColor, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            foreach (var (center, radius) in drawn.Circles)
                g.DrawEllipse(dotted, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);

            using var arrowBrush = new SolidBrush(DimensionColor);
            foreach (var (tip, from) in drawn.Arrows)
                FillArrow(g, arrowBrush, tip, from);

            foreach (var label in drawn.Labels)
                PaintLabel(g, label);
        }

        private static void FillArrow(Graphics g, Brush brush, PointF tip, PointF from)
        {
            var dx = from.X - tip.X;
            var dy = from.Y - tip.Y;
            var length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (length < 1)
                return;
            dx /= length;
            dy /= length;
            var back = new PointF(tip.X + dx * ArrowLength, tip.Y + dy * ArrowLength);
            g.FillPolygon(brush, new[]
            {
                tip,
                new PointF(back.X - dy * ArrowHalfWidth, back.Y + dx * ArrowHalfWidth),
                new PointF(back.X + dy * ArrowHalfWidth, back.Y - dx * ArrowHalfWidth),
            });
        }

        private void PaintLabel(Graphics g, Label label)
        {
            var active = label.Key == activeKey;
            var hot = label.Enabled && label.Key == hoverKey;
            var back = active ? Color.FromArgb(215, 232, 252)
                : hot ? Color.FromArgb(255, 236, 210)
                : label.IsNote ? Color.FromArgb(246, 246, 246)
                : Color.White;
            var rect = label.Bounds;
            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, rect);
            using (var border = new Pen(active ? ActiveColor : hot ? HoverColor : DimensionColor, active || hot ? 1.5f : 1f))
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            TextRenderer.DrawText(g, label.Text, Font, Rectangle.Round(rect),
                label.Enabled ? Color.Black : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        #endregion
    }
}
