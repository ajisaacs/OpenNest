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

        public ShapePreviewControl()
        {
            DrawOrigin = false;
            DrawBounds = false;
            AllowPan = false;
            AllowSelect = false;
            AllowZoom = false;
            AllowDrop = false;
            BackColor = ColorScheme.Default.LibraryBackgroundColor;
        }

        /// <summary>Raised with a label's key when an enabled dimension or note is clicked.</summary>
        public event EventHandler<string> LabelClicked;

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
            if (Plate.Parts.Count > 0)
                ZoomToFit(false);
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

        public void ShowDrawing(Drawing drawing)
        {
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
                ZoomToFit();
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
                ZoomToFit(false);
        }

        /// <summary>
        /// Fits the drawing with room around it for its dimensions: lays them out, measures how
        /// far they reach past the drawing and fits again until that stops changing.
        /// </summary>
        public override void ZoomToFit(bool redraw = true)
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
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            if (hit != hoverKey)
            {
                hoverKey = hit;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var hit = e.Button == MouseButtons.Left ? HitTest(e.Location) : null;
            if (hit != null)
            {
                LabelClicked?.Invoke(this, hit);
                return;
            }
            base.OnMouseDown(e);
        }

        private string HitTest(Point location) =>
            CurrentLayout().Labels.FirstOrDefault(l => l.Enabled && l.Bounds.Contains(location))?.Key;

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
        }

        private sealed class AnnotationLayout
        {
            public readonly List<(PointF A, PointF B)> Lines = new();
            public readonly List<(PointF Tip, PointF From)> Arrows = new();
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

            var lineA = Along(a, side, extreme - Dot(a, side) + Gap);
            var lineB = Along(b, side, extreme - Dot(b, side) + Gap);
            result.Lines.Add((Along(a, side, 3), Along(lineA, side, ExtensionOverrun)));
            result.Lines.Add((Along(b, side, 3), Along(lineB, side, ExtensionOverrun)));
            AddDimensionLine(result, lineA, lineB);
            AddLabels(result, annotation, new PointF((lineA.X + lineB.X) / 2, (lineA.Y + lineB.Y) / 2), 0.5f);
        }

        private void LayoutInline(AnnotationLayout result, Annotation annotation)
        {
            var a = ToScreen(annotation.Dimension.Start);
            var b = ToScreen(annotation.Dimension.End);
            AddDimensionLine(result, a, b);
            AddLabels(result, annotation, new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2), 0.5f);
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
            var end = Along(p, dir, length);
            var right = dir.X >= 0;
            var shoulder = new PointF(end.X + (right ? Shoulder : -Shoulder), end.Y);

            result.Lines.Add((p, end));
            result.Lines.Add((end, shoulder));
            result.Arrows.Add((p, end));
            AddLabels(result, annotation, new PointF(shoulder.X + (right ? 2 : -2), shoulder.Y), right ? 0 : 1);
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

            result.Labels.Add(new Label { Key = d.Property, Text = annotation.Text, Bounds = new RectangleF(x, y, main.Width, main.Height) });
            if (hasCount)
                result.Labels.Add(new Label
                {
                    Key = d.CountProperty,
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
