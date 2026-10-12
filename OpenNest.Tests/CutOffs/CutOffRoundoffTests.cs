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

    [SkippableFact]
    public void RoundedImportedArcs_PlanAllPlatesWithoutEditingSource()
    {
        var path = TestConfig.GetExistingPath("RoundedCutOffNestPath");
        Skip.If(path == null, "Configure RoundedCutOffNestPath in ignored test-config.json.");
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path!));
        var nest = new NestReader(path).Read();
        var plates = nest.Plates.Where(p => p.Parts.Count > 0).ToArray();
        Assert.Equal(2, plates.Length);
        var parts = plates.SelectMany(p => p.Parts).ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var bounds = parts.Select(p => p.BoundingBox).ToArray();
        var result = AutomaticCutOffBatch.Create(plates,
            new AutomaticCutOffOptions { Spacing = 4, MinimumTailLength = 12 }, new CutOffSettings());
        Assert.Equal(plates.Length, result.Count);
        Assert.All(result, p => Assert.False(p.HasBlockingDiagnostics));
        Assert.All(result, p => Assert.NotEmpty(p.Definitions));
        var defaultSpacing = AutomaticCutOffBatch.Create(plates,
            new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 12 }, new CutOffSettings());
        Assert.All(defaultSpacing, p => Assert.False(p.HasBlockingDiagnostics));
        Assert.All(defaultSpacing, p => Assert.NotEmpty(p.Definitions));
        // Probe a separator in the tiny converted-arc protrusion: both cached and
        // uncached regeneration must leave the original curve untouched.
        var witness = plates[0].Parts.SelectMany(part => ConvertProgram.ToGeometry(part.Program)
                .OfType<Arc>().Where(a => SpecialLayers.IsMaterial(a.Layer))
                .Select(a => (part, arc: a, top: a.BoundingBox.Translate(part.Location).Top)))
            .First(w => w.top > w.part.BoundingBox.Top + 8 *
                (System.Math.BitIncrement(w.part.BoundingBox.Top) - w.part.BoundingBox.Top));
        witness.arc.Offset(witness.part.Location);
        var y = (witness.part.BoundingBox.Top + witness.top) / 2;
        foreach (var useCache in new[] { false, true })
        {
            var cut = new CutOff(new Vector(0, y), CutOffAxis.Horizontal);
            cut.Regenerate(plates[0], new CutOffSettings { PartClearance = 0 },
                useCache ? Plate.BuildPerimeterCache(plates[0]) : null);
            foreach (var line in ConvertProgram.ToGeometry(cut.Drawing.Program).OfType<Line>()
                .Where(e => SpecialLayers.IsMaterial(e.Layer)))
                Assert.False(witness.arc.Intersects(line, out _), $"useCache={useCache} y={y:R} segment={line}");
        }
        Assert.Equal(programs, parts.Select(p => p.Program));
        Assert.Equal(bounds, parts.Select(p => p.BoundingBox));
        Assert.All(plates, p => Assert.Empty(p.CutOffs));
        var applied = AutomaticCutOffBatch.Apply(plates,
            new AutomaticCutOffOptions { Spacing = 4, MinimumTailLength = 12 }, new CutOffSettings());
        Assert.Equal(result.Select(p => p.Definitions.Count), applied.Select(p => p.Definitions.Count));
        Assert.All(plates, p => Assert.NotEmpty(p.CutOffs));
        Assert.Equal(programs, parts.Select(p => p.Program));
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var reloaded = new NestReader(stream).Read();
        var repeated = AutomaticCutOffBatch.Create(reloaded.Plates.Where(p => p.Parts.Count > 0).ToArray(),
            new AutomaticCutOffOptions { Spacing = 4, MinimumTailLength = 12 }, new CutOffSettings());
        Assert.All(repeated, p => Assert.Empty(p.Definitions));
        Assert.Equal(hash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path!)));
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
