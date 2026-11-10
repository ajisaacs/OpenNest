using System.Collections.Immutable;
using OpenNest.Geometry;
using PdfSharp.Drawing;

namespace OpenNest.Reporting;

/// <summary>A part ID label in canvas-local page coordinates (Y down).</summary>
internal sealed record DiagramLabel(int Part, string Id, XRect Box);

/// <summary>A dotted group boundary and its shared ID, in canvas coordinates.</summary>
internal sealed record DiagramGroup(IReadOnlyList<int> Parts, XRect Boundary, DiagramLabel Label);

internal sealed record DiagramView(XSize Size, Fit Fit, IReadOnlyList<DiagramLabel> Labels,
    IReadOnlyList<DiagramGroup> Groups, DiagramRulers Rulers);

/// <summary>
/// Sheet rulers in model units: a horizontal ruler under the drawing and a vertical one to its
/// left, each reading plate coordinates across the sheet. <see cref="Horizontal"/> and
/// <see cref="Vertical"/> are the canvas bands they occupy, which labels keep clear of.
/// </summary>
internal sealed record DiagramRulers(double Step, int Divisions, int Decimals, XRect Horizontal, XRect Vertical)
{
    /// <summary>Major tick values: multiples of <see cref="Step"/> within [<paramref name="from"/>, <paramref name="to"/>].</summary>
    public IEnumerable<double> Majors(double from, double to) => Ticks(from, to, Step);

    /// <summary>Minor tick values between the majors.</summary>
    public IEnumerable<double> Minors(double from, double to) =>
        Ticks(from, to, Step / Divisions).Where(value => !IsMajor(value));

    public string Format(double value)
    {
        var text = value.ToString("F" + Decimals, System.Globalization.CultureInfo.InvariantCulture);
        // Roundoff can leave a negative zero ("-0" or "-0.0"); print it as zero.
        return text[0] == '-' && text.AsSpan(1).Trim("0.").IsEmpty ? text[1..] : text;
    }

    private bool IsMajor(double value)
    {
        var ratio = value / Step;
        return System.Math.Abs(ratio - System.Math.Round(ratio)) < 1e-6;
    }

    private static IEnumerable<double> Ticks(double from, double to, double step)
    {
        // Integer multiples avoid accumulated roundoff; a sliver of tolerance keeps the sheet's
        // own edges (0 and its size) when they are multiples.
        var slack = step * 1e-6;
        var first = System.Math.Ceiling((from - slack) / step);
        var last = System.Math.Floor((to + slack) / step);
        for (var index = first; index <= last; index++)
            yield return index * step;
    }
}

/// <summary>Plans best-effort labels on a single plate view; small repeated parts may share a label.</summary>
internal static class NestReportDiagram
{
    internal const double LabelFontSize = 7;
    private const double LabelPadding = 1;
    // Minimum white space between a label and any edge, hole, cutoff or other label.
    private const double Clearance = 1.5;
    private const double Inset = 5;

    internal const double RulerFontSize = 6;
    // Canvas points between the drawing and a ruler's baseline, tick lengths, and the space
    // between a major tick and its number.
    internal const double RulerGap = 3;
    internal const double MajorTick = 5;
    internal const double MinorTick = 2.5;
    internal const double RulerTextGap = 1.5;
    // Numbered ticks at least this far apart, minor ticks no closer than the second.
    private const double MinMajorSpacing = 40;
    private const double MinMinorSpacing = 4;

    internal static XFont LabelFont()
    {
        ReportFonts.Initialize();
        return new(ReportFonts.Family, LabelFontSize, XFontStyleEx.Bold);
    }

    internal static XFont RulerFont()
    {
        ReportFonts.Initialize();
        return new(ReportFonts.Family, RulerFontSize, XFontStyleEx.Regular);
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
        // Active clamps are fixtures labels must not cover; a removed clamp's area may hold parts.
        var obstacles = shapes.Concat(plate.Cutoffs.Select(cutoff => new Shape(cutoff)))
            .Concat(plate.Clamps.Where(clamp => !clamp.Removed).Select(clamp => new Shape(Rectangle(clamp.Bounds))))
            .ToList();
        // Whole clamp footprints can hang past the sheet; keep them on the page.
        var union = Union(plate.Bounds, obstacles.Select(shape => shape.Bounds)
            .Concat(plate.Clamps.Select(clamp => clamp.Bounds)));
        var (fit, rulers) = FitWithRulers(plate.Bounds, union, width, height);
        var font = LabelFont();
        var all = Enumerable.Range(0, plate.Parts.Length).ToList();
        var canvas = new Canvas(new XRect(0, 0, width, height), [rulers.Horizontal, rulers.Vertical]);
        var labels = PlaceLabels(plate, shapes, obstacles, all, fit, canvas, font, out var failed);
        var groups = PlaceGroups(plate, obstacles, failed, labels, fit, canvas, font);
        return new DiagramView(new XSize(width, height), fit, labels, groups, rulers);
    }

