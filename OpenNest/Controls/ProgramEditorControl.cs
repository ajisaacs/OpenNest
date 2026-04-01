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
            menuMoveUp.Click += OnMoveUpClicked;
            menuMoveDown.Click += OnMoveDownClicked;
            menuSequence.Click += OnSequenceClicked;
            contourMenu.Opening += OnContourMenuOpening;
            applyButton.Click += OnApplyClicked;
            preview.PaintOverlay = OnPreviewPaintOverlay;
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

            // Assign contour-type colors once so the CAD view also picks them up
            foreach (var contour in contours)
            {
                var color = GetContourColor(contour.Type, false);
                foreach (var entity in contour.Shape.Entities)
                    entity.Color = color;
            }

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
            gcodeEditor.Text = Program != null ? FormatProgram(Program) : string.Empty;
        }

        private static string FormatProgram(Program pgm)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(pgm.Mode == Mode.Absolute ? "G90" : "G91");

            var lastWasRapid = false;
            foreach (var code in pgm.Codes)
            {
                if (code is RapidMove rapid)
                {
                    if (!lastWasRapid && sb.Length > 0)
                        sb.AppendLine();
                    sb.AppendLine($"G00 X{FormatCoord(rapid.EndPoint.X)} Y{FormatCoord(rapid.EndPoint.Y)}");
                    lastWasRapid = true;
                }
                else if (code is ArcMove arc)
                {
                    var g = arc.Rotation == RotationType.CW ? "G02" : "G03";
                    sb.AppendLine($"{g} X{FormatCoord(arc.EndPoint.X)} Y{FormatCoord(arc.EndPoint.Y)} I{FormatCoord(arc.CenterPoint.X)} J{FormatCoord(arc.CenterPoint.Y)}");
                    lastWasRapid = false;
                }
                else if (code is LinearMove linear)
                {
                    sb.AppendLine($"G01 X{FormatCoord(linear.EndPoint.X)} Y{FormatCoord(linear.EndPoint.Y)}");
                    lastWasRapid = false;
                }
            }

            return sb.ToString();
        }

        private static string FormatCoord(double value)
        {
            return System.Math.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void RefreshPreview()
        {
            preview.ClearPenCache();
            preview.Entities.Clear();

            // Restore base colors first (undo any selection highlight)
            foreach (var contour in contours)
            {
                var baseColor = GetContourColor(contour.Type, false);
                foreach (var entity in contour.Shape.Entities)
                    entity.Color = baseColor;
            }

            for (var i = 0; i < contours.Count; i++)
            {
                var contour = contours[i];
                var selected = contourList.SelectedIndices.Contains(i);

                if (selected)
                {
                    var selColor = GetContourColor(contour.Type, true);
                    foreach (var entity in contour.Shape.Entities)
                        entity.Color = selColor;
                }

                preview.Entities.AddRange(contour.Shape.Entities);
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

        private void OnContourMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var index = contourList.SelectedIndex;
            var perimeterIndex = contours.FindIndex(c => c.Type == ContourClassification.Perimeter);
            var isPerimeter = index >= 0 && index == perimeterIndex;

            // Can't move perimeter, can't move past perimeter
            menuMoveUp.Enabled = index > 0 && !isPerimeter;
            menuMoveDown.Enabled = index >= 0 && index < perimeterIndex - 1;
        }

        private void OnMoveUpClicked(object sender, EventArgs e)
        {
            var index = contourList.SelectedIndex;
            if (index <= 0) return;
            if (contours[index].Type == ContourClassification.Perimeter) return;

            (contours[index], contours[index - 1]) = (contours[index - 1], contours[index]);
            RebuildAfterReorder(index - 1);
        }

        private void OnMoveDownClicked(object sender, EventArgs e)
        {
            var index = contourList.SelectedIndex;
            if (index < 0 || index >= contours.Count - 1) return;
            if (contours[index].Type == ContourClassification.Perimeter) return;
            if (contours[index + 1].Type == ContourClassification.Perimeter) return;

            (contours[index], contours[index + 1]) = (contours[index + 1], contours[index]);
            RebuildAfterReorder(index + 1);
        }

        private void OnSequenceClicked(object sender, EventArgs e)
        {
            // Nearest-neighbor sort for non-perimeter contours
            var perimeterIndex = contours.FindIndex(c => c.Type == ContourClassification.Perimeter);
            if (perimeterIndex < 0) return;

            var nonPerimeter = contours.Where(c => c.Type != ContourClassification.Perimeter).ToList();
            if (nonPerimeter.Count <= 1) return;

            var sorted = new List<ContourInfo>();
            var remaining = new List<ContourInfo>(nonPerimeter);
            var pos = new Vector();

            while (remaining.Count > 0)
            {
                var nearest = 0;
                var nearestDist = double.MaxValue;

                for (var i = 0; i < remaining.Count; i++)
                {
                    var startPt = GetContourStartPoint(remaining[i]);
                    var dist = pos.DistanceTo(startPt);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = i;
                    }
                }

                var next = remaining[nearest];
                sorted.Add(next);
                pos = GetContourEndPoint(next);
                remaining.RemoveAt(nearest);
            }

            // Put perimeter back at the end
            sorted.Add(contours[perimeterIndex]);
            contours = sorted;
            RebuildAfterReorder(-1);
        }

        private static Vector GetContourStartPoint(ContourInfo contour)
        {
            var entity = contour.Shape.Entities[0];
            return entity switch
            {
                Line line => line.StartPoint,
                Arc arc => arc.StartPoint(),
                Circle circle => new Vector(circle.Center.X + circle.Radius, circle.Center.Y),
                _ => new Vector(),
            };
        }

        private static Vector GetContourEndPoint(ContourInfo contour)
        {
            var entity = contour.Shape.Entities[^1];
            return entity switch
            {
                Line line => line.EndPoint,
                Arc arc => arc.EndPoint(),
                Circle circle => new Vector(circle.Center.X + circle.Radius, circle.Center.Y),
                _ => new Vector(),
            };
        }

        private void RebuildAfterReorder(int selectIndex)
        {
            RelabelContours();
            Program = BuildProgram(contours);
            isDirty = true;

            PopulateContourList();
            if (selectIndex >= 0 && selectIndex < contourList.Items.Count)
                contourList.SelectedIndex = selectIndex;
            UpdateGcodeText();
            RefreshPreview();
            ProgramChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RelabelContours()
        {
            var holeCount = 0;
            var etchCount = 0;
            var openCount = 0;

            foreach (var contour in contours)
            {
                switch (contour.Type)
                {
                    case ContourClassification.Hole:
                        holeCount++;
                        contour.SetLabel($"Hole {holeCount}");
                        break;
                    case ContourClassification.Etch:
                        etchCount++;
                        contour.SetLabel(etchCount == 1 ? "Etch" : $"Etch {etchCount}");
                        break;
                    case ContourClassification.Open:
                        openCount++;
                        contour.SetLabel(openCount == 1 ? "Open" : $"Open {openCount}");
                        break;
                }
            }
        }

        private void OnPreviewPaintOverlay(Graphics g)
        {
            if (contours.Count == 0) return;

            var spacing = preview.LengthGuiToWorld(60f);
            var arrowSize = 8f;

            using var pen = new Pen(Color.LightGray, 1.5f);

            for (var i = 0; i < contours.Count; i++)
            {
                if (!contourList.SelectedIndices.Contains(i)) continue;

                var contour = contours[i];
                var pgm = ConvertGeometry.ToProgram(contour.Shape);
                if (pgm == null) continue;

                var pos = new Vector();
                CutDirectionArrows.DrawProgram(g, preview, pgm, ref pos, pen, spacing, arrowSize);
            }
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
