using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A cut line through a vertex of the clearance outline must still follow the part, not
/// fall back to its bounding box. Automatic strip lines sit on a regular lattice, so they
/// land on such vertices far more often than a hand-placed line, and the shift search
/// prefers them because the bounding-box fallback measures as less cutting.
/// </summary>
public class CutOffVertexParityTests
{
    // A 12 x 10 block with a V notch cut down from the top edge to (6, 4).
    private static readonly Vector[] Notch =
    [
        new(0, 0), new(12, 0), new(12, 10), new(9, 10), new(6, 4), new(3, 10), new(0, 10),
    ];

    public static IEnumerable<object[]> NotchCases()
    {
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
            foreach (var axis in new[] { CutOffAxis.Vertical, CutOffAxis.Horizontal })
                foreach (var materialize in new[] { false, true })
                    yield return [quadrant, axis, materialize];
    }

    [Theory]
    [MemberData(nameof(NotchCases))]
    public void LineThroughOutlineVertex_CutsTheOpenNotch(int quadrant, CutOffAxis axis, bool materialize)
    {
        var (plate, part, map) = NotchPlate(quadrant, axis);
        var settings = new CutOffSettings();
        var cut = map(new Vector(6, 0));
        var cutoff = new CutOff(cut, axis);
        var cache = Plate.BuildPerimeterCache(plate);
        var vertex = OutlineVertexOnLine(cache[part], cutoff, settings.PartClearance);
        Assert.True(vertex, "Fixture must put a clearance-outline vertex exactly on the cut line.");

        var segments = Cut(plate, cutoff, settings, cache, materialize);

        AssertClear(segments, map, settings.PartClearance);
        // The notch is open from (6, 4) up past the top edge: a segment must start inside
        // it (above the notch tip plus clearance) rather than at the part's top bound.
        var along = segments.Select(s => (Lo: Unmap(map, s.Start, axis), Hi: Unmap(map, s.End, axis)))
            .Select(s => (Lo: System.Math.Min(s.Lo, s.Hi), Hi: System.Math.Max(s.Lo, s.Hi)))
            .ToList();
        Assert.Contains(along, s => s.Lo < 4.1 && s.Hi > 10.1);
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical)]
    [InlineData(CutOffAxis.Horizontal)]
    public void MeasureSegments_MatchesRegeneratedLine(CutOffAxis axis)
    {
        var (plate, _, map) = NotchPlate(1, axis);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(map(new Vector(6, 0)), axis);
        var cache = Plate.BuildPerimeterCache(plate);
        cutoff.Regenerate(plate, settings, cache);
        var codes = cutoff.Drawing.Program.Codes;
        var length = 0.0;
        for (var i = 0; i < codes.Count; i += 2)
            length += ((Motion)codes[i]).EndPoint.DistanceTo(((Motion)codes[i + 1]).EndPoint);

        var measured = cutoff.MeasureSegments(plate, settings, cache);

        Assert.Equal(codes.Count / 2, measured.SegmentCount);
        Assert.Equal(length, measured.Length, 9);
        // The bounding-box fallback leaves 100 - 10.04 = 89.96; the notch adds about six more.
        Assert.True(length > 95, $"Line through the notch vertex must cut the notch; cut {length:R}.");
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical)]
    [InlineData(CutOffAxis.Horizontal)]
    public void LineThroughSidewaysNotchTip_KeepsMaterialOnBothSides(CutOffAxis axis)
    {
        // Notch from the right edge, tip pointing at the cut: both tip edges lie beyond the line,
        // so the line meets the outline at one tangent point and material remains on both sides.
        var points = new Vector[]
        {
            new(0, 0), new(12, 0), new(12, 3), new(6, 5), new(12, 7), new(12, 10), new(0, 10),
        };
        var plate = new Plate(100, 100);
        Vector Orient(Vector p) => axis == CutOffAxis.Vertical ? p : new Vector(p.Y, p.X);
        var part = new Part(new Drawing("sideways-notch", Polygon(points.Select(Orient).ToArray())),
            Orient(new Vector(20, 20)));
        plate.Parts.Add(part);
        var settings = new CutOffSettings();
        var cache = Plate.BuildPerimeterCache(plate);
        var tip = TipOnOutline(cache[part], axis, 20 + 6, 20 + 5);
        var cutoff = new CutOff(axis == CutOffAxis.Vertical ? new Vector(tip, 0) : new Vector(0, tip), axis);

        var segments = Cut(plate, cutoff, settings, cache, materialize: true);

        // Whole part slice stays excluded: no segment may start or end within the part's span.
        foreach (var (start, end) in segments)
        {
            var lo = System.Math.Min(start, end);
            var hi = System.Math.Max(start, end);
            Assert.True(hi <= 20 - settings.PartClearance + 1e-9 || lo >= 30 + settings.PartClearance - 1e-9,
                $"Cut {lo:R}..{hi:R} enters the part slice 20..30.");
        }
        Assert.Equal(2, segments.Count);
    }

    private static (Plate Plate, Part Part, System.Func<Vector, Vector> Map) NotchPlate(int quadrant, CutOffAxis axis)
    {
        var plate = new Plate(100, 100) { Quadrant = quadrant };
        var negativeX = quadrant is 2 or 3;
        var negativeY = quadrant is 3 or 4;
        // Part-local frame: notch block at (20, 20); reflect for the quadrant, transpose for horizontal.
        Vector Map(Vector p)
        {
            var oriented = axis == CutOffAxis.Vertical ? new Vector(p.X + 20, p.Y + 20)
                : new Vector(p.Y + 20, p.X + 20);
            return new Vector(negativeX ? -oriented.X : oriented.X, negativeY ? -oriented.Y : oriented.Y);
        }
        var points = Notch.Select(p => Map(p)).ToArray();
        var origin = new Vector(points.Min(p => p.X), points.Min(p => p.Y));
        var local = points.Select(p => p - origin).ToArray();
        var part = new Part(new Drawing($"notch-q{quadrant}-{axis}", Polygon(local)), origin);
        plate.Parts.Add(part);
        // The cut coordinate for a local X maps to Map(x, 0).X (vertical) or .Y (horizontal).
        return (plate, part, p => Map(p));
    }

    private static double Unmap(System.Func<Vector, Vector> map, double along, CutOffAxis axis)
    {
        // Along-line coordinate back to the notch frame's Y (always 20 + local Y, possibly negated).
        var origin = axis == CutOffAxis.Vertical ? map(Vector.Zero).Y : map(Vector.Zero).X;
        var unit = axis == CutOffAxis.Vertical ? map(new Vector(0, 1)).Y - origin : map(new Vector(0, 1)).X - origin;
        return (along - origin) / unit;
    }

    private static List<(double Start, double End)> Cut(Plate plate, CutOff cutoff, CutOffSettings settings,
        Dictionary<Part, Entity> cache, bool materialize)
    {
        Program program;
        var location = Vector.Zero;
        if (materialize)
        {
            plate.CutOffs.Add(cutoff);
            plate.RegenerateCutOffs(settings);
            program = TestHelpers.CutOffProgram(plate, cutoff);
        }
        else
        {
            cutoff.Regenerate(plate, settings, cache);
            program = cutoff.Drawing.Program;
        }
        var vertical = cutoff.Axis == CutOffAxis.Vertical;
        var result = new List<(double, double)>();
        for (var i = 0; i < program.Codes.Count; i += 2)
        {
            var start = Assert.IsType<RapidMove>(program.Codes[i]).EndPoint + location;
            var end = Assert.IsType<LinearMove>(program.Codes[i + 1]).EndPoint + location;
            Assert.Equal(vertical ? cutoff.Position.X : cutoff.Position.Y, vertical ? start.X : start.Y);
            result.Add(vertical ? (start.Y, end.Y) : (start.X, end.X));
        }
        return result;
    }

    private static void AssertClear(List<(double Start, double End)> segments, System.Func<Vector, Vector> map,
        double clearance)
    {
        // Independent oracle: exact point-to-segment distance against the raw notch edges.
        var edges = Notch.Select((p, i) => (A: map(p), B: map(Notch[(i + 1) % Notch.Length]))).ToArray();
        var line = map(new Vector(6, 0));
        var horizontal = System.Math.Abs(map(new Vector(1, 0)).Y - map(Vector.Zero).Y) > 0.5;
        foreach (var (start, end) in segments)
        {
            var a = horizontal ? new Vector(start, line.Y) : new Vector(line.X, start);
            var b = horizontal ? new Vector(end, line.Y) : new Vector(line.X, end);
            foreach (var edge in edges)
            {
                var distance = SegmentDistance(a, b, edge.A, edge.B);
                Assert.True(distance >= clearance - 1e-9,
                    $"Cut {a} -> {b} is {distance:R} from edge {edge.A} -> {edge.B}; requires {clearance:R}.");
            }
        }
    }

    private static double SegmentDistance(Vector p1, Vector p2, Vector q1, Vector q2)
    {
        if (SegmentsIntersect(p1, p2, q1, q2))
            return 0;
        return new[]
        {
            PointDistance(p1, q1, q2), PointDistance(p2, q1, q2),
            PointDistance(q1, p1, p2), PointDistance(q2, p1, p2),
        }.Min();
    }

    private static double PointDistance(Vector p, Vector a, Vector b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = System.Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        var x = a.X + t * dx - p.X;
        var y = a.Y + t * dy - p.Y;
        return System.Math.Sqrt(x * x + y * y);
    }

    private static bool SegmentsIntersect(Vector p1, Vector p2, Vector q1, Vector q2)
    {
        static double Cross(Vector o, Vector a, Vector b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var d1 = Cross(q1, q2, p1);
        var d2 = Cross(q1, q2, p2);
        var d3 = Cross(p1, p2, q1);
        var d4 = Cross(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static bool OutlineVertexOnLine(Entity perimeter, CutOff cutoff, double clearance)
    {
        var offset = ClipperBridge.OffsetPerimeter((Shape)perimeter, clearance, 0.001, circumscribe: true);
        var line = cutoff.Axis == CutOffAxis.Vertical ? cutoff.Position.X : cutoff.Position.Y;
        return offset.Outers.Concat(offset.Holes).SelectMany(p => p.Vertices)
            .Any(v => (cutoff.Axis == CutOffAxis.Vertical ? v.X : v.Y) == line);
    }

    private static double TipOnOutline(Entity perimeter, CutOffAxis axis, double tipAcross, double tipAlong)
    {
        var offset = ClipperBridge.OffsetPerimeter((Shape)perimeter, new CutOffSettings().PartClearance, 0.001,
            circumscribe: true);
        var tip = offset.Outers.SelectMany(p => p.Vertices)
            .OrderBy(v => axis == CutOffAxis.Vertical
                ? System.Math.Abs(v.Y - tipAlong) + System.Math.Abs(v.X - tipAcross)
                : System.Math.Abs(v.X - tipAlong) + System.Math.Abs(v.Y - tipAcross))
            .First();
        return axis == CutOffAxis.Vertical ? tip.X : tip.Y;
    }

    private static Program Polygon(Vector[] points)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(points[0]));
        foreach (var point in points.Skip(1).Append(points[0]))
            program.Codes.Add(new LinearMove(point));
        Assert.True(Assert.Single(ShapeBuilder.GetShapes(ConvertProgram.ToGeometry(program)
            .Where(entity => SpecialLayers.IsMaterial(entity.Layer)))).IsClosed());
        return program;
    }
}
