using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Geometry;

public class ClipperBridgeTests
{
    [Fact]
    public void Offset_NotchNarrowerThanTwiceSpacing_ClosesNotch()
    {
        // 10x10 square with a 0.3-wide, 3-deep slot down from the top edge.
        var profile = Profile(
            Poly(
                (0, 0),
                (10, 0),
                (10, 10),
                (5.15, 10),
                (5.15, 7),
                (4.85, 7),
                (4.85, 10),
                (0, 10)
            )
        );

        var result = ClipperBridge.Offset(profile, 0.25, 0.001);

        var outer = Assert.Single(result.Outers);
        Assert.Empty(result.Holes);

        // The slot fills in. Only a shallow dent is left where the round joins of the
        // two mouth corners meet: 10 + sqrt(0.25^2 - 0.15^2) = 10.2.
        Assert.DoesNotContain(outer.Vertices, v => v.X > 4.85 && v.X < 5.15 && v.Y < 10.199);

        var fullSquare = 10 * 10 + 4 * 10 * 0.25 + System.Math.PI * 0.25 * 0.25;
        Assert.InRange(outer.Area(), fullSquare - 0.01, fullSquare);
    }

    [Fact]
    public void Offset_HoleSmallerThanTwiceSpacing_DropsHole()
    {
        var profile = Profile(Poly((0, 0), (10, 0), (10, 10), (0, 10)), Circle(5, 5, 0.2));

        var result = ClipperBridge.Offset(profile, 0.25, 0.001);

        Assert.Single(result.Outers);
        Assert.Empty(result.Holes);
    }

    [Fact]
    public void Offset_HoleWithThinNeck_SplitsIntoTwoHoles()
    {
        // Two 2x2 pockets joined by a 2-long, 0.3-wide channel.
        var hole = Poly(
            (2, 4),
            (4, 4),
            (4, 4.85),
            (6, 4.85),
            (6, 4),
            (8, 4),
            (8, 6),
            (6, 6),
            (6, 5.15),
            (4, 5.15),
            (4, 6),
            (2, 6)
        );
        var profile = Profile(Poly((0, 0), (10, 0), (10, 10), (0, 10)), hole);

        var result = ClipperBridge.Offset(profile, 0.25, 0.001);

        Assert.Single(result.Outers);
        Assert.Equal(2, result.Holes.Count);

        // Each pocket shrinks to 1.5x1.5, plus a small lobe toward the channel mouth
        // where the round joins of the channel corners meet.
        Assert.All(result.Holes, h => Assert.InRange(h.Area(), 2.25, 2.26));
    }

    [Fact]
    public void Offset_WindingOfInputDoesNotMatter()
    {
        var ccw = Poly((0, 0), (10, 0), (10, 10), (0, 10));
        var cw = Poly((0, 0), (0, 10), (10, 10), (10, 0));
        var hole = Circle(5, 5, 2);

        var a = ClipperBridge.Offset(Profile(ccw, hole), 0.25, 0.001);
        var b = ClipperBridge.Offset(Profile(cw, hole), 0.25, 0.001);

        Assert.Equal(a.Outers.Count, b.Outers.Count);
        Assert.Equal(a.Holes.Count, b.Holes.Count);
        Assert.Equal(a.Outers[0].Area(), b.Outers[0].Area(), 6);
        Assert.Equal(a.Holes[0].Area(), b.Holes[0].Area(), 6);
    }

    [Fact]
    public void Offset_Circumscribe_NeverUnderestimatesDistance()
    {
        const double spacing = 0.25;
        var profile = Profile(Circle(0, 0, 5), Circle(0, 0, 3));

        var result = ClipperBridge.Offset(profile, spacing, 0.05, circumscribe: true);

        var outer = Assert.Single(result.Outers);
        var hole = Assert.Single(result.Holes);

        for (var i = 0; i < 360; i++)
        {
            var a = i * System.Math.PI / 180;
            var onPerimeter = new Vector(5 * System.Math.Cos(a), 5 * System.Math.Sin(a));
            var onCutout = new Vector(3 * System.Math.Cos(a), 3 * System.Math.Sin(a));

            Assert.True(outer.ContainsPoint(onPerimeter));
            Assert.True(
                outer.ClosestPointTo(onPerimeter).DistanceTo(onPerimeter) >= spacing,
                $"Perimeter sample at {i} deg is closer than the spacing."
            );

            Assert.False(hole.ContainsPoint(onCutout));
            Assert.True(
                hole.ClosestPointTo(onCutout).DistanceTo(onCutout) >= spacing,
                $"Cutout sample at {i} deg is closer than the spacing."
            );
        }
    }

    [Fact]
    public void Offset_PepNotchedPart_HasNoSpikes()
    {
        // 1.nest (PEP P260417-06): rounded-square hole, perimeter with 0.0598-wide
        // notches and 0.015 fillets, all narrower than twice the 0.25 spacing.
        var program = ReadProgram(PepNotchedPart);
        var entities = ConvertProgram.ToGeometry(program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .ToList();

        var result = ClipperBridge.Offset(new ShapeProfile(entities), 0.25, 0.001);

        var outer = Assert.Single(result.Outers);
        Assert.Single(result.Holes);

        var verts = outer.Vertices;
        var n = verts.Count - 1;

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                    continue;

                Assert.False(
                    SegmentsCross(verts[i], verts[i + 1], verts[j], verts[j + 1]),
                    $"Edges {i} and {j} cross."
                );
            }
        }

