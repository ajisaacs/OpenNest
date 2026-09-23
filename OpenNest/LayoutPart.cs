using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Clipper2Lib;
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
        private Brush brush;
        private Pen pen;

        private const int OffsetPrecision = 4;

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

            Color = part.BaseDrawing.Color;
        }

        internal bool IsDirty { get; set; }

        public bool IsSelected { get; set; }

        public GraphicsPath Path { get; private set; }

        public GraphicsPath LeadInPath { get; private set; }

        public Color Color
        {
            get { return color; }
            set
            {
                color = value;

                if (brush != null)
                    brush.Dispose();

                brush = new SolidBrush(value);

                if (pen != null)
                    pen.Dispose();

                pen = new Pen(ControlPaint.Dark(value));
            }
        }

        public void Draw(Graphics g)
        {
            if (IsSelected)
            {
                g.FillPath(selectedBrush, Path);
                g.DrawPath(selectedPen, Path);
            }
            else
            {
                g.FillPath(brush, Path);
                g.DrawPath(pen, Path);
            }

            if (LeadInPath != null)
                g.DrawPath(leadInPen, LeadInPath);
        }

        public void Draw(Graphics g, string id)
        {
            if (IsSelected)
            {
                g.FillPath(selectedBrush, Path);
                g.DrawPath(selectedPen, Path);
            }
            else
            {
                g.FillPath(brush, Path);
                g.DrawPath(pen, Path);
            }

            if (LeadInPath != null)
                g.DrawPath(leadInPen, LeadInPath);

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(
                id,
                programIdFont,
                Brushes.Black,
                _labelScreenPoint.X,
                _labelScreenPoint.Y,
                sf
            );
        }

        public GraphicsPath OffsetPath { get; private set; }

        private Vector ComputeLabelPoint()
        {
            var entities = ConvertProgram.ToGeometry(BasePart.BaseDrawing.Program);
            var nonRapid = entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList();

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

            return PolyLabel.Find(outer, holes);
        }

        public void Update(DrawControl plateView)
        {
            if (BasePart.HasManualLeadIns)
            {
                BasePart.Program.GetGraphicsPaths(
                    BasePart.Location,
                    out var cutPath,
                    out var leadPath
                );
                cutPath.Transform(plateView.Matrix);
                leadPath.Transform(plateView.Matrix);
                Path = cutPath;
                LeadInPath?.Dispose();
                LeadInPath = leadPath;
            }
            else
            {
                Path = GraphicsHelper.GetGraphicsPath(BasePart.Program, BasePart.Location);
                Path.Transform(plateView.Matrix);
                LeadInPath?.Dispose();
                LeadInPath = null;
            }

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

            // Inflate the flattened part region (perimeter positive, holes negative) in
            // one Clipper pass. Offsetting entity-by-entity leaves spikes and inverted
            // loops wherever a feature is narrower than the spacing; Clipper collapses
            // those features and drops holes that close up entirely.
            var paths = new PathsD();
            AddRegionPath(paths, profile.Perimeter, tolerance, positive: true);

            foreach (var cutout in profile.Cutouts)
                AddRegionPath(paths, cutout, tolerance, positive: false);

            var inflated = Clipper.InflatePaths(
                paths,
                spacing,
                JoinType.Round,
                EndType.Polygon,
                2.0,
                OffsetPrecision,
                tolerance
            );

            var result = new List<PointF[]>(inflated.Count);

            foreach (var path in inflated)
            {
                if (path.Count < 3)
                    continue;

                var pts = new PointF[path.Count + 1];

                for (var j = 0; j < path.Count; j++)
                    pts[j] = new PointF((float)path[j].x, (float)path[j].y);

                pts[path.Count] = pts[0];
                result.Add(pts);
            }

            return result;
        }

        private static void AddRegionPath(PathsD paths, Shape shape, double tolerance, bool positive)
        {
            var polygon = shape.ToPolygonWithTolerance(tolerance);

            if (polygon.Vertices.Count < 3)
                return;

            var path = new PathD(polygon.Vertices.Count);

            foreach (var v in polygon.Vertices)
                path.Add(new PointD(v.X, v.Y));

            if (Clipper.IsPositive(path) != positive)
                path.Reverse();

            paths.Add(path);
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
            layoutPart.Update(plateView);

            return layoutPart;
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
            Color = BasePart.BaseDrawing.Color;
        }
    }
}
