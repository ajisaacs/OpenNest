using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class PolygonHelperTests
{
    [Fact]
    public void ExtractPerimeterPolygon_ReturnsPolygon_ForValidDrawing()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.NotNull(result.Polygon);
        Assert.True(result.Polygon.Vertices.Count >= 4);
    }

    [Fact]
    public void ExtractPerimeterPolygon_InflatesPolygon_WhenSpacingNonZero()
    {
        var drawing = TestHelpers.MakeSquareDrawing(10);
        var noSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        var withSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 1);

        noSpacing.Polygon.UpdateBounds();
        withSpacing.Polygon.UpdateBounds();

        // The offset polygon should differ in size from the non-offset polygon.
        // OffsetSide.Left offsets outward or inward depending on winding,
        // but either way the result must be a different size.
        Assert.True(
            System.Math.Abs(withSpacing.Polygon.BoundingBox.Width - noSpacing.Polygon.BoundingBox.Width) > 0.5,
            $"Expected polygon width to differ by >0.5 with 1mm spacing. " +
            $"No-spacing width: {noSpacing.Polygon.BoundingBox.Width:F3}, " +
            $"With-spacing width: {withSpacing.Polygon.BoundingBox.Width:F3}");
    }

    [Fact]
    public void ExtractPerimeterPolygon_InflatedPolygonIsLarger_ForCWWinding()
    {
        // CW winding (standard CNC convention): (0,0)→(0,10)→(10,10)→(10,0)→(0,0)
        var drawing = TestHelpers.MakeSquareDrawing(10);
        var noSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        var withSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 1);

        noSpacing.Polygon.UpdateBounds();
        withSpacing.Polygon.UpdateBounds();

        Assert.True(withSpacing.Polygon.BoundingBox.Width > noSpacing.Polygon.BoundingBox.Width,
            $"Inflated width {withSpacing.Polygon.BoundingBox.Width:F3} should be > original {noSpacing.Polygon.BoundingBox.Width:F3}");
        Assert.True(withSpacing.Polygon.BoundingBox.Length > noSpacing.Polygon.BoundingBox.Length,
            $"Inflated length {withSpacing.Polygon.BoundingBox.Length:F3} should be > original {noSpacing.Polygon.BoundingBox.Length:F3}");
    }

    [Fact]
    public void ExtractPerimeterPolygon_InflatedPolygonIsLarger_ForCCWWinding()
    {
        // CCW winding: (0,0)→(10,0)→(10,10)→(0,10)→(0,0)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("ccw-square", pgm);

        var noSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        var withSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 1);

        noSpacing.Polygon.UpdateBounds();
        withSpacing.Polygon.UpdateBounds();

        Assert.True(withSpacing.Polygon.BoundingBox.Width > noSpacing.Polygon.BoundingBox.Width,
            $"Inflated width {withSpacing.Polygon.BoundingBox.Width:F3} should be > original {noSpacing.Polygon.BoundingBox.Width:F3}");
        Assert.True(withSpacing.Polygon.BoundingBox.Length > noSpacing.Polygon.BoundingBox.Length,
            $"Inflated length {withSpacing.Polygon.BoundingBox.Length:F3} should be > original {noSpacing.Polygon.BoundingBox.Length:F3}");
    }

    [Fact]
    public void ExtractPerimeterPolygon_ReturnsNull_ForEmptyDrawing()
    {
        var pgm = new Program();
        var drawing = new Drawing("empty", pgm);
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.Null(result.Polygon);
    }

    [Fact]
    public void ExtractPerimeterPolygon_CorrectionVector_ReflectsOriginDifference()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.NotNull(result.Polygon);
        Assert.True(System.Math.Abs(result.Correction.X) < 1);
        Assert.True(System.Math.Abs(result.Correction.Y) < 1);
    }

    [Fact]
    public void RotatePolygon_AtZero_ReturnsSamePolygon()
    {
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(0, 0));
        polygon.Vertices.Add(new Vector(10, 0));
        polygon.Vertices.Add(new Vector(10, 10));
        polygon.Vertices.Add(new Vector(0, 10));
        polygon.UpdateBounds();

        var rotated = PolygonHelper.RotatePolygon(polygon, 0);
        Assert.Same(polygon, rotated);
    }

    [Fact]
    public void RotatePolygon_At90Degrees_SwapsDimensions()
    {
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(0, 0));
        polygon.Vertices.Add(new Vector(20, 0));
        polygon.Vertices.Add(new Vector(20, 10));
        polygon.Vertices.Add(new Vector(0, 10));
        polygon.UpdateBounds();

        var rotated = PolygonHelper.RotatePolygon(polygon, Angle.HalfPI);
        rotated.UpdateBounds();

        Assert.True(System.Math.Abs(rotated.BoundingBox.Length - 10) < 0.1);
        Assert.True(System.Math.Abs(rotated.BoundingBox.Width - 20) < 0.1);
    }
}
