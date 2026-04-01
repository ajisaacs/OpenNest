using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public partial class ProgramEditorControl : UserControl
    {
        private List<ContourInfo> contours = new();
        private bool isDirty;
        private bool isLoaded;

        public ProgramEditorControl()
        {
            InitializeComponent();

            contourList.DrawItem += OnDrawContourItem;
            contourList.MeasureItem += OnMeasureContourItem;
            contourList.SelectedIndexChanged += OnContourSelectionChanged;
            reverseButton.Click += OnReverseClicked;
            menuReverse.Click += OnReverseClicked;
            applyButton.Click += OnApplyClicked;
        }

        public Program Program { get; private set; }
        public bool IsDirty => isDirty;
        public bool IsLoaded => isLoaded;

        public event EventHandler ProgramChanged;

        public void LoadEntities(List<Entity> entities)
        {
            var shapes = ShapeBuilder.GetShapes(entities);
            if (shapes.Count == 0)
            {
                Clear();
                return;
            }

            contours = ContourInfo.Classify(shapes);
            Program = BuildProgram(contours);
            isDirty = false;
            isLoaded = true;

            PopulateContourList();
            UpdateGcodeText();
            RefreshPreview();
        }

        public void Clear()
        {
            contours.Clear();
            contourList.Items.Clear();
            preview.Entities.Clear();
            preview.Invalidate();
            gcodeEditor.Clear();
            Program = null;
            isDirty = false;
            isLoaded = false;
        }

        private static Program BuildProgram(List<ContourInfo> contours)
        {
            var pgm = new Program();
            foreach (var contour in contours)
            {
                var sub = ConvertGeometry.ToProgram(contour.Shape);
                pgm.Merge(sub);
            }
            pgm.Mode = Mode.Incremental;
            return pgm;
        }

        private void PopulateContourList()
        {
            contourList.Items.Clear();
            foreach (var contour in contours)
                contourList.Items.Add(contour);
        }

        private void UpdateGcodeText()
        {
            gcodeEditor.Text = Program?.ToString() ?? string.Empty;
        }

        private void RefreshPreview()
        {
            preview.ClearPenCache();
            preview.Entities.Clear();

            for (var i = 0; i < contours.Count; i++)
            {
                var contour = contours[i];
                var selected = contourList.SelectedIndices.Contains(i);
                var color = GetContourColor(contour.Type, selected);

                foreach (var entity in contour.Shape.Entities)
                {
                    entity.Color = color;
                    preview.Entities.Add(entity);
                }

                if (selected)
                    AddDirectionArrows(contour.Shape, color);
            }

            preview.ZoomToFit();
            preview.Invalidate();
        }

        private static Color GetContourColor(ContourClassification type, bool selected)
        {
            if (selected)
                return Color.White;

            return type switch
            {
                ContourClassification.Perimeter => Color.FromArgb(80, 180, 120),
                ContourClassification.Hole => Color.FromArgb(100, 140, 255),
                ContourClassification.Etch => Color.FromArgb(255, 170, 50),
                ContourClassification.Open => Color.FromArgb(200, 200, 100),
                _ => Color.Gray,
            };
        }

        private void OnMeasureContourItem(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = 42;
        }

        private void OnDrawContourItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= contours.Count) return;

            var contour = contours[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            var bounds = e.Bounds;

            // Background
            using var bgBrush = new SolidBrush(selected
                ? Color.FromArgb(230, 238, 255)
                : Color.White);
            e.Graphics.FillRectangle(bgBrush, bounds);

            // Accent bar
            var accentColor = GetContourColor(contour.Type, false);
            using var accentBrush = new SolidBrush(accentColor);
            e.Graphics.FillRectangle(accentBrush, bounds.X, bounds.Y + 4, 3, bounds.Height - 8);

            // Direction icon
            var icon = contour.Type switch
            {
                ContourClassification.Perimeter or ContourClassification.Hole =>
                    contour.DirectionLabel == "CW" ? "\u21BB" : "\u21BA",
                ContourClassification.Etch => "\u2014",
                _ => "\u2014",
            };
            using var iconFont = new Font("Segoe UI", 14f);
            using var iconBrush = new SolidBrush(accentColor);
            e.Graphics.DrawString(icon, iconFont, iconBrush, bounds.X + 8, bounds.Y + 6);

            // Label
            using var labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var labelBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
            e.Graphics.DrawString(contour.Label, labelFont, labelBrush, bounds.X + 32, bounds.Y + 4);

            // Info line
            var info = $"{contour.DirectionLabel} \u00B7 {contour.DimensionLabel}";
            using var infoFont = new Font("Segoe UI", 8f);
            using var infoBrush = new SolidBrush(Color.Gray);
            e.Graphics.DrawString(info, infoFont, infoBrush, bounds.X + 32, bounds.Y + 22);

            // Separator
            using var sepPen = new Pen(Color.FromArgb(230, 230, 230));
            e.Graphics.DrawLine(sepPen, bounds.X + 8, bounds.Bottom - 1, bounds.Right - 8, bounds.Bottom - 1);
        }

        private void OnContourSelectionChanged(object sender, EventArgs e)
        {
            RefreshPreview();
        }

        private void OnReverseClicked(object sender, EventArgs e)
        {
            if (contourList.SelectedIndices.Count == 0) return;

            foreach (int index in contourList.SelectedIndices)
            {
                if (index >= 0 && index < contours.Count)
                    contours[index].Reverse();
            }

            Program = BuildProgram(contours);
            isDirty = true;

            contourList.Invalidate();
            UpdateGcodeText();
            RefreshPreview();
            ProgramChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddDirectionArrows(Shape shape, Color color)
        {
            var entities = shape.Entities;
            if (entities.Count == 0) return;

            var totalLength = shape.Length;
            if (totalLength < 0.001) return;

            var arrowSize = totalLength * 0.02;
            if (arrowSize < 0.5) arrowSize = 0.5;
            if (arrowSize > 5) arrowSize = 5;

            foreach (var fraction in new[] { 0.25, 0.75 })
            {
                var targetDist = totalLength * fraction;
                var accumulated = 0.0;
                var found = false;

                foreach (var entity in entities)
                {
                    var entityLen = entity.Length;
                    if (accumulated + entityLen >= targetDist)
                    {
                        var localFraction = (targetDist - accumulated) / entityLen;
                        var (point, angle) = GetPointAndAngle(entity, localFraction);
                        AddArrowHead(point, angle, arrowSize, color);
                        found = true;
                        break;
                    }
                    accumulated += entityLen;
                }

                if (!found && entities.Count > 0)
                {
                    var last = entities[^1];
                    var (point, angle) = GetPointAndAngle(last, 0.5);
                    AddArrowHead(point, angle, arrowSize, color);
                }
            }
        }

        private static (Vector point, double angle) GetPointAndAngle(Entity entity, double fraction)
        {
            switch (entity)
            {
                case Line line:
                {
                    var dx = line.EndPoint.X - line.StartPoint.X;
                    var dy = line.EndPoint.Y - line.StartPoint.Y;
                    var pt = new Vector(
                        line.StartPoint.X + dx * fraction,
                        line.StartPoint.Y + dy * fraction);
                    var angle = System.Math.Atan2(dy, dx);
                    return (pt, angle);
                }
                case Arc arc:
                {
                    var startAngle = arc.StartAngle;
                    var endAngle = arc.EndAngle;
                    if (arc.IsReversed)
                    {
                        var span = startAngle - endAngle;
                        if (span < 0) span += 2 * System.Math.PI;
                        var a = startAngle - span * fraction;
                        var pt = new Vector(
                            arc.Center.X + arc.Radius * System.Math.Cos(a),
                            arc.Center.Y + arc.Radius * System.Math.Sin(a));
                        var tangent = a - System.Math.PI / 2;
                        return (pt, tangent);
                    }
                    else
                    {
                        var span = endAngle - startAngle;
                        if (span < 0) span += 2 * System.Math.PI;
                        var a = startAngle + span * fraction;
                        var pt = new Vector(
                            arc.Center.X + arc.Radius * System.Math.Cos(a),
                            arc.Center.Y + arc.Radius * System.Math.Sin(a));
                        var tangent = a + System.Math.PI / 2;
                        return (pt, tangent);
                    }
                }
                case Circle circle:
                {
                    var a = 2 * System.Math.PI * fraction;
                    var pt = new Vector(
                        circle.Center.X + circle.Radius * System.Math.Cos(a),
                        circle.Center.Y + circle.Radius * System.Math.Sin(a));
                    var tangent = a + System.Math.PI / 2;
                    return (pt, tangent);
                }
                default:
                    return (new Vector(), 0);
            }
        }

        private void AddArrowHead(Vector tip, double angle, double size, Color color)
        {
            var leftAngle = angle + System.Math.PI + 0.4;
            var rightAngle = angle + System.Math.PI - 0.4;

            var left = new Vector(
                tip.X + size * System.Math.Cos(leftAngle),
                tip.Y + size * System.Math.Sin(leftAngle));
            var right = new Vector(
                tip.X + size * System.Math.Cos(rightAngle),
                tip.Y + size * System.Math.Sin(rightAngle));

            var arrowColor = Color.FromArgb(255, 140, 50);
            preview.Entities.Add(new Line(left, tip) { Color = arrowColor });
            preview.Entities.Add(new Line(right, tip) { Color = arrowColor });
        }

        private void OnApplyClicked(object sender, EventArgs e)
        {
            var text = gcodeEditor.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("G-code is empty.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                var reader = new ProgramReader(stream);
                var parsed = reader.Read();

                if (parsed == null || parsed.Length == 0)
                {
                    MessageBox.Show("No valid G-code found.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Rebuild shapes from the parsed program
                var entities = ConvertProgram.ToGeometry(parsed);
                var shapes = ShapeBuilder.GetShapes(entities);

                if (shapes.Count == 0)
                {
                    MessageBox.Show("No contours found in parsed G-code.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                contours = ContourInfo.Classify(shapes);
                Program = parsed;
                isDirty = true;

                PopulateContourList();
                RefreshPreview();
                ProgramChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing G-code: {ex.Message}", "Apply",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
