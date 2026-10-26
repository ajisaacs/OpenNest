using System;
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
        private static Color selectedColor = Color.FromArgb(90, 150, 200, 255);

        // GDI+ objects are not thread-safe, so views on different UI threads each draw
        // with their own; sharing one fails with "Object is currently in use elsewhere".
        [ThreadStatic] private static Font programIdFont;
        [ThreadStatic] private static Pen selectedPen;
        [ThreadStatic] private static Brush selectedBrush;
        [ThreadStatic] private static Pen leadInPen;
        [ThreadStatic] private static StringFormat labelFormat;

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

        internal PointF LabelScreenPoint => _labelScreenPoint;

        // The font is fixed, so an id's screen size changes only with the id.
        private string measuredLabel;
        private SizeF measuredLabelSize;

        // Paths in the drawing's program frame, before placement. A zoom or a move only maps
        // these to the screen again; rebuilding them from the program dominates for parts with
        // hundreds of holes. They are rebuilt when the program, its rotation or its lead-ins change.
        private GraphicsPath localCut;
        private GraphicsPath localLead;
        private GraphicsPath localEtch;
        private GraphicsPath localFill;
        private GraphicsPath localDisplay;
        private CNC.Program localProgram;
        private double localRotation;
        private bool localManualLeadIns;
        private bool needsRebuild;
        private bool moved;

        public readonly Part BasePart;

        private LayoutPart(Part part)
        {
            this.BasePart = part;

            if (part.BaseDrawing.Color.IsEmpty)
                part.BaseDrawing.Color = Color.FromArgb(130, 204, 130);

            SetDisplayColor(part.BaseDrawing.Color);
        }

        /// <summary>Setting it rebuilds the paths from the program on the next update.</summary>
        internal bool IsDirty
        {
            get => needsRebuild || moved;
            set
            {
                needsRebuild = value;
                moved = false;
            }
        }

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

        internal void Draw(Graphics g, bool muted)
        {
            RefreshColors();
            using var schemeSelectionBrush = IsSelected && !colorScheme.SelectedPartColor.IsEmpty
                ? new SolidBrush(colorScheme.SelectedPartColor) : null;
            g.FillPath(muted ? Brushes.Gainsboro : schemeSelectionBrush ?? (IsSelected ? SelectedBrush : brush),
                MaterialPath);
            DrawDisplay(g, muted);
            DrawEtch(g, muted);
            // Keep real cuts visible even where an etch overlaps them.
            g.DrawPath(muted ? Pens.Gray : IsSelected && colorScheme.PartOutlineColor.IsEmpty ? SelectedPen : pen, Path);

            if (LeadInPath != null)
                g.DrawPath(muted ? Pens.Gray : LeadInPen, LeadInPath);
        }

        /// <summary>
        /// Draws the part in the scheme's library style, ignoring its drawing color: library
        /// previews show drawings that are not in the nest yet.
        /// </summary>
        internal void DrawLibrary(Graphics g, ColorScheme scheme)
        {
            using var fill = new SolidBrush(scheme.LibraryFillColor);
            using var outline = new Pen(scheme.LibraryOutlineColor);
            using var etch = new Pen(scheme.LibraryEtchColor, 1.5f);
            g.FillPath(fill, MaterialPath);
            DrawDisplay(g, false);
            if (EtchPath?.PointCount > 0)
                g.DrawPath(etch, EtchPath);
            // Keep real cuts visible even where an etch overlaps them.
            g.DrawPath(outline, Path);
        }

        public void Draw(Graphics g, string id) => Draw(g, id, false);

        internal void Draw(Graphics g, string id, bool muted)
        {
            Draw(g, muted);
            if (LabelFits(MeasureLabel(g, id)))
                DrawLabel(g, id, muted);
        }

        /// <summary>
        /// Draws the id over a disc of <paramref name="background"/>, so a line drawn through the
        /// label's point, such as a cut-off's own cut, does not strike through the number.
        /// </summary>
        internal void DrawLabel(Graphics g, string id, bool muted, Brush background)
        {
            var size = MeasureLabel(g, id);
            if (!LabelFits(size))
                return;
            var diameter = LabelDiscDiameter(size);
            g.FillEllipse(background,
                _labelScreenPoint.X - diameter / 2, _labelScreenPoint.Y - diameter / 2, diameter, diameter);
            DrawLabel(g, id, muted);
        }

        private SizeF MeasureLabel(Graphics g, string id)
        {
            if (id != measuredLabel)
            {
                measuredLabelSize = g.MeasureString(id, ProgramIdFont, PointF.Empty, LabelFormat);
                measuredLabel = id;
            }
            return measuredLabelSize;
        }

        private static float LabelDiscDiameter(SizeF label) => System.Math.Max(label.Width, label.Height) + 2;

        // As the CAD converter numbers entities: an id shows only once the zoom makes the part big
        // enough on screen to hold it, so zoomed out, small parts do not pile their ids on each other.
        // A cut-off segment is a line; its length must hold the id's disc.
        private bool LabelFits(SizeF label)
        {
            // DisplayBounds carries a pixel of stroke width on each side.
            var width = DisplayBounds.Width - 2;
            var height = DisplayBounds.Height - 2;
            return BasePart.BaseDrawing.IsCutOff
                ? System.Math.Max(width, height) >= LabelDiscDiameter(label)
                : width >= label.Width && height >= label.Height;
        }

        private void DrawLabel(Graphics g, string id, bool muted)
        {
            g.DrawString(
                id,
                ProgramIdFont,
                muted ? Brushes.DimGray : Brushes.Black,
                _labelScreenPoint.X,
                _labelScreenPoint.Y,
                LabelFormat
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
            ReleaseLocalPaths();
        }

        private void ReleaseLocalPaths()
        {
            localCut?.Dispose();
            localCut = null;
            localLead?.Dispose();
            localLead = null;
            localEtch?.Dispose();
            localEtch = null;
            localFill?.Dispose();
            localFill = null;
            localDisplay?.Dispose();
            localDisplay = null;
            localProgram = null;
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
        private GraphicsPath BuildDisplayPath()
        {
            var entities = EntityRoles.DisplayGeometry(BasePart.BaseDrawing);
            if (entities.Count == 0)
                return null;

            var path = new GraphicsPath();
            foreach (var entity in entities)
            {
                entity.Rotate(BasePart.Rotation);
                using var figure = new Shape { Entities = new List<Entity> { entity } }.GetGraphicsPath();
                path.AddPath(figure, false);
            }
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

        /// <summary>Rebuilds the part's paths from its program and maps them to the view.</summary>
        public void Update(DrawControl plateView)
        {
            BuildLocalPaths();
            ApplyView(plateView);
        }

        /// <summary>
        /// Maps the part to the view, rebuilding its paths only when <see cref="IsDirty"/> was set
        /// or its program, rotation or lead-ins changed. Use after a zoom or a move.
        /// </summary>
        internal void UpdateView(DrawControl plateView)
        {
            if (needsRebuild
                || localCut == null
                || !ReferenceEquals(localProgram, BasePart.Program)
                || localRotation != BasePart.Rotation
                || localManualLeadIns != BasePart.HasManualLeadIns)
                BuildLocalPaths();

            ApplyView(plateView);
        }

        private void BuildLocalPaths()
        {
            ReleaseLocalPaths();
            var program = BasePart.Program;
            program.GetDisplayPaths(Vector.Zero,
                out var cutPath, out var leadPath, out var etchPath, out var hasOpenCuts);
            localCut = cutPath;
            localEtch = etchPath;
            // Only programs with break cuts or open (tabbed) cuts need a fill apart from the outline path.
            if (hasOpenCuts || HasBreakCuts(program))
                localFill = program.GetFillPath(Vector.Zero);
            localLead = BasePart.HasManualLeadIns ? leadPath : null;
            if (!BasePart.HasManualLeadIns)
                leadPath.Dispose();
            localDisplay = BuildDisplayPath();

            localProgram = program;
            localRotation = BasePart.Rotation;
            localManualLeadIns = BasePart.HasManualLeadIns;
            needsRebuild = false;
        }

        private static GraphicsPath Transformed(GraphicsPath path, Matrix matrix)
        {
            if (path == null)
                return null;
            var result = (GraphicsPath)path.Clone();
            result.Transform(matrix);
            return result;
        }

        private void ApplyView(DrawControl plateView)
        {
            colorScheme = (plateView as PlateView)?.ColorScheme ?? ColorScheme.Default;
            RefreshColors();

            // Place the program-frame paths, then map them to the screen.
            using var matrix = plateView.Matrix.Clone();
            matrix.Translate((float)BasePart.Location.X, (float)BasePart.Location.Y);

            Path?.Dispose();
            LeadInPath?.Dispose();
            EtchPath?.Dispose();
            FillPath?.Dispose();
            DisplayPath?.Dispose();
            var cutPath = Path = Transformed(localCut, matrix);
            var etchPath = EtchPath = Transformed(localEtch, matrix);
            FillPath = Transformed(localFill, matrix);
            LeadInPath = Transformed(localLead, matrix);
            DisplayPath = Transformed(localDisplay, matrix);

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
            // must be re-applied net of the baseline already baked into _labelPoint â€” otherwise
            // the baseline rotation is counted twice. Mirrors CanonicalFrame.RebindToOriginal.
            var label = CutOffSegmentLabelPoint() ?? PlacedLabelPoint();
            var pts = new[] { new PointF((float)label.X, (float)label.Y) };
            plateView.Matrix.TransformPoints(pts);
            _labelScreenPoint = pts[0];

            IsDirty = false;
        }

        private Vector PlacedLabelPoint()
        {
            _labelPoint ??= ComputeLabelPoint();
            var baseRotation = BasePart.BaseDrawing.Program.Rotation;
            return _labelPoint.Value.Rotate(BasePart.Rotation - baseRotation) + BasePart.Location;
        }

        // Every segment of a cut-off is a part of the one cut-off drawing with its own trimmed
        // program. Labelled from the drawing, all of them would stack on one point and the view's
        // culling would decide which number shows; label each at the middle of its own cut.
        private Vector? CutOffSegmentLabelPoint()
        {
            var program = BasePart.Program;
            if (!BasePart.BaseDrawing.IsCutOff || program?.Mode != CNC.Mode.Absolute)
                return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            Vector? position = null;
            foreach (var code in program.Codes)
            {
                if (code is not CNC.Motion motion)
                    continue;
                if (motion is not CNC.RapidMove && position is { } start)
                {
                    foreach (var point in new[] { start, motion.EndPoint })
                    {
                        minX = System.Math.Min(minX, point.X);
                        minY = System.Math.Min(minY, point.Y);
                        maxX = System.Math.Max(maxX, point.X);
                        maxY = System.Math.Max(maxY, point.Y);
                    }
                }
                position = motion.EndPoint;
            }

            if (minX > maxX)
                return null;
            return new Vector((minX + maxX) / 2, (minY + maxY) / 2) + BasePart.Location;
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
            set { selectedColor = value; }
        }

        private static Font ProgramIdFont =>
            programIdFont ??= new Font(SystemFonts.DefaultFont, FontStyle.Bold | FontStyle.Underline);

        private static Pen LeadInPen => leadInPen ??= new Pen(Color.OrangeRed, 1.5f);

        private static StringFormat LabelFormat => labelFormat ??= new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        private static Brush SelectedBrush
        {
            get
            {
                if (selectedBrush is not SolidBrush current || current.Color != selectedColor)
                {
                    selectedBrush?.Dispose();
                    selectedBrush = new SolidBrush(selectedColor);
                }

                return selectedBrush;
            }
        }

        private static Pen SelectedPen
        {
            get
            {
                var color = ControlPaint.Dark(selectedColor);
                if (selectedPen == null || selectedPen.Color != color)
                {
                    selectedPen?.Dispose();
                    selectedPen = new Pen(color);
                }

                return selectedPen;
            }
        }

        public Vector Location
        {
            get { return BasePart.Location; }
            set
            {
                BasePart.Location = value;
                moved = true;
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
            moved = true;
        }

        public void Offset(Vector voffset)
        {
            BasePart.Offset(voffset);
            moved = true;
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
