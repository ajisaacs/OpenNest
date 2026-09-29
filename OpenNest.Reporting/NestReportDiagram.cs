using OpenNest.Geometry;
using PdfSharp.Drawing;

namespace OpenNest.Reporting;

/// <summary>A part ID label in canvas-local page coordinates (Y down).</summary>
internal sealed record DiagramLabel(int Part, string Id, XRect Box);

/// <summary>Map grid over the overview; rows are lettered from the top, columns numbered from the left.</summary>
internal sealed record DiagramGrid(ReportBounds Union, double CellLength, double CellWidth, int Columns, int Rows,
    IReadOnlyList<(int Row, int Column)> DetailCells)
{
    public string Name(int row, int column) => ReportText.RowName(row) + (column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public ReportBounds Cell(int row, int column)
    {
        var left = Union.Left + column * CellLength;
        var top = Union.Top - row * CellWidth;
        return new ReportBounds(left, top - CellWidth, left + CellLength, top);
    }
}

/// <summary>One drawn view of a plate: the fitted overview or a zoomed grid-cell detail.</summary>
internal sealed record DiagramView(XSize Size, Fit Fit, IReadOnlyList<DiagramLabel> Labels,
    DiagramGrid? Grid, string? Cell, ReportBounds? CellBounds);

internal sealed record PlateDiagramPlan(DiagramView Overview, IReadOnlyList<DiagramView> Details);

/// <summary>
/// Plans labels before any PDF output. IDs are never shrunk below <see cref="LabelFontSize"/> or
/// dropped: a label that cannot sit legibly inside its part's material at overview scale is placed
/// in a zoomed map-grid detail view instead, or the report fails with the part identified.
/// </summary>
internal static class NestReportDiagram
{
    internal const double LabelFontSize = 7;
    internal const int MaxDetailViews = 24;
    private const double LabelPadding = 1;
    // Minimum white space between a label and any edge, hole, cutoff or other label.
    private const double Clearance = 1.5;
    private const double Inset = 5;
    private const double GutterLeft = 16;
    private const double GutterTop = 12;
    private const double DetailMargin = 0.08;
    private static readonly double[] Zooms = [1, 2, 4];

    internal static XFont LabelFont()
    {
        ReportFonts.Initialize();
        return new(ReportFonts.Family, LabelFontSize, XFontStyleEx.Bold);
    }

    /// <summary>Chord tolerance for placement-only flattening, relative to the part size.</summary>
    internal static double ChordTolerance(ReportBounds bounds) =>
        System.Math.Max(System.Math.Max(bounds.Length, bounds.Width) * 0.0005, 1e-9);

    /// <summary>PolyLabel search precision, relative to the part size.</summary>
    internal static double PolePrecision(ReportBounds bounds) =>
        System.Math.Max(System.Math.Max(bounds.Length, bounds.Width) * 0.005, 1e-9);

    internal static PlateDiagramPlan Plan(ReportPlate plate, double width, double overviewHeight, double detailHeight)
    {
        var shapes = plate.Parts.Select(part => new Shape(part.Geometry)).ToList();
        var obstacles = shapes.Concat(plate.Cutoffs.Select(cutoff => new Shape(cutoff))).ToList();
        var union = Union(plate, obstacles);
        var font = LabelFont();
        var all = Enumerable.Range(0, plate.Parts.Length).ToList();
        var canvas = new XSize(width, overviewHeight);
        var overviewView = new XRect(0, 0, width, overviewHeight);

        var fit = Fit.Create(union, new XRect(Inset, Inset, width - 2 * Inset, overviewHeight - 2 * Inset));
        var labels = PlaceLabels(plate, shapes, obstacles, all, fit, overviewView, font, out var failed);
        if (failed.Count == 0)
            return new PlateDiagramPlan(new DiagramView(canvas, fit, labels, null, null, null), []);

        // Reserve map-grid gutters, then re-place what still fits at overview scale.
        fit = Fit.Create(union, new XRect(GutterLeft, GutterTop, width - GutterLeft - Inset, overviewHeight - GutterTop - Inset));
        labels = PlaceLabels(plate, shapes, obstacles, all, fit, overviewView, font, out failed);
        var failedSet = failed.ToHashSet();
        var required = failed.Max(index => RequiredScale(plate, index, font));
        var inner = new XRect(Inset, Inset, width - 2 * Inset, detailHeight - 2 * Inset);
        var legibleGrid = false;
        foreach (var zoom in Zooms)
        {
            var scale = required * zoom;
            // Cells evenly divide the sheet (no overrun); each is at most the size a detail view
            // shows at the required scale, so the zoom only increases.
            var columns = System.Math.Max(1, (int)System.Math.Ceiling(union.Length * scale * (1 + 2 * DetailMargin) / inner.Width));
            var rows = System.Math.Max(1, (int)System.Math.Ceiling(union.Width * scale * (1 + 2 * DetailMargin) / inner.Height));
            var cellLength = union.Length / columns;
            var cellWidth = union.Width / rows;
            var grid = new DiagramGrid(union, cellLength, cellWidth, columns, rows, []);
            // Every grid line and gutter name must stay readable on the overview.
            if (!GridIsLegible(grid, fit, font))
                continue;
            legibleGrid = true;
            (int Row, int Column) CellOf(int index) => CellIndex(plate, index, union, columns, rows);

            var cells = failed.Select(CellOf).Distinct().OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToList();
            if (cells.Count > MaxDetailViews)
                throw new NotSupportedException($"Plate {plate.Number}: legible part ID labels would need {cells.Count} detail views; this report supports at most {MaxDetailViews} per plate.");
            grid = grid with { DetailCells = cells };
            var details = new List<DiagramView>();
            var unplaced = new List<int>();
            foreach (var (row, column) in cells)
            {
                var cell = grid.Cell(row, column);
                var marginX = cellLength * DetailMargin;
                var marginY = cellWidth * DetailMargin;
                var view = new ReportBounds(cell.Left - marginX, cell.Bottom - marginY, cell.Right + marginX, cell.Top + marginY);
                var detailFit = Fit.Create(view, inner);
                // Parts missing from the overview claim label space first.
                var members = all.Where(index => CellOf(index) == (row, column))
                    .OrderBy(index => failedSet.Contains(index) ? 0 : 1).ToList();
                var detailLabels = PlaceLabels(plate, shapes, obstacles, members, detailFit,
                    new XRect(0, 0, width, detailHeight), font, out var missed);
                unplaced.AddRange(missed.Where(failedSet.Contains));
                details.Add(new DiagramView(new XSize(width, detailHeight), detailFit, detailLabels, null,
                    grid.Name(row, column), cell));
            }
            if (unplaced.Count == 0)
                return new PlateDiagramPlan(new DiagramView(canvas, fit, labels, grid, null, null), details);
        }

        var worst = failed.First();
        if (!legibleGrid)
            throw new NotSupportedException($"Plate {plate.Number}, part {worst + 1} ({plate.Parts[worst].ReportId}): the part is too small relative to the sheet for a legible detail-view grid; this is not supported by the report.");
        throw new NotSupportedException($"Plate {plate.Number}, part {worst + 1} ({plate.Parts[worst].ReportId}): its ID label cannot be placed legibly inside the part's material, even in a zoomed detail view; this is not supported by the report.");
    }

    /// <summary>Grid cell holding a part's bounds center; rows count down from the top.</summary>
    private static (int Row, int Column) CellIndex(ReportPlate plate, int index, ReportBounds union, int columns, int rows)
    {
        var bounds = plate.Parts[index].Geometry.Bounds;
        var x = (bounds.Left + bounds.Right) / 2;
        var y = (bounds.Bottom + bounds.Top) / 2;
        return (System.Math.Clamp((int)((union.Top - y) / (union.Width / rows)), 0, rows - 1),
            System.Math.Clamp((int)((x - union.Left) / (union.Length / columns)), 0, columns - 1));
    }

    /// <summary>Grid spacing on the overview must exceed the gutter names it carries.</summary>
    internal static bool GridIsLegible(DiagramGrid grid, Fit fit, XFont font)
    {
        var column = ReportText.Size(grid.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture), font);
        var row = ReportText.Size(ReportText.RowName(grid.Rows - 1), font);
        return grid.CellLength * fit.Scale >= column.Width + 2 && grid.CellWidth * fit.Scale >= row.Height + 1
            && row.Width + 2 <= GutterLeft && column.Height + 2 <= GutterTop;
    }

    /// <summary>
    /// Centers each ID on its part's pole of inaccessibility (<see cref="PolyLabel"/>, as PlateView
    /// does), so it sits deepest inside the material and clear of holes. A label box that would
    /// touch an edge, hole, cutoff or another label is not moved elsewhere: the part fails here
    /// and gets a zoomed detail view instead.
    /// </summary>
    private static List<DiagramLabel> PlaceLabels(ReportPlate plate, List<Shape> shapes, List<Shape> obstacles,
        List<int> targets, Fit fit, XRect view, XFont font, out List<int> failed)
    {
        failed = [];
        var placed = new List<DiagramLabel>();
        foreach (var index in targets)
        {
            var id = plate.Parts[index].ReportId;
            var size = ReportText.Size(id, font);
            var width = size.Width + 2 * LabelPadding;
            var height = size.Height + 2 * LabelPadding;
            var pole = fit.Point(shapes[index].Pole.X, shapes[index].Pole.Y);
            var box = new XRect(pole.X - width / 2, pole.Y - height / 2, width, height);
            var topLeft = fit.Model(new XPoint(box.X - Clearance, box.Y - Clearance));
            var bottomRight = fit.Model(new XPoint(box.Right + Clearance, box.Bottom + Clearance));
            var test = new Box(topLeft.X, bottomRight.Y, bottomRight.X - topLeft.X, topLeft.Y - bottomRight.Y);
            if (view.Contains(box) && !placed.Any(label => label.Box.IntersectsWith(box))
                && !obstacles.Any(shape => shape.Crosses(test)))
                placed.Add(new DiagramLabel(index, id, box));
            else
                failed.Add(index);
        }
        return placed;
    }

    /// <summary>Scale at which the label would fit the part's bounds, with room to avoid holes and strokes.</summary>
    private static double RequiredScale(ReportPlate plate, int index, XFont font)
    {
        var bounds = plate.Parts[index].Geometry.Bounds;
        if (!(bounds.Length > 0) || !(bounds.Width > 0))
            throw new NotSupportedException($"Plate {plate.Number}, part {index + 1} ({plate.Parts[index].ReportId}): the part has no area to hold its ID label; this is not supported by the report.");
        var size = ReportText.Size(plate.Parts[index].ReportId, font);
        var scale = System.Math.Max((size.Width + 2 * (LabelPadding + Clearance)) / bounds.Length,
            (size.Height + 2 * (LabelPadding + Clearance)) / bounds.Width) * 1.5;
        if (!double.IsFinite(scale))
            throw new NotSupportedException($"Plate {plate.Number}, part {index + 1} ({plate.Parts[index].ReportId}): the part is too small to hold a legible ID label.");
        return scale;
    }

    private static ReportBounds Union(ReportPlate plate, IEnumerable<Shape> shapes)
    {
        var left = plate.Bounds.Left;
        var bottom = plate.Bounds.Bottom;
        var right = plate.Bounds.Right;
        var top = plate.Bounds.Top;
        foreach (var shape in shapes)
        {
            left = System.Math.Min(left, shape.Bounds.Left);
            bottom = System.Math.Min(bottom, shape.Bounds.Bottom);
            right = System.Math.Max(right, shape.Bounds.Right);
            top = System.Math.Max(top, shape.Bounds.Top);
        }
        return new ReportBounds(left, bottom, right, top);
    }

    /// <summary>
    /// Contours flattened with the same chord-tolerance routine as overlap/validation region
    /// preparation, used only for label placement and never for drawing. Open contours are
    /// closed here so a label cannot sit in a tab gap; rendering never closes them.
    /// </summary>
    private sealed class Shape
    {
        public Shape(ReportGeometry geometry)
        {
            Bounds = geometry.Bounds;
            Box = new Box(Bounds.Left, Bounds.Bottom, Bounds.Length, Bounds.Width);
            Rings = geometry.Contours.Select(contour => ClipperBridge.Flatten(ToShape(contour),
                ChordTolerance(Bounds), circumscribe: false)).ToList();
            lazyPole = new Lazy<Vector>(FindPole);
        }

        private readonly Lazy<Vector> lazyPole;

        public ReportBounds Bounds { get; }

        /// <summary>Pole of inaccessibility: the material point farthest from every edge.</summary>
        public Vector Pole => lazyPole.Value;

        private Box Box { get; }

        private List<Polygon> Rings { get; }

        /// <summary>
        /// Like PlateView, which finds the pole once in the drawing's own frame, search a
        /// placement-independent copy: symmetric shapes (a square with a central hole) have
        /// several equally deep poles, and sheet-position roundoff must not pick different ones
        /// for identical parts. Largest ring is the perimeter, the rest holes (ShapeProfile's rule).
        /// </summary>
        private Vector FindPole()
        {
            if (Rings.Count == 0)
                return new Vector();
            var grain = System.Math.Max(Bounds.Length, Bounds.Width) * 1e-9;
            Polygon Local(Polygon ring)
            {
                var local = new Polygon();
                local.Vertices.AddRange(ring.Vertices.Select(vertex => new Vector(
                    System.Math.Round((vertex.X - Bounds.Left) / grain) * grain,
                    System.Math.Round((vertex.Y - Bounds.Bottom) / grain) * grain)));
                return local;
            }
            var ordered = Rings.OrderByDescending(ring => ring.Area()).Select(Local).ToList();
            var pole = PolyLabel.Find(ordered[0], ordered.Skip(1).ToList(), PolePrecision(Bounds));
            return new Vector(pole.X + Bounds.Left, pole.Y + Bounds.Bottom);
        }

        /// <summary>Whether any contour edge lies in or crosses the model-space rectangle.</summary>
        public bool Crosses(Box box)
        {
            if (!Box.Intersects(box))
                return false;
            var outline = new Polygon();
            outline.Vertices.AddRange([new Vector(box.Left, box.Bottom), new Vector(box.Right, box.Bottom),
                new Vector(box.Right, box.Top), new Vector(box.Left, box.Top)]);
            outline.Close();
            return Rings.Any(ring => ring.Vertices.Any(box.Contains) || ring.Intersects(outline));
        }

        private static OpenNest.Geometry.Shape ToShape(ReportContour contour)
        {
            var shape = new OpenNest.Geometry.Shape();
            foreach (var segment in contour.Segments)
            {
                if (segment.Center == null)
                {
                    shape.Entities.Add(new Line(ToVector(segment.Start), ToVector(segment.End)));
                    continue;
                }
                var center = ToVector(segment.Center);
                if (System.Math.Abs(segment.SweepAngle) >= 2 * System.Math.PI)
                {
                    shape.Entities.Add(new Circle(center, segment.Radius)
                    {
                        Rotation = segment.SweepAngle < 0 ? RotationType.CW : RotationType.CCW,
                    });
                    continue;
                }
                // A reversed arc runs from StartAngle toward the (smaller) EndAngle.
                shape.Entities.Add(new Arc(center, segment.Radius, segment.StartAngle,
                    segment.StartAngle + segment.SweepAngle, reversed: segment.SweepAngle < 0));
            }
            return shape;
        }

        private static Vector ToVector(ReportPoint point) => new(point.X, point.Y);
    }
}

