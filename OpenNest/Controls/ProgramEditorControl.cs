using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
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
            var text = Program != null ? FormatProgram(Program, contours) : string.Empty;
            gcodeEditor.Text = text;
            ApplyHighlighting();
        }

        private static string FormatProgram(Program pgm, List<ContourInfo> contours)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(pgm.Mode == Mode.Absolute ? "G90" : "G91");

            var codeIndex = 0;
            var codes = pgm.Codes;

            foreach (var contour in contours)
            {
                var sub = ConvertGeometry.ToProgram(contour.Shape);
                if (sub == null) continue;

                sb.AppendLine();
                sb.AppendLine($"; {contour.Label} ({contour.DirectionLabel})");

                var lastWasRapid = false;
                for (var i = 0; i < sub.Length && codeIndex < codes.Count; i++, codeIndex++)
                {
                    var code = codes[codeIndex];
                    if (code is RapidMove rapid)
                    {
                        if (!lastWasRapid)
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
            }

            return sb.ToString();
        }

        private static string FormatCoord(double value)
        {
            return System.Math.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void ApplyHighlighting()
        {
            var text = gcodeEditor.Text;
            if (string.IsNullOrEmpty(text)) return;

            gcodeEditor.SuspendLayout();

            var rapidColor = Color.FromArgb(230, 180, 80);
            var linearColor = Color.FromArgb(130, 200, 140);
            var arcColor = Color.FromArgb(120, 160, 255);
            var commentColor = Color.FromArgb(120, 120, 140);
            var modeColor = Color.FromArgb(200, 140, 220);
            var coordColor = Color.FromArgb(180, 200, 180);

            gcodeEditor.SelectAll();
            gcodeEditor.SelectionColor = coordColor;

            var rules = new (Regex pattern, Color color)[]
            {
                (new Regex(@"^;.*$", RegexOptions.Multiline), commentColor),
                (new Regex(@"^G9[01]\b", RegexOptions.Multiline), modeColor),
                (new Regex(@"^G00\b", RegexOptions.Multiline), rapidColor),
                (new Regex(@"^G01\b", RegexOptions.Multiline), linearColor),
                (new Regex(@"^G0[23]\b", RegexOptions.Multiline), arcColor),
            };

            foreach (var (pattern, color) in rules)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    gcodeEditor.Select(match.Index, match.Length);
                    gcodeEditor.SelectionColor = color;
                }
            }

            gcodeEditor.Select(0, 0);
            gcodeEditor.ResumeLayout();
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
                    var clone = CloneEntity(entity, color);
                    if (clone != null)
                        preview.Entities.Add(clone);
                }
            }

            preview.ZoomToFit();
            preview.Invalidate();
        }

        private static Entity CloneEntity(Entity entity, Color color)
        {
            var clone = entity.Clone();
            clone.Color = color;
            return clone;
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

    }
}