    /// <summary>
    /// Fits the drawing beside a vertical ruler on its left and a horizontal one below it. A
    /// first fit with provisional bands picks the tick step; the second sizes the bands to that
    /// step's widest numbers. Each ruler hugs the drawing and spans the sheet.
    /// </summary>
    private static (Fit Fit, DiagramRulers Rulers) FitWithRulers(ReportBounds sheet, ReportBounds union,
        double width, double height)
    {
        var font = RulerFont();
        var textHeight = font.GetHeight();
        var below = RulerGap + MajorTick + RulerTextGap + textHeight;
        XRect Area(double left, double right) =>
            new(Inset + left, Inset, width - 2 * Inset - left - right, height - 2 * Inset - below);

        var draft = Ruler(sheet, Fit.Create(union, Area(30, 10)).Scale, font);
        double Widest(double from, double to) => draft.Majors(from, to)
            .Select(value => ReportText.Size(draft.Format(value), font).Width).DefaultIfEmpty(0).Max();
        var left = RulerGap + MajorTick + RulerTextGap + Widest(sheet.Bottom, sheet.Top);
        var overhang = Widest(sheet.Left, sheet.Right) / 2;
        var fit = Fit.Create(union, Area(left, overhang));

        var drawing = fit.Rect(union);
        var low = fit.Point(sheet.Left, sheet.Top);
        var high = fit.Point(sheet.Right, sheet.Bottom);
        var horizontal = new XRect(low.X - overhang, drawing.Bottom + RulerGap, high.X - low.X + 2 * overhang,
            below - RulerGap);
        var vertical = new XRect(drawing.Left - left, low.Y - textHeight / 2, left - RulerGap,
            high.Y - low.Y + textHeight);
        return (fit, draft with { Horizontal = horizontal, Vertical = vertical });
    }

    /// <summary>
    /// The smallest 1-2-5 step whose numbered ticks sit <see cref="MinMajorSpacing"/> apart and
    /// whose numbers do not touch, with the finest legible subdivision of it.
    /// </summary>
    private static DiagramRulers Ruler(ReportBounds sheet, double scale, XFont font)
    {
        var exponent = (int)System.Math.Floor(System.Math.Log10(MinMajorSpacing / scale));
        for (; ; exponent++)
        {
            foreach (var mantissa in new[] { 1, 2, 5 })
            {
                var step = mantissa * System.Math.Pow(10, exponent);
                var spacing = step * scale;
                if (spacing < MinMajorSpacing)
                    continue;
                var decimals = exponent >= 0 ? 0 : -exponent;
                int[] divisions = mantissa switch { 1 => [10, 5, 2], 2 => [4, 2], _ => [5] };
                var ruler = new DiagramRulers(step,
                    divisions.FirstOrDefault(count => spacing / count >= MinMinorSpacing, 1), decimals, default, default);
                var widest = ruler.Majors(sheet.Left, sheet.Right).Concat(ruler.Majors(sheet.Bottom, sheet.Top))
                    .Select(value => ReportText.Size(ruler.Format(value), font).Width).DefaultIfEmpty(0).Max();
                if (widest + 2 * RulerTextGap <= spacing)
                    return ruler;
            }
        }
    }

