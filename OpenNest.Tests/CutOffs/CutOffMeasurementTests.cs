using System.Reflection;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

public class CutOffMeasurementTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(4, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(3, false, true)]
    [InlineData(4, false, true)]
    public void Measurement_MatchesRegeneratedSegmentsWithoutMutatingDrawing(int quadrant, bool limited, bool excluded)
    {
        var plate = new Plate(60, 120) { Quadrant = quadrant };
        var negativeX = quadrant is 2 or 3;
        var negativeY = quadrant is 3 or 4;
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(20, 0));
        program.Codes.Add(new LinearMove(20, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        plate.Parts.Add(new Part(new Drawing("measurement rectangle", program),
            new Vector(negativeX ? -40 : 20, negativeY ? -40 : 20)));
        var bounds = plate.BoundingBox(false);
        var cut = new CutOff(new Vector(negativeX ? -30 : 30, 0), CutOffAxis.Vertical);
        var settings = new CutOffSettings
        {
            PartClearance = excluded ? 100 : 0.5,
            Overtravel = 2,
            MinSegmentLength = limited ? 21 : 0,
            CutDirection = quadrant % 2 == 0 ? CutDirection.TowardOrigin : CutDirection.AwayFromOrigin,
        };
        if (limited)
        {
            cut.StartLimit = bounds.Bottom + 5;
            cut.EndLimit = bounds.Top - 5;
        }
        var cache = Plate.BuildPerimeterCache(plate);
        var original = cut.Drawing.Program;
        // Reflection keeps the first run behavioral-red on the unchanged production tree.
        var method = typeof(CutOff).GetMethod("MeasureSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var measured = Assert.IsType<ValueTuple<double, int>>(method.Invoke(cut, new object[] { plate, settings, cache }));
        Assert.Same(original, cut.Drawing.Program);
        cut.Regenerate(plate, settings, cache);
        var length = Enumerable.Range(0, cut.Drawing.Program.Codes.Count / 2).Sum(i =>
            System.Math.Abs(Assert.IsType<LinearMove>(cut.Drawing.Program.Codes[2 * i + 1]).EndPoint.Y -
                Assert.IsType<RapidMove>(cut.Drawing.Program.Codes[2 * i]).EndPoint.Y));
        Assert.Equal(length, measured.Item1, 10);
        Assert.Equal(cut.Drawing.Program.Codes.Count / 2, measured.Item2);
        if (excluded)
            Assert.Equal((0.0, 0), measured);
    }
}
