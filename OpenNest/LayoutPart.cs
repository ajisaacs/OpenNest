using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest
{
    public class LayoutPart : IPart
    {
        private static Font programIdFont;
        private static Color selectedColor;
        private static Pen selectedPen;
        private static Brush selectedBrush;
        private static Pen leadInPen;

        private Color color;
        private Color? colorOverride;
        private Brush brush;
        private Pen pen;
        private ColorScheme colorScheme = ColorScheme.Default;

        private List<PointF[]> _offsetPolygonPoints;
        private double _cachedOffsetSpacing;
        private double _cachedOffsetTolerance;
        private double _cachedOffsetRotation = double.NaN;

        private Vector? _labelPoint;
        private PointF _labelScreenPoint;

        public readonly Part BasePart;

        static LayoutPart()
        {
            programIdFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold | FontStyle.Underline);
            SelectedColor = Color.FromArgb(90, 150, 200, 255);
            leadInPen = new Pen(Color.OrangeRed, 1.5f);
        }

        private LayoutPart(Part part)
        {
            this.BasePart = part;

            if (part.BaseDrawing.Color.IsEmpty)
                part.BaseDrawing.Color = Color.FromArgb(130, 204, 130);

            SetDisplayColor(part.BaseDrawing.Color);
        }

        internal bool IsDirty { get; set; }

        public bool IsSelected { get; set; }

        public GraphicsPath Path { get; private set; }

        public GraphicsPath LeadInPath { get; private set; }

        public GraphicsPath EtchPath { get; private set; }

        /// <summary>The drawing's display-only geometry; null when it has none. Never material or cut.</summary>
        public GraphicsPath DisplayPath { get; private set; }

        // Etch-only parts have no material path, but their visible closed mark must
        // remain hit-testable without treating scribe geometry as a cut or fill.
        internal GraphicsPath SelectionPath => Path?.PointCount > 0 ? MaterialPath : EtchPath;

        /// <summary>The filled material region: the cut outline unless break cuts or tabs make it differ.</summary>
        internal GraphicsPath MaterialPath => FillPath ?? Path;

        internal RectangleF DisplayBounds { get; private set; }

        public Color Color
        {
            get { return color; }
            set
            {
                colorOverride = value;
                SetDisplayColor(value);
            }
        }

        private void SetDisplayColor(Color value)
        {
            color = value;
            brush?.Dispose();
            brush = new SolidBrush(value);
            pen?.Dispose();
            pen = new Pen(colorScheme.GetPartOutlineColor(value));
        }

        private void RefreshColors()
        {
            var fill = colorOverride ?? BasePart.BaseDrawing.Color;
            if (color != fill || pen.Color != colorScheme.GetPartOutlineColor(fill))
                SetDisplayColor(fill);
        }

        public void Draw(Graphics g) => Draw(g, false);

        private void Draw(Graphics g, bool muted)
        {
            RefreshColors();
            using var schemeSelectionBrush = IsSelected && !colorScheme.SelectedPartColor.IsEmpty
                ? new SolidBrush(colorScheme.SelectedPartColor) : null;
            g.FillPath(muted ? Brushes.Gainsboro : schemeSelectionBrush ?? (IsSelected ? selectedBrush : brush),
                MaterialPath);
            DrawDisplay(g, muted);
            DrawEtch(g, muted);
            // Keep real cuts visible even where an etch overlaps them.
            g.DrawPath(muted ? Pens.Gray : IsSelected && colorScheme.PartOutlineColor.IsEmpty ? selectedPen : pen, Path);

            if (LeadInPath != null)
                g.DrawPath(muted ? Pens.Gray : leadInPen, LeadInPath);
        }

        public void Draw(Graphics g, string id) => Draw(g, id, false);

        internal void Draw(Graphics g, string id, bool muted)
        {
            Draw(g, muted);

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(
                id,
                programIdFont,
                muted ? Brushes.DimGray : Brushes.Black,
                _labelScreenPoint.X,
                _labelScreenPoint.Y,
                sf
            );
        }

        internal void ReleaseDisplayResources()
        {
            brush?.Dispose();
            brush = null;
            pen?.Dispose();
            pen = null;
            Path?.Dispose();
            Path = null;
            FillPath?.Dispose();
            FillPath = null;
            LeadInPath?.Dispose();
            LeadInPath = null;
            EtchPath?.Dispose();
            EtchPath = null;
            DisplayPath?.Dispose();
            DisplayPath = null;
            OffsetPath?.Dispose();
            OffsetPath = null;
        }

        public GraphicsPath OffsetPath { get; private set; }

        /// <summary>
        /// The material fill when it differs from <c>Path</c>: break cuts are drawn, not filled, and a
        /// cutout cut in pieces around tabs fills as one hole rather than as chord-closed pieces.
        /// </summary>
        private GraphicsPath FillPath { get; set; }

        private static bool HasBreakCuts(CNC.Program program) =>
            program.Codes.Any(code => code is CNC.LinearMove { Layer: CNC.LayerType.Break }
                    or CNC.ArcMove { Layer: CNC.LayerType.Break }
                || code is CNC.SubProgramCall { Program: { } sub } && HasBreakCuts(sub));

        internal void DrawEtch(Graphics g, bool muted = false)
        {
            if (EtchPath == null || EtchPath.PointCount == 0)
                return;
            using var etchPen = new Pen(muted ? Color.Gray : colorScheme.EtchColor, 1.5f);
            g.DrawPath(etchPen, EtchPath);
        }

        private void DrawDisplay(Graphics g, bool muted)
        {
            if (DisplayPath == null)
                return;
            using var displayPen = new Pen(muted ? Color.Gray : Color.FromArgb(70, 110, 140))
            {
                DashPattern = new float[] { 6, 3 },
            };
            g.DrawPath(displayPen, DisplayPath);
        }

        // Display-only entities are kept in the drawing's program frame before rotation, so they
        // follow the part by its cumulative rotation about the program origin, then its location.
        private GraphicsPath BuildDisplayPath(Matrix matrix)
        {
            var entities = EntityRoles.DisplayGeometry(BasePart.BaseDrawing);
            if (entities.Count == 0)
                return null;

            var path = new GraphicsPath();
            foreach (var entity in entities)
            {
                entity.Rotate(BasePart.Rotation);
                entity.Offset(BasePart.Location);
                using var figure = new Shape { Entities = new List<Entity> { entity } }.GetGraphicsPath();
                path.AddPath(figure, false);
            }
            path.Transform(matrix);
            return path;
        }

        private Vector ComputeLabelPoint()
        {
            var entities = ConvertProgram.ToGeometry(BasePart.BaseDrawing.Program);
            var nonRapid = entities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();

            var shapes = ShapeBuilder.GetShapes(nonRapid);

            if (shapes.Count == 0)
            {
                var bbox = BasePart.BaseDrawing.Program.BoundingBox();
                return new Vector(
                    bbox.Location.X + bbox.Length / 2,
                    bbox.Location.Y + bbox.Width / 2
                );
            }

            var profile = new ShapeProfile(nonRapid);
            var outer = profile.Perimeter.ToPolygonWithTolerance(0.1);

            List<Polygon> holes = null;

            if (profile.Cutouts.Count > 0)
            {
                holes = new List<Polygon>();
                foreach (var cutout in profile.Cutouts)
                    holes.Add(cutout.ToPolygonWithTolerance(0.1));
            }

            return PolyLabel.Find(outer, holes, PolyLabel.ThicknessPrecision(outer, holes));
        }

        public void Update(DrawControl plateView)
        {
            colorScheme = (plateView as PlateView)?.ColorScheme ?? ColorScheme.Default;
            RefreshColors();
            BasePart.Program.GetDisplayPaths(BasePart.Location,
                out var cutPath, out var leadPath, out var etchPath, out var hasOpenCuts);
            cutPath.Transform(plateView.Matrix);
            leadPath.Transform(plateView.Matrix);
            etchPath.Transform(plateView.Matrix);
            Path?.Dispose();
            LeadInPath?.Dispose();
            EtchPath?.Dispose();
            FillPath?.Dispose();
            FillPath = null;
            // Only programs with break cuts or open (tabbed) cuts need a fill apart from the outline path.
            if (hasOpenCuts || HasBreakCuts(BasePart.Program))
            {
                FillPath = BasePart.Program.GetFillPath(BasePart.Location);
                FillPath.Transform(plateView.Matrix);
            }
            Path = cutPath;
            EtchPath = etchPath;
            DisplayPath?.Dispose();
            DisplayPath = BuildDisplayPath(plateView.Matrix);
            LeadInPath = BasePart.HasManualLeadIns ? leadPath : null;
            if (!BasePart.HasManualLeadIns)
                leadPath.Dispose();

            var bounds = cutPath.GetBounds();
            if (etchPath.PointCount > 0)
                bounds = cutPath.PointCount > 0 ? RectangleF.Union(bounds, etchPath.GetBounds()) : etchPath.GetBounds();
            if (LeadInPath?.PointCount > 0)
                bounds = cutPath.PointCount > 0 || etchPath.PointCount > 0
                    ? RectangleF.Union(bounds, LeadInPath.GetBounds()) : LeadInPath.GetBounds();
            if (DisplayPath != null)
                bounds = cutPath.PointCount > 0 || etchPath.PointCount > 0
                    ? RectangleF.Union(bounds, DisplayPath.GetBounds()) : DisplayPath.GetBounds();
            // Include screen-space stroke width, even for a horizontal or vertical mark.
            bounds.Inflate(1, 1);
            DisplayBounds = bounds;

            // _labelPoint is computed from BaseDrawing.Program's current geometry, which already
            // carries BaseDrawing.Program.Rotation (nonzero for canonical-frame drawings, e.g. in
            // BestFitViewerForm). BasePart.Rotation is cumulative from that same baseline, so it
            // must be re-applied net of the baseline already baked into _labelPoint — otherwise
            // the baseline rotation is counted twice. Mirrors CanonicalFrame.RebindToOriginal.
            _labelPoint ??= ComputeLabelPoint();
            var baseRotation = BasePart.BaseDrawing.Program.Rotation;
            var rotatedLabel = _labelPoint.Value.Rotate(BasePart.Rotation - baseRotation);
            var labelPt = new PointF(
                (float)(rotatedLabel.X + BasePart.Location.X),
                (float)(rotatedLabel.Y + BasePart.Location.Y)
            );
            var pts = new[] { labelPt };
            plateView.Matrix.TransformPoints(pts);
            _labelScreenPoint = pts[0];

            IsDirty = false;
        }

        public void UpdateOffset(double spacing, double tolerance, Matrix matrix)
        {
            if (
                _offsetPolygonPoints == null
                || spacing != _cachedOffsetSpacing
                || tolerance != _cachedOffsetTolerance
                || BasePart.Rotation != _cachedOffsetRotation
            )
            {
                _offsetPolygonPoints = ComputeOffsetPolygons(spacing, tolerance);
                _cachedOffsetSpacing = spacing;
                _cachedOffsetTolerance = tolerance;
                _cachedOffsetRotation = BasePart.Rotation;
            }

            RebuildOffsetPath(matrix);
        }

        public void InvalidateOffset()
        {
            _offsetPolygonPoints = null;
        }

        private List<PointF[]> ComputeOffsetPolygons(double spacing, double tolerance)
        {
            var entities = ConvertProgram.ToGeometry(BasePart.Program);
            var profile = new ShapeProfile(
                entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList()
            );

            var offset = ClipperBridge.Offset(profile, spacing, tolerance);
            var result = new List<PointF[]>(offset.Outers.Count + offset.Holes.Count);

            foreach (var polygon in offset.Outers.Concat(offset.Holes))
            {
                var pts = new PointF[polygon.Vertices.Count];

                for (var j = 0; j < pts.Length; j++)
                    pts[j] = new PointF((float)polygon.Vertices[j].X, (float)polygon.Vertices[j].Y);

                result.Add(pts);
            }

            return result;
        }

        private void RebuildOffsetPath(Matrix matrix)
        {
            OffsetPath?.Dispose();

            if (_offsetPolygonPoints == null || _offsetPolygonPoints.Count == 0)
            {
                OffsetPath = null;
                return;
            }

            var path = new GraphicsPath();
            var dx = (float)BasePart.Location.X;
            var dy = (float)BasePart.Location.Y;

            foreach (var pts in _offsetPolygonPoints)
            {
                var offsetPts = new PointF[pts.Length];

                for (var i = 0; i < pts.Length; i++)
                    offsetPts[i] = new PointF(pts[i].X + dx, pts[i].Y + dy);

                path.AddLines(offsetPts);
                path.StartFigure();
            }

            path.Transform(matrix);
            OffsetPath = path;
        }

        public static LayoutPart Create(Part part, PlateView plateView)
        {
            var layoutPart = new LayoutPart(part);
            try
            {
                layoutPart.Update(plateView);
                return layoutPart;
            }
            catch
            {
                layoutPart.ReleaseDisplayResources();
                throw;
            }
        }

        public static Color SelectedColor
        {
            get { return selectedColor; }
            set
            {
                selectedColor = value;

                if (selectedBrush != null)
                    selectedBrush.Dispose();

                selectedBrush = new SolidBrush(value);

                if (selectedPen != null)
                    selectedPen.Dispose();

                selectedPen = new Pen(ControlPaint.Dark(value));
            }
        }

        public Vector Location
        {
            get { return BasePart.Location; }
            set
            {
                BasePart.Location = value;
                IsDirty = true;
            }
        }

        public double Rotation
        {
            get { return BasePart.Rotation; }
        }

        public void Rotate(double angle)
        {
            BasePart.Rotate(angle);
            IsDirty = true;
        }

        public void Rotate(double angle, Vector origin)
        {
            BasePart.Rotate(angle, origin);
            IsDirty = true;
        }

        public void Offset(double x, double y)
        {
            BasePart.Offset(x, y);
            IsDirty = true;
        }

        public void Offset(Vector voffset)
        {
            BasePart.Offset(voffset);
            IsDirty = true;
        }

        public Box BoundingBox
        {
            get { return BasePart.BoundingBox; }
        }

        public double Left
        {
            get { return BasePart.Left; }
        }

        public double Right
        {
            get { return BasePart.Right; }
        }

        public double Top
        {
            get { return BasePart.Top; }
        }

        public double Bottom
        {
            get { return BasePart.Bottom; }
        }

        public void UpdateBounds()
        {
            BasePart.UpdateBounds();
        }

        public void Update()
        {
            colorOverride = null;
            RefreshColors();
        }
    }
}