    /// <summary>A closed axis-aligned rectangle contour, for clamp footprints.</summary>
    private static ReportGeometry Rectangle(ReportBounds bounds)
    {
        ReportPoint[] corners = [new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom),
            new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top)];
        var segments = Enumerable.Range(0, 4)
            .Select(index => new ReportSegment(corners[index], corners[(index + 1) % 4], null, 0, 0, 0))
            .ToImmutableArray();
        return new ReportGeometry([new ReportContour(segments, true)], bounds);
    }

    /// <summary>The canvas labels may use: inside the view and clear of the ruler bands.</summary>
    private sealed record Canvas(XRect View, IReadOnlyList<XRect> Reserved)
    {
        public bool Holds(XRect rect) => View.Contains(rect) && !Reserved.Any(band => band.IntersectsWith(rect));
    }

    /// <summary>
    /// Groups unlabeled same-ID neighbors. Clearance against other parts and cutoffs is tested on
    /// their real contours, not their bounding boxes: a cluster nested in a large part's hole or
    /// concave notch lies inside that part's bounding box but touches none of its material.
    /// </summary>
    private static List<DiagramGroup> PlaceGroups(ReportPlate plate, List<Shape> obstacles, List<int> failed,
        List<DiagramLabel> labels, Fit fit, Canvas canvas, XFont font)
    {
        // Obstacles are the parts, in plate order, followed by the cutoffs.
        var boxes = plate.Parts.Select(part => fit.Rect(part.Geometry.Bounds)).ToList();
        var groups = new List<DiagramGroup>();
        // A small screen-space gap also accommodates tightly packed parts with unset spacing.
        var gap = System.Math.Max(4, plate.PartSpacing * fit.Scale * 2);
        var sheet = fit.Rect(plate.Bounds);
        // Outlines stay under half the part spacing from their members, so the outlines of two
        // neighboring clusters (different IDs one spacing apart) never meet or overlap.
        var margin = plate.PartSpacing > 0 ? System.Math.Min(1, plate.PartSpacing * fit.Scale * 0.45) : 1;
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
            boundary.Inflate(margin, margin);
            var memberSet = members.ToHashSet();
            // A rectangular group must not imply that unrelated or ungrouped parts belong to it.
            if (!canvas.Holds(boundary)
                || obstacles.Where((_, index) => !memberSet.Contains(index)).Any(shape => shape.Crosses(ModelBox(fit, boundary)))
                || labels.Any(label => label.Box.IntersectsWith(boundary))
                || groups.Any(group => group.Boundary.IntersectsWith(boundary) || group.Label.Box.IntersectsWith(boundary)))
                continue;

            var size = ReportText.Size(id, font);
            var labelWidth = size.Width + 2 * LabelPadding;
            var labelHeight = size.Height + 2 * LabelPadding;
            var centerX = boundary.X + (boundary.Width - labelWidth) / 2;
            var centerY = boundary.Y + (boundary.Height - labelHeight) / 2;
            var target = new XPoint(boundary.X + boundary.Width / 2, boundary.Y + boundary.Height / 2);
            var inside = Grid(boundary, labelWidth, labelHeight).ToList();
            // The ID goes wherever it fits best: clear space inside the outline (between the parts
            // or in a hole) or beside it.
            var candidates = inside.Concat(
            [
                new XRect(centerX, boundary.Y - labelHeight - Clearance, labelWidth, labelHeight),
                new XRect(centerX, boundary.Bottom + Clearance, labelWidth, labelHeight),
                new XRect(boundary.Right + Clearance, centerY, labelWidth, labelHeight),
                new XRect(boundary.X - labelWidth - Clearance, centerY, labelWidth, labelHeight),
            ]);
            bool Blocked(XRect padded) => !canvas.Holds(padded) || OnSheetEdge(sheet, padded)
                || labels.Any(label => label.Box.IntersectsWith(padded))
                || groups.Any(group => group.Boundary.IntersectsWith(padded) || group.Label.Box.IntersectsWith(padded));
            var others = obstacles.Where((_, index) => !memberSet.Contains(index)).ToList();
            // With no clear space anywhere, the ID may cover the group's own parts (the parts
            // list shows each part up close), but never another part or a cutoff.
            var spot = BestSpot(candidates, obstacles, others, fit, Blocked, target)
                ?? BestSpot(inside, others, others, fit, Blocked, target, required: 0, preferRoom: false);
            if (spot is { } box)
                groups.Add(new DiagramGroup(members, boundary, new DiagramLabel(seed, id, box)));
        }
        return groups;
    }

    /// <summary>Whether a label would sit across the sheet outline and hide part of it.</summary>
    private static bool OnSheetEdge(XRect sheet, XRect padded) =>
        sheet.IntersectsWith(padded) && !sheet.Contains(padded);

    /// <summary>Label-sized boxes tiling an area on a fine grid, centered when the area is too small.</summary>
    private static IEnumerable<XRect> Grid(XRect area, double width, double height)
    {
        yield return new XRect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        var spanX = area.Width - width;
        var spanY = area.Height - height;
        if (spanX < 0 || spanY < 0)
            yield break;
        var step = System.Math.Max(1, System.Math.Max(spanX, spanY) / 60);
        for (var y = 0.0; y <= spanY + 1e-9; y += step)
            for (var x = 0.0; x <= spanX + 1e-9; x += step)
                yield return new XRect(area.X + x, area.Y + y, width, height);
    }

    /// <summary>
    /// The candidate with the most room to the nearest contour (capped, so open space ties) and,
    /// among ties, the one nearest <paramref name="target"/>. A candidate must keep at least
    /// <paramref name="required"/> canvas points from every obstacle edge and may not sit inside
    /// the material of any shape in <paramref name="foreign"/>, which would label the wrong part.
    /// Without <paramref name="preferRoom"/>, only the distance to the target ranks candidates.
    /// </summary>
    private static XRect? BestSpot(IEnumerable<XRect> candidates, IReadOnlyList<Shape> obstacles,
        IReadOnlyList<Shape> foreign, Fit fit, Func<XRect, bool> blocked, XPoint target, double required = Clearance,
        bool preferRoom = true)
    {
        XRect? best = null;
        var bestRoom = double.NegativeInfinity;
        var bestDistance = double.PositiveInfinity;
        foreach (var box in candidates)
        {
            var padded = box;
            padded.Inflate(Clearance, Clearance);
            if (blocked(padded))
                continue;
            var cap = 2 * box.Height;
            var reach = box;
            reach.Inflate(cap, cap);
            var near = ModelBox(fit, reach);
            var model = ModelBox(fit, box);
            var room = cap;
            foreach (var shape in obstacles)
            {
                if (room < required || !shape.Overlaps(near))
                    continue;
                room = System.Math.Min(room, shape.Distance(model) * fit.Scale);
            }
            if (room < required)
                continue;
            var center = fit.Model(new XPoint(box.X + box.Width / 2, box.Y + box.Height / 2));
            if (foreign.Any(shape => shape.InMaterial(new Vector(center.X, center.Y))))
                continue;
            var distance = System.Math.Pow(box.X + box.Width / 2 - target.X, 2)
                + System.Math.Pow(box.Y + box.Height / 2 - target.Y, 2);
            if (!preferRoom)
                room = 0;
            if (room > bestRoom + 1e-6 || (room > bestRoom - 1e-6 && distance < bestDistance))
            {
                best = box;
                bestRoom = room;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// Centers each ID on its part's pole of inaccessibility (<see cref="PolyLabel"/>, as PlateView
    /// does), so it sits deepest inside the material and clear of holes. A label box that would
    /// touch an edge, hole, cutoff or another label falls back to the roomiest clear spot within the
    /// part's own extents (such as an empty hole); failing that, the part may share a group label or
    /// remain unlabeled.
    /// </summary>
    private static List<DiagramLabel> PlaceLabels(ReportPlate plate, List<Shape> shapes, List<Shape> obstacles,
        List<int> targets, Fit fit, Canvas canvas, XFont font, out List<int> failed)
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
            var padded = box;
            padded.Inflate(Clearance, Clearance);
            var test = ModelBox(fit, padded);
            if (canvas.Holds(box) && !placed.Any(label => label.Box.IntersectsWith(box))
                && !obstacles.Any(shape => shape.Crosses(test)))
                placed.Add(new DiagramLabel(index, id, box));
            else
                failed.Add(index);
        }

        // A part too narrow at its pole (a ring around a large hole) may still have clear room
        // elsewhere within its own extents, such as the hole itself.
        foreach (var index in failed.ToList())
        {
            var id = plate.Parts[index].ReportId;
            var size = ReportText.Size(id, font);
            var area = fit.Rect(shapes[index].Bounds);
            var others = obstacles.Where((_, other) => other != index).ToList();
            var spot = BestSpot(Grid(area, size.Width + 2 * LabelPadding, size.Height + 2 * LabelPadding)
                    .Where(area.Contains), obstacles, others, fit,
                padded => !canvas.Holds(padded) || OnSheetEdge(fit.Rect(plate.Bounds), padded) || placed.Any(label => label.Box.IntersectsWith(padded)),
                new XPoint(area.X + area.Width / 2, area.Y + area.Height / 2));
            if (spot is not { } found)
                continue;
            placed.Add(new DiagramLabel(index, id, found));
            failed.Remove(index);
        }
        return placed;
    }

    /// <summary>The model-space rectangle under a canvas rectangle.</summary>
    private static Box ModelBox(Fit fit, XRect rect)
    {
        var topLeft = fit.Model(new XPoint(rect.Left, rect.Top));
        var bottomRight = fit.Model(new XPoint(rect.Right, rect.Bottom));
        return new Box(topLeft.X, bottomRight.Y, bottomRight.X - topLeft.X, topLeft.Y - bottomRight.Y);
    }

    private static ReportBounds Union(ReportBounds sheet, IEnumerable<ReportBounds> boxes)
    {
        var left = sheet.Left;
        var bottom = sheet.Bottom;
        var right = sheet.Right;
        var top = sheet.Top;
        foreach (var box in boxes)
        {
            left = System.Math.Min(left, box.Left);
            bottom = System.Math.Min(bottom, box.Bottom);
            right = System.Math.Max(right, box.Right);
            top = System.Math.Max(top, box.Top);
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

        public bool Overlaps(Box box) => Box.Intersects(box);

        /// <summary>Distance from the rectangle to the nearest contour edge; zero when one touches it.</summary>
        public double Distance(Box box)
        {
            var best = double.PositiveInfinity;
            foreach (var ring in Rings)
            {
                var vertices = ring.Vertices;
                for (var i = 0; i < vertices.Count; i++)
                {
                    best = System.Math.Min(best, SegmentDistance(vertices[i], vertices[(i + 1) % vertices.Count], box));
                    if (best <= 0)
                        return 0;
                }
            }
            return best;
        }

        /// <summary>Whether the point lies in material: inside an odd number of contours.</summary>
        public bool InMaterial(Vector point)
        {
            if (!Box.Contains(point))
                return false;
            var inside = false;
            foreach (var ring in Rings)
            {
                var vertices = ring.Vertices;
                for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                {
                    if ((vertices[i].Y > point.Y) != (vertices[j].Y > point.Y)
                        && point.X < (vertices[j].X - vertices[i].X) * (point.Y - vertices[i].Y)
                            / (vertices[j].Y - vertices[i].Y) + vertices[i].X)
                        inside = !inside;
                }
            }
            return inside;
        }

        private static double SegmentDistance(Vector a, Vector b, Box box)
        {
            if (box.Contains(a) || box.Contains(b))
                return 0;
            Vector[] corners = [new(box.Left, box.Bottom), new(box.Right, box.Bottom),
                new(box.Right, box.Top), new(box.Left, box.Top)];
            for (var i = 0; i < 4; i++)
            {
                if (SegmentsCross(a, b, corners[i], corners[(i + 1) % 4]))
                    return 0;
            }
            var best = System.Math.Min(PointToBox(a, box), PointToBox(b, box));
            foreach (var corner in corners)
                best = System.Math.Min(best, PointToSegment(corner, a, b));
            return best;
        }

        private static double PointToBox(Vector point, Box box)
        {
            var dx = System.Math.Max(System.Math.Max(box.Left - point.X, 0), point.X - box.Right);
            var dy = System.Math.Max(System.Math.Max(box.Bottom - point.Y, 0), point.Y - box.Top);
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PointToSegment(Vector point, Vector a, Vector b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared <= 0 ? 0
                : System.Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
            var x = a.X + t * dx - point.X;
            var y = a.Y + t * dy - point.Y;
            return System.Math.Sqrt(x * x + y * y);
        }

        private static bool SegmentsCross(Vector a, Vector b, Vector c, Vector d)
        {
            static double Cross(Vector o, Vector p, Vector q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
            var d1 = Cross(c, d, a);
            var d2 = Cross(c, d, b);
            var d3 = Cross(a, b, c);
            var d4 = Cross(a, b, d);
            return ((d1 > 0) != (d2 > 0) || d1 == 0 || d2 == 0) && ((d3 > 0) != (d4 > 0) || d3 == 0 || d4 == 0)
                && System.Math.Min(a.X, b.X) <= System.Math.Max(c.X, d.X) && System.Math.Min(c.X, d.X) <= System.Math.Max(a.X, b.X)
                && System.Math.Min(a.Y, b.Y) <= System.Math.Max(c.Y, d.Y) && System.Math.Min(c.Y, d.Y) <= System.Math.Max(a.Y, b.Y);
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
