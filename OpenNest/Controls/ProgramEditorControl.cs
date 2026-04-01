using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
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
    }
}