/// <summary>One aspect-preserving model-to-canvas transform, centered, with the Y axis flipped.</summary>
internal readonly record struct Fit(ReportBounds Bounds, double Scale, double X, double Y)
{
    public static Fit Create(ReportBounds bounds, XRect area)
    {
        var scales = new List<double>(2);
        if (bounds.Length > 0)
            scales.Add(area.Width / bounds.Length);
        if (bounds.Width > 0)
            scales.Add(area.Height / bounds.Width);
        var scale = scales.Count == 0 ? 1 : scales.Min();
        if (!double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("Report geometry cannot be fitted to the page.");
        return new Fit(bounds, scale,
            area.X + (area.Width - bounds.Length * scale) / 2,
            area.Y + (area.Height - bounds.Width * scale) / 2);
    }

    public XPoint Point(ReportPoint point) => Point(point.X, point.Y);

    public XPoint Point(double x, double y) => new(X + (x - Bounds.Left) * Scale, Y + (Bounds.Top - y) * Scale);

    public XPoint Model(XPoint local) => new(Bounds.Left + (local.X - X) / Scale, Bounds.Top - (local.Y - Y) / Scale);

    public XRect Rect(ReportBounds box)
    {
        var low = Point(box.Left, box.Top);
        return new XRect(low.X, low.Y, box.Length * Scale, box.Width * Scale);
    }
}
