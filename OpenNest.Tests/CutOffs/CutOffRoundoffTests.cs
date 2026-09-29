using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class CutOffRoundoffTests
{
    // Like P260805-10: adding the local minimum and size after placement rounds
    // one representational step below translating a material endpoint directly.
    private static (Nest Nest, Plate Plate, Part Part) MakeNest(CutOffAxis axis)
    {
        Vector Point(double x, double y) => axis == CutOffAxis.Vertical ? new(x, y) : new(y, x);
        var program = new Program();
        program.Codes.Add(new RapidMove(Point(-0.54907, 2)));
        program.Codes.Add(new LinearMove(Point(2.80679, 2)));
        program.Codes.Add(new LinearMove(Point(2.80679, 6)));
        program.Codes.Add(new LinearMove(Point(-0.54907, 6)));
        program.Codes.Add(new LinearMove(Point(-0.54907, 2)));
        var drawing = new Drawing("translated rectangle", program);
        var part = new Part(drawing, Point(13.65687, 10));
        var plate = new Plate(60, 120) { PartSpacing = 0 };
        var nest = new Nest("roundoff");
        nest.Drawings.Add(drawing);
        nest.Plates.Add(plate);
        plate.Parts.Add(part);
        return (nest, plate, part);
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical)]
    [InlineData(CutOffAxis.Horizontal)]
    public void RoundoffOnlyBoundsMismatch_PlansAppliesAndRoundTripsWithoutChangingParts(CutOffAxis axis)
    {
        var (nest, plate, part) = MakeNest(axis);
        var program = part.Program;
        var text = NestWriter.GetProgramText(program);
        var box = part.BoundingBox;
        var location = part.Location;
        var quantity = part.BaseDrawing.Quantity.Nested;
        var options = new AutomaticCutOffOptions { Spacing = 5 };
        var settings = new CutOffSettings();
        var plan = AutomaticCutOffPlanner.Create(plate, options, settings);
        Assert.NotEmpty(plan.Definitions);
        Assert.True(plan.HasSeparatedTail);
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Same(part, Assert.Single(plate.Parts));
        Assert.Empty(plate.CutOffs);
        foreach (var definition in plan.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);
        Assert.Same(program, part.Program);
        Assert.Same(box, part.BoundingBox);
        Assert.Equal(location, part.Location);
        Assert.Equal(text, NestWriter.GetProgramText(part.Program));
        Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
        Assert.Empty(AutomaticCutOffPlanner.Create(plate, options, settings).Definitions);

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();
        var loadedPlate = Assert.Single(loaded.Plates);
        Assert.Equal(plate.CutOffs.Count, loadedPlate.CutOffs.Count);
        Assert.Empty(AutomaticCutOffPlanner.Create(loadedPlate, options, settings).Definitions);
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical, false, 0)]
    [InlineData(CutOffAxis.Vertical, true, 0)]
    [InlineData(CutOffAxis.Horizontal, false, 0)]
    [InlineData(CutOffAxis.Horizontal, true, 0)]
    [InlineData(CutOffAxis.Vertical, false, 0.02)]
    [InlineData(CutOffAxis.Vertical, true, 0.02)]
    [InlineData(CutOffAxis.Horizontal, false, 0.02)]
    [InlineData(CutOffAxis.Horizontal, true, 0.02)]
    public void LineBeyondRoundedCache_DoesNotSkipMaterialOrClearance(CutOffAxis axis, bool useCache, double clearance)
    {
        var (_, plate, part) = MakeNest(axis);
        var farEdge = 2.80679 + 13.65687;
        var cachedEdge = axis == CutOffAxis.Vertical ? part.BoundingBox.Right : part.BoundingBox.Top;
        Assert.True(farEdge > cachedEdge);
        var cutPosition = farEdge + clearance;
        Assert.True(cutPosition > cachedEdge + clearance);
        var cut = new CutOff(new Vector(cutPosition, cutPosition), axis);
        cut.Regenerate(plate, new CutOffSettings { PartClearance = clearance },
            useCache ? Plate.BuildPerimeterCache(plate) : null);

        // Independent rectangle oracle: even a line on the true edge must not cut
        // along that edge or through its clearance band. Both gaps must remain.
        var segments = ConvertProgram.ToGeometry(cut.Drawing.Program).OfType<Line>()
            .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToArray();
        Assert.Equal(2, segments.Length);
        foreach (var segment in segments)
        {
            var from = axis == CutOffAxis.Vertical ? segment.StartPoint.Y : segment.StartPoint.X;
            var to = axis == CutOffAxis.Vertical ? segment.EndPoint.Y : segment.EndPoint.X;
            var low = System.Math.Min(from, to);
            var high = System.Math.Max(from, to);
            Assert.True(high <= 12 || low >= 16);
            var alongGap = System.Math.Max(12 - high, low - 16);
            var acrossGap = cutPosition - farEdge;
            var distance = System.Math.Sqrt(acrossGap * acrossGap + alongGap * alongGap);
            Assert.True(distance >= clearance - 1e-12);
        }
    }
}