        for (var i = 0; i < n; i++)
        {
            var prev = verts[(i + n - 1) % n];
            var cur = verts[i];
            var next = verts[(i + 1) % n];

            var inDir = Unit(cur - prev);
            var outDir = Unit(next - cur);
            var dot = inDir.X * outDir.X + inDir.Y * outDir.Y;

            Assert.True(dot > -0.99, $"Spike at vertex {i} ({cur.X:F4}, {cur.Y:F4}).");
        }
    }

    [Theory]
    [InlineData(true, OffsetSide.Left, 12 * 12)]
    [InlineData(true, OffsetSide.Right, 8 * 8)]
    [InlineData(false, OffsetSide.Left, 8 * 8)]
    [InlineData(false, OffsetSide.Right, 12 * 12)]
    public void PolygonOffsetEntity_MitersToSideAndKeepsWinding(
        bool ccw,
        OffsetSide side,
        double expectedArea
    )
    {
        var square = new Polygon();
        square.Vertices.AddRange(new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10) });

        if (!ccw)
            square.Vertices.Reverse();

        square.Close();

        var result = (Polygon)square.OffsetEntity(1, side);

        Assert.Equal(expectedArea, result.Area(), 6);
        Assert.Equal(square.RotationDirection(), result.RotationDirection());
    }

    private static bool SegmentsCross(Vector a, Vector b, Vector c, Vector d)
    {
        static double Cross(Vector o, Vector p, Vector q) =>
            (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);

        return Cross(c, d, a) * Cross(c, d, b) < 0 && Cross(a, b, c) * Cross(a, b, d) < 0;
    }

    private static Vector Unit(Vector v)
    {
        var len = System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return new Vector(v.X / len, v.Y / len);
    }

    private static Shape Poly(params (double X, double Y)[] pts)
    {
        var shape = new Shape();

        for (var i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            shape.Entities.Add(new Line(a.X, a.Y, b.X, b.Y));
        }

        return shape;
    }

    private static Shape Circle(double x, double y, double r)
    {
        var shape = new Shape();
        shape.Entities.Add(new Circle(x, y, r));
        return shape;
    }

    private static ShapeProfile Profile(params Shape[] shapes) =>
        new(shapes.SelectMany(s => s.Entities).ToList());

    private static Program ReadProgram(string gcode)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gcode));
        return new ProgramReader(stream).Read();
    }

    private const string PepNotchedPart = """
        G91
        G00X-8.003411Y12.354904
        G01X0Y5.03125
        G03X-2.3125Y2.3125I-2.3125J0
        G01X-10.0625Y0
        G03X-2.3125Y-2.3125I0J-2.3125
        G01X0Y-10.0625
        G03X2.3125Y-2.3125I2.3125J0
        G01X10.0625Y0
        G03X2.3125Y2.3125I0J2.3125
        G01X0Y5.03125
        G00X10.200865Y-12.347161
        G01X-2.182454Y0
        G03X-0.015Y-0.015I0J-0.015
        G01X0Y-1.457646
        G02X-0.015Y-0.015I-0.015J0
        G01X-30.664322Y0
        G02X-0.015Y0.015I0J0.015
        G01X0Y1.1725
        G01X0.072967Y0.149903
        G02X0.013487Y0.008435I0.013487J-0.006565
        G01X0.620707Y0
        G03X0.015Y0.015I0J0.015
        G01X0Y0.396469
        G03X-0.015Y0.015I-0.015J0
        G01X-0.4225Y0
        G01X0Y0.095339
        G01X-2.419615Y0
        G02X-0.0625Y0.0625I0J0.0625
        G01X0Y23.809322
        G02X0.0625Y0.0625I0.0625J0
        G01X2.405015Y0
        G03X0.015Y0.015I0J0.015
        G01X0Y1.837647
        G02X0.015Y0.015I0.015J0
        G01X4.005139Y0
        G02X0.015Y-0.015I0J-0.015
        G01X0Y-0.3573
        G03X0.0598Y0I0.03J0
        G01X0Y0.974934
        G02X0.0625Y0.0625I0.0625J0
        G01X25.420246Y0
        G02X0.0625Y-0.0625I0J-0.0625
        G01X0Y-0.974934
        G03X0.0598Y0I0.03J0
        G01X0Y0.3573
        G02X0.015Y0.015I0.015J0
        G01X0.679276Y0
        G02X0.015Y-0.015I0J-0.015
        G01X0Y-1.457647
        G03X0.015Y-0.015I0.015J0
        G01X1.145147Y0
        G02X0.015Y-0.015I0J-0.015
        G01X0Y-0.709988
        G03X0.015Y-0.015I0.015J0
        G01X0.944807Y0
        G02X0.0625Y-0.0625I0J-0.0625
        G01X0Y-23.891834
        """;
}
