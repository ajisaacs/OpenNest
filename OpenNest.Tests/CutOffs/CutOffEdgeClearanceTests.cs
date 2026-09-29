using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

public class CutOffEdgeClearanceTests
{
    public static IEnumerable<object[]> RectangleCases()
    {
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
            foreach (var axis in new[] { CutOffAxis.Vertical, CutOffAxis.Horizontal })
                foreach (var cutCoordinate in new[] { 70.0, 80.0, 90.0 })
                    foreach (var zeroClearance in new[] { false, true })
                        foreach (var materialize in new[] { false, true })
                            yield return new object[] { quadrant, axis, cutCoordinate, zeroClearance, materialize };
    }

    [Theory]
    [MemberData(nameof(RectangleCases))]
    public void Rectangle_EdgesAndInterior_KeepClearance(
        int quadrant, CutOffAxis axis, double cutCoordinate, bool zeroClearance, bool materialize)
    {
        // Reported case: Plate(81,120), rectangle [70,90] x [30,50], vertical X=70.
        // Transpose for horizontal cuts, and reflect both the plate and part for each quadrant.
        var plate = axis == CutOffAxis.Vertical ? new Plate(81, 120) : new Plate(120, 81);
        plate.Quadrant = quadrant;
        var negativeX = quadrant is 2 or 3;
        var negativeY = quadrant is 3 or 4;
        var x0 = axis == CutOffAxis.Vertical ? 70.0 : 30.0;
        var y0 = axis == CutOffAxis.Vertical ? 30.0 : 70.0;
        var left = negativeX ? -x0 - 20 : x0;
        var bottom = negativeY ? -y0 - 20 : y0;
        var right = left + 20;
        var top = bottom + 20;
        var part = new Part(MakeRectangle(), new Vector(left, bottom));
        plate.Parts.Add(part);
        Assert.Equal(left, part.BoundingBox.Left);
        Assert.Equal(right, part.BoundingBox.Right);
        Assert.Equal(bottom, part.BoundingBox.Bottom);
        Assert.Equal(top, part.BoundingBox.Top);

        var cutPosition = axis == CutOffAxis.Vertical
            ? new Vector(negativeX ? -cutCoordinate : cutCoordinate, 0)
            : new Vector(0, negativeY ? -cutCoordinate : cutCoordinate);
        var cutoff = new CutOff(cutPosition, axis);
        var settings = new CutOffSettings();
        if (zeroClearance)
            settings.PartClearance = 0;

        if (materialize)
        {
            plate.CutOffs.Add(cutoff);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                plate.RegenerateCutOffs(settings);
                var cutPart = Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
                Assert.Same(cutoff.Drawing, cutPart.BaseDrawing);
                AssertRectangleClearance(cutPart.Program, cutPart.Location, axis,
                    left, right, bottom, top, settings.PartClearance);
            }
        }
        else
        {
            var cache = Plate.BuildPerimeterCache(plate);
            Assert.NotNull(cache[part]);
            cutoff.Regenerate(plate, settings, cache);
            AssertRectangleClearance(cutoff.Drawing.Program, Vector.Zero, axis,
                left, right, bottom, top, settings.PartClearance);
        }
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical)]
    [InlineData(CutOffAxis.Horizontal)]
    public void ZeroClearance_ThroughTwoCrossingVertices_DoesNotCutMaterial(CutOffAxis axis)
    {
        // Both diamond tips are transverse crossings, each reported by two incident edges.
        var points = new[]
        {
            new Vector(70, 20), new Vector(90, 40),
            new Vector(70, 60), new Vector(50, 40),
        };
        AssertOccupiedSlice(points, axis, 70, 20, 60);
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical)]
    [InlineData(CutOffAxis.Horizontal)]
    public void ZeroClearance_TwoTangentNotches_DoNotToggleCrossingParity(CutOffAxis axis)
    {
        // At X=70 the notch tips only touch the line: material remains between Y=20 and 80.
        // Deduplicating their events would treat each tangency as a crossing and cut Y=40..60.
        var points = new[]
        {
            new Vector(60, 20), new Vector(90, 20), new Vector(90, 35),
            new Vector(70, 40), new Vector(90, 45), new Vector(90, 55),
            new Vector(70, 60), new Vector(90, 65), new Vector(90, 80), new Vector(60, 80),
        };
        AssertOccupiedSlice(points, axis, 70, 20, 80);
    }

    private static void AssertOccupiedSlice(Vector[] points, CutOffAxis axis,
        double cutCoordinate, double occupiedStart, double occupiedEnd)
    {
        Vector Orient(Vector point) => axis == CutOffAxis.Vertical
            ? point : new Vector(point.Y, point.X);
        var program = new Program();
        program.Codes.Add(new RapidMove(Orient(points[0])));
        foreach (var point in points.Skip(1).Append(points[0]))
            program.Codes.Add(new LinearMove(Orient(point)));
        Assert.True(Assert.Single(ShapeBuilder.GetShapes(ConvertProgram.ToGeometry(program)
            .Where(entity => SpecialLayers.IsMaterial(entity.Layer)))).IsClosed());
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("vertex-events", program)));
        var cutoff = new CutOff(Orient(new Vector(cutCoordinate, 0)), axis);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(new CutOffSettings { PartClearance = 0 });
        var cutPart = Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
        var codes = cutPart.Program.Codes;
        Assert.Equal(4, codes.Count);
        var before = Assert.IsType<LinearMove>(codes[1]).EndPoint + cutPart.Location;
        var after = Assert.IsType<RapidMove>(codes[2]).EndPoint + cutPart.Location;
        Assert.Equal(occupiedStart, axis == CutOffAxis.Vertical ? before.Y : before.X, 9);
        Assert.Equal(occupiedEnd, axis == CutOffAxis.Vertical ? after.Y : after.X, 9);
    }

    private static Drawing MakeRectangle()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(20, 0));
        program.Codes.Add(new LinearMove(20, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        Assert.True(Assert.Single(ShapeBuilder.GetShapes(ConvertProgram.ToGeometry(program)
            .Where(entity => SpecialLayers.IsMaterial(entity.Layer)))).IsClosed());
        var drawing = new Drawing("edge-clearance-rectangle", program);
        Assert.Equal(400, drawing.Area);
        return drawing;
    }

    private static void AssertRectangleClearance(Program program, Vector location, CutOffAxis axis,
        double left, double right, double bottom, double top, double clearance)
    {
        // Independent analytic oracle: no production offsets, intersections or distance helpers.
        // The distance between an axis-aligned segment and a filled rectangle is the norm of
        // the gaps between their X/Y intervals. Zero clearance still forbids cutting along an edge.
        Assert.NotEmpty(program.Codes);
        Assert.Equal(0, program.Codes.Count % 2);
        for (var i = 0; i < program.Codes.Count; i += 2)
        {
            var start = Assert.IsType<RapidMove>(program.Codes[i]).EndPoint + location;
            var end = Assert.IsType<LinearMove>(program.Codes[i + 1]).EndPoint + location;
            var minX = System.Math.Min(start.X, end.X);
            var maxX = System.Math.Max(start.X, end.X);
            var minY = System.Math.Min(start.Y, end.Y);
            var maxY = System.Math.Max(start.Y, end.Y);
            var dx = IntervalGap(minX, maxX, left, right);
            var dy = IntervalGap(minY, maxY, bottom, top);
            var distance = System.Math.Sqrt(dx * dx + dy * dy);
            Assert.True(distance >= clearance - 1e-9,
                $"Cut {start} -> {end} is only {distance:R} from the rectangle; requires {clearance:R}.");

            var cutsMaterial = axis == CutOffAxis.Vertical
                ? minX >= left && minX <= right && minY < top && maxY > bottom
                : minY >= bottom && minY <= top && minX < right && maxX > left;
            Assert.False(cutsMaterial, $"Cut {start} -> {end} crosses or follows the rectangle.");
        }

        // Must retain the useful scrap cuts on both sides, not suppress the entire line.
        Assert.Equal(4, program.Codes.Count);
    }

    private static double IntervalGap(double a0, double a1, double b0, double b1) =>
        System.Math.Max(0, System.Math.Max(b0 - a1, a0 - b1));
}
