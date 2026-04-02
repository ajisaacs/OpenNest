using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.Engine;

public class PartClassifierTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Drawing MakeRectDrawing(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PureRectangle_ReturnsRectangle()
    {
        var drawing = MakeRectDrawing(100, 50);
        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Rectangle, result.Type);
        Assert.True(result.Rectangularity >= 0.99, $"Expected rectangularity>=0.99, got {result.Rectangularity:F4}");
        Assert.True(result.PerimeterRatio >= 0.99, $"Expected perimeterRatio>=0.99, got {result.PerimeterRatio:F4}");
    }

    [Fact]
    public void Classify_RoundedRectangle_ReturnsRectangle()
    {
        // Use the built-in shape builder so arc geometry is constructed correctly.
        var shape = new RoundedRectangleShape { Length = 100, Width = 50, Radius = 5 };
        var drawing = shape.GetDrawing();

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Rectangle, result.Type);
    }

    [Fact]
    public void Classify_RectWithSmallNotches_ReturnsRectangle()
    {
        // 100x50 rectangle with a 5x2 notch cut into the bottom edge near the centre.
        // The notch is small relative to the overall perimeter so both metrics still pass.
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        // Bottom edge left section -> notch -> bottom edge right section
        pgm.Codes.Add(new LinearMove(new Vector(45, 0)));   // along bottom to notch start
        pgm.Codes.Add(new LinearMove(new Vector(45, 2)));   // up into notch
        pgm.Codes.Add(new LinearMove(new Vector(50, 2)));   // across notch (5 wide)
        pgm.Codes.Add(new LinearMove(new Vector(50, 0)));   // back down
        pgm.Codes.Add(new LinearMove(new Vector(100, 0)));  // remainder of bottom edge
        pgm.Codes.Add(new LinearMove(new Vector(100, 50))); // right edge
        pgm.Codes.Add(new LinearMove(new Vector(0, 50)));   // top edge
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));    // left edge back to start
        var drawing = new Drawing("rect-notch", pgm);

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Rectangle, result.Type);
    }

    [Fact]
    public void Classify_Circle_ReturnsCircle()
    {
        var shape = new CircleShape { Diameter = 50 };
        var drawing = shape.GetDrawing();

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Circle, result.Type);
        Assert.True(result.Circularity >= PartClassifier.CircularityThreshold,
            $"Expected circularity>={PartClassifier.CircularityThreshold}, got {result.Circularity:F4}");
    }

    [Fact]
    public void Classify_LShape_ReturnsIrregular()
    {
        // 100x80 L-shape: full rect minus a 50x40 block from the top-right corner.
        // Outline (CCW): (0,0) → (100,0) → (100,40) → (50,40) → (50,80) → (0,80) → (0,0)
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(100, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(100, 40)));
        pgm.Codes.Add(new LinearMove(new Vector(50, 40)));
        pgm.Codes.Add(new LinearMove(new Vector(50, 80)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 80)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("lshape", pgm);

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Irregular, result.Type);
    }

    [Fact]
    public void Classify_Triangle_ReturnsIrregular()
    {
        // Right triangle: base 100, height 80.
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(100, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 80)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("triangle", pgm);

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Irregular, result.Type);
    }

    [Fact]
    public void Classify_SerratedEdge_CaughtByPerimeterRatio()
    {
        // 100x30 rectangle with 20 teeth of depth 6 along the bottom edge.
        // Each tooth is 5 wide, 6 deep → adds 12 units of extra perimeter per tooth.
        // Total extra = 20 * 12 = 240 mm extra over a plain 100mm bottom edge.
        // MBR perimeter ≈ 2*(100+30) = 260. Actual perimeter ≈ 260 - 100 + 100 + 240 = 500.
        // PerimeterRatio ≈ 260/500 = 0.52 — well below the 0.85 threshold.
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));

        // Serrated bottom edge: 20 teeth, each 5 wide and 6 deep.
        var toothCount = 20;
        var toothWidth = 5.0;
        var toothDepth = 6.0;
        var w = toothCount * toothWidth; // = 100
        var h = 30.0;

        for (var i = 0; i < toothCount; i++)
        {
            var x0 = i * toothWidth;
            pgm.Codes.Add(new LinearMove(new Vector(x0 + toothWidth / 2, -toothDepth)));
            pgm.Codes.Add(new LinearMove(new Vector(x0 + toothWidth, 0)));
        }

        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("serrated", pgm);

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Irregular, result.Type);
        Assert.True(result.PerimeterRatio < PartClassifier.PerimeterRatioThreshold,
            $"Expected perimeterRatio<{PartClassifier.PerimeterRatioThreshold}, got {result.PerimeterRatio:F4}");
    }

    [Fact]
    public void Classify_PrimaryAngle_MatchesMbrAlignment()
    {
        // A rectangle rotated 30° around the origin — no edge is axis-aligned, so
        // RotatingCalipers must find a non-zero MBR angle.
        var tiltDeg = 30.0;
        var tiltRad = Angle.ToRadians(tiltDeg);
        var w = 80.0;
        var h = 30.0;
        var cos = System.Math.Cos(tiltRad);
        var sin = System.Math.Sin(tiltRad);

        // Rotate each corner of an 80×30 rectangle by 30°.
        Vector Rot(double x, double y) => new Vector(x * cos - y * sin, x * sin + y * cos);

        var p0 = Rot(0, 0);
        var p1 = Rot(w, 0);
        var p2 = Rot(w, h);
        var p3 = Rot(0, h);

        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(p0));
        pgm.Codes.Add(new LinearMove(p1));
        pgm.Codes.Add(new LinearMove(p2));
        pgm.Codes.Add(new LinearMove(p3));
        pgm.Codes.Add(new LinearMove(p0));
        var drawing = new Drawing("tilted-rect", pgm);

        var result = PartClassifier.Classify(drawing);

        // The MBR must be tilted — primary angle should be non-zero.
        Assert.True(System.Math.Abs(result.PrimaryAngle) > 0.01,
            $"Expected non-zero primary angle for 30°-tilted rect, got {result.PrimaryAngle:F4} rad");
    }

    [Fact]
    public void Classify_EmptyDrawing_ReturnsIrregularDefault()
    {
        var pgm = new OpenNest.CNC.Program();
        var drawing = new Drawing("empty", pgm);

        var result = PartClassifier.Classify(drawing);

        Assert.Equal(PartType.Irregular, result.Type);
        Assert.Equal(0.0, result.Rectangularity);
        Assert.Equal(0.0, result.Circularity);
    }
}
