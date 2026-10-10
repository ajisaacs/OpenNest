using OpenNest.Geometry;
using PdfSharp.Drawing;

namespace OpenNest.Reporting;

/// <summary>A part ID label in canvas-local page coordinates (Y down).</summary>
internal sealed record DiagramLabel(int Part, string Id, XRect Box);

/// <summary>A dotted group boundary and its shared ID, in canvas coordinates.</summary>
internal sealed record DiagramGroup(IReadOnlyList<int> Parts, XRect Boundary, DiagramLabel Label);

internal sealed record DiagramView(XSize Size, Fit Fit, IReadOnlyList<DiagramLabel> Labels,
    IReadOnlyList<DiagramGroup> Groups);

/// <summary>Plans best-effort labels on a single plate view; small repeated parts may share a label.</summary>
internal static class NestReportDiagram
{
    internal const double LabelFontSize = 7;
    private const double LabelPadding = 1;
    // Minimum white space between a label and any edge, hole, cutoff or other label.
    private const double Clearance = 1.5;
    private const double Inset = 5;

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

    internal static DiagramView Plan(ReportPlate plate, double width, double height)
    {
        var shapes = plate.Parts.Select(part => new Shape(part.Geometry)).ToList();
        var obstacles = shapes.Concat(plate.Cutoffs.Select(cutoff => new Shape(cutoff))).ToList();
        var union = Union(plate, obstacles);
        var font = LabelFont();
        var all = Enumerable.Range(0, plate.Parts.Length).ToList();
        var canvas = new XSize(width, height);
        var view = new XRect(0, 0, width, height);
        var fit = Fit.Create(union, new XRect(Inset, Inset, width - 2 * Inset, height - 2 * Inset));
        var labels = PlaceLabels(plate, shapes, obstacles, all, fit, view, font, out var failed);
        var groups = PlaceGroups(plate, failed, labels, fit, view, font);
        return new DiagramView(canvas, fit, labels, groups);
    }

    private static List<DiagramGroup> PlaceGroups(ReportPlate plate, List<int> failed,
        List<DiagramLabel> labels, Fit fit, XRect view, XFont font)
    {
        var boxes = plate.Parts.Select(part => fit.Rect(part.Geometry.Bounds)).ToList();
        var cutoffs = plate.Cutoffs.Select(cutoff => fit.Rect(cutoff.Bounds)).ToList();
        var groups = new List<DiagramGroup>();
        // A small screen-space gap also accommodates tightly packed parts with unset spacing.
        var gap = System.Math.Max(4, plate.PartSpacing * fit.Scale * 2);
        var remaining = failed.ToHashSet();
        foreach (var seed in failed)
        {
            if (!remaining.Remove(seed))
                continue;
            var id = plate.Parts[seed].ReportId;
            var members = new List<int> { seed };
            for (var cursor = 0; cursor < members.Count; cursor++)
            {
                var nearby = boxes[members[cursor]];
                nearby.Inflate(gap, gap);
                foreach (var candidate in failed)
                {
                    if (remaining.Contains(candidate) && plate.Parts[candidate].ReportId == id
                        && nearby.IntersectsWith(boxes[candidate]))
                    {
                        remaining.Remove(candidate);
                        members.Add(candidate);
                    }
                }
            }
            if (members.Count < 2)
                continue;

            var boundary = boxes[seed];
            foreach (var member in members.Skip(1))
                boundary.Union(boxes[member]);
            boundary.Inflate(1, 1);
            var memberSet = members.ToHashSet();
            // A rectangular group must not imply that unrelated or ungrouped parts belong to it.
            if (!view.Contains(boundary)
                || boxes.Where((_, index) => !memberSet.Contains(index)).Any(box => box.IntersectsWith(boundary))
                || cutoffs.Any(box => box.IntersectsWith(boundary))
                || labels.Any(label => label.Box.IntersectsWith(boundary))
                || groups.Any(group => group.Boundary.IntersectsWith(boundary) || group.Label.Box.IntersectsWith(boundary)))
                continue;

            var size = ReportText.Size(id, font);
            var labelWidth = size.Width + 2 * LabelPadding;
            var labelHeight = size.Height + 2 * LabelPadding;
            var centerX = boundary.X + (boundary.Width - labelWidth) / 2;
            var centerY = boundary.Y + (boundary.Height - labelHeight) / 2;
            // Put the ID beside the dotted boundary, never over a small part or its hole.
            var candidates = new[]
            {
                new XRect(centerX, boundary.Y - labelHeight - Clearance, labelWidth, labelHeight),
                new XRect(centerX, boundary.Bottom + Clearance, labelWidth, labelHeight),
                new XRect(boundary.Right + Clearance, centerY, labelWidth, labelHeight),
                new XRect(boundary.X - labelWidth - Clearance, centerY, labelWidth, labelHeight),
            };
            foreach (var box in candidates)
            {
                var padded = box;
                padded.Inflate(Clearance, Clearance);
                if (!view.Contains(padded) || boxes.Any(other => other.IntersectsWith(padded))
                    || cutoffs.Any(other => other.IntersectsWith(padded))
                    || labels.Any(label => label.Box.IntersectsWith(padded))
                    || groups.Any(group => group.Boundary.IntersectsWith(padded) || group.Label.Box.IntersectsWith(padded)))
                    continue;
                groups.Add(new DiagramGroup(members, boundary, new DiagramLabel(seed, id, box)));
                break;
            }
        }
        return groups;
    }

    /// <summary>
    /// Centers each ID on its part's pole of inaccessibility (<see cref="PolyLabel"/>, as PlateView
    /// does), so it sits deepest inside the material and clear of holes. A label box that would
    /// touch an edge, hole, cutoff or another label is not moved elsewhere: the individual label is skipped here
    /// and may share a group label or remain unlabeled.
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
