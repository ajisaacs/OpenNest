using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffBatchTests
{
    private static readonly AutomaticCutOffOptions Options = new() { Spacing = 35 };

    [Fact]
    public void Apply_AllPlates_UsesEachSheetAndPreservesRealPartsAndManualSequence()
    {
        var settings = new CutOffSettings();
        var plates = new[] { CreatePlate(1, 36, 120), CreatePlate(3, 50, 100), new Plate(60, 120) };
        var realParts = plates.SelectMany(p => p.Parts).ToArray();
        var realPrograms = realParts.Select(p => p.Program).ToArray();
        var quantities = realParts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        var manual = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
        plates[0].CutOffs.Add(manual);
        plates[0].RegenerateCutOffs(settings);
        var manualPart = plates[0].Parts[^1];
        plates[0].Parts.Remove(manualPart);
        plates[0].Parts.Insert(0, manualPart);

        var preview = AutomaticCutOffBatch.Create(plates, Options, settings);
        Assert.Same(manual, Assert.Single(plates[0].CutOffs));
        Assert.Empty(plates[1].CutOffs);
        Assert.Empty(preview[2].Definitions);

        var plans = AutomaticCutOffBatch.Apply(plates, Options, settings);

        Assert.All(plans, p => Assert.False(p.HasBlockingDiagnostics));
        Assert.Equal(3, plans[0].Definitions.Count);
        Assert.Equal(3, plans[1].Definitions.Count);
        Assert.Same(manual, plates[0].CutOffs[0]);
        Assert.Same(manual.Drawing, plates[0].Parts[0].BaseDrawing);
        Assert.Same(realParts[0], plates[0].Parts[1]);
        Assert.Same(realParts[1], plates[1].Parts[0]);
        Assert.All(plates.Take(2), p => Assert.Equal(p.CutOffs.Count, p.Parts.Count(x => x.BaseDrawing.IsCutOff)));
        Assert.All(plates[1].CutOffs, c => Assert.True(c.Position.X < 0));
        var tailProgram = plates[1].Parts[^1].Program;
        Assert.Equal(50, tailProgram.BoundingBox().Width, 6);
        Assert.Empty(plates[2].Parts);
        Assert.Empty(plates[2].CutOffs);
        Assert.Equal(realPrograms, realParts.Select(p => p.Program));
        Assert.Equal(quantities, realParts.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.All(preview.SelectMany(p => p.PreviewParts), p =>
            Assert.DoesNotContain(p, plates.SelectMany(plate => plate.Parts)));

        var originalParts = plates.Select(p => p.Parts.ToArray()).ToArray();
        var repeated = AutomaticCutOffBatch.Apply(plates, Options, settings);
        Assert.All(repeated, p => Assert.Empty(p.Definitions));
        for (var i = 0; i < plates.Length; i++)
            Assert.Equal(originalParts[i], plates[i].Parts.ToArray());

        var nest = new Nest("batch-cutoffs");
        foreach (var part in realParts)
            nest.Drawings.Add(part.BaseDrawing);
        nest.Plates.AddRange(plates);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();
        // The existing writer omits empty plates.
        Assert.Equal(2, loaded.Plates.Count);
        for (var i = 0; i < loaded.Plates.Count; i++)
        {
            Assert.Equal(plates[i].CutOffs.Select(c => (c.Position.X, c.Axis, c.StartLimit, c.EndLimit)),
                loaded.Plates[i].CutOffs.Select(c => (c.Position.X, c.Axis, c.StartLimit, c.EndLimit)));
            Assert.Equal(plates[i].Parts.Select(p => p.BaseDrawing.IsCutOff),
                loaded.Plates[i].Parts.Select(p => p.BaseDrawing.IsCutOff));
        }
    }

    [Fact]
    public void Apply_ConflictOnLaterPlate_ChangesNothingOnAnyPlate()
    {
        var plates = new[] { CreatePlate(), CreatePlate() };
        var settings = new CutOffSettings();
        var preview = AutomaticCutOffBatch.Create(plates, Options, settings);
        Assert.All(preview, p => Assert.NotEmpty(p.Definitions));
        var limited = new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { EndLimit = 2 };
        plates[1].CutOffs.Add(limited);
        var beforeParts = plates.Select(p => p.Parts.ToArray()).ToArray();

        var result = AutomaticCutOffBatch.Apply(plates, Options, settings);

        Assert.True(result[1].HasBlockingDiagnostics);
        Assert.Empty(plates[0].CutOffs);
        Assert.Same(limited, Assert.Single(plates[1].CutOffs));
        for (var i = 0; i < plates.Length; i++)
            Assert.Equal(beforeParts[i], plates[i].Parts.ToArray());
    }

    [Fact]
    public void Apply_InvalidLaterPlate_ReportsPlateNumberBeforeChangingAnything()
    {
        var plates = new[] { CreatePlate(), CreatePlate() };
        plates[1].Parts[0].Offset(500, 0);

        var error = Assert.Throws<ArgumentException>(() =>
            AutomaticCutOffBatch.Apply(plates, Options, new CutOffSettings()));

        Assert.Contains("Plate 2:", error.Message);
        Assert.All(plates, p => Assert.Empty(p.CutOffs));
        Assert.All(plates, p => Assert.Single(p.Parts));
    }

    [Fact]
    public void Apply_ReplansCurrentPartsAndSettingsInsteadOfAcceptingPreview()
    {
        var plates = new[] { CreatePlate(), CreatePlate() };
        var settings = new CutOffSettings();
        var preview = AutomaticCutOffBatch.Create(plates, Options, settings);
        plates[1].Parts[0].Offset(5, 0);
        settings.PartClearance = 2;
        var expected = AutomaticCutOffPlanner.Create(plates[1], Options, settings);

        var result = AutomaticCutOffBatch.Apply(plates, Options, settings);

        Assert.NotEqual(preview[1].TailSeparatorX, result[1].TailSeparatorX);
        Assert.Equal(expected.Definitions.Select(c => c.Position.X), plates[1].CutOffs.Select(c => c.Position.X));
    }

    [Fact]
    public void Apply_RegenerationFailureOnLaterPlate_RestoresEveryTouchedPlate()
    {
        var plates = new[] { CreatePlate(), CreatePlate(), CreatePlate() };
        var settings = new CutOffSettings();
        foreach (var plate in plates)
        {
            plate.CutOffs.Add(new CutOff(new Vector(5, 0), CutOffAxis.Vertical));
            plate.RegenerateCutOffs(settings);
            var cutoffPart = plate.Parts[^1];
            plate.Parts.Remove(cutoffPart);
            plate.Parts.Insert(0, cutoffPart);
        }
        var beforeParts = plates.Select(p => p.Parts.ToArray()).ToArray();
        var beforeDefinitions = plates.Select(p => p.CutOffs.ToArray()).ToArray();
        var beforePrograms = plates.Select(p => p.CutOffs[0].Drawing.Program).ToArray();
        var quantities = plates.Select(p => p.Parts[1].BaseDrawing.Quantity.Nested).ToArray();
        var failOnce = true;
        plates[1].PartAdded += (_, e) =>
        {
            if (failOnce && e.Item.BaseDrawing.IsCutOff)
            {
                failOnce = false;
                throw new InvalidOperationException("Injected regeneration failure");
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            AutomaticCutOffBatch.Apply(plates, Options, settings));

        Assert.False(failOnce);
        Assert.Contains("original cut-offs were restored", error.Message);
        for (var i = 0; i < plates.Length; i++)
        {
            Assert.Equal(beforeParts[i], plates[i].Parts.ToArray());
            Assert.Equal(beforeDefinitions[i], plates[i].CutOffs.ToArray());
            Assert.Same(beforePrograms[i], plates[i].CutOffs[0].Drawing.Program);
            Assert.Equal(quantities[i], plates[i].Parts[1].BaseDrawing.Quantity.Nested);
        }
    }

    [Fact]
    public void Apply_MinimumTailIsEvaluatedIndependentlyForEachPlate()
    {
        var plates = new[] { CreatePlate(length: 90), CreatePlate(), new Plate(36, 90) };
        var options = new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 12 };

        var result = AutomaticCutOffBatch.Apply(plates, options, new CutOffSettings());

        Assert.False(result[0].HasSeparatedTail);
        Assert.Equal(new[] { 35.0, 70.0 }, plates[0].CutOffs.Select(c => c.Position.X));
        Assert.True(result[1].HasSeparatedTail);
        Assert.Equal(3, plates[1].CutOffs.Count);
        Assert.Empty(plates[2].CutOffs);
        Assert.Empty(AutomaticCutOffBatch.Apply(plates, options, new CutOffSettings()).SelectMany(p => p.Definitions));
    }

    [Fact]
    public void Apply_RollbackFailureIsExplicitAndDoesNotStopRecoveryOfOtherPlates()
    {
        var plates = new[] { CreatePlate(), CreatePlate() };
        var originals = plates.Select(p => p.Parts.ToArray()).ToArray();
        plates[1].PartAdded += (_, _) => throw new InvalidOperationException("Apply observer failed");
        plates[1].CutOffs.ItemRemoved += (_, _) => throw new InvalidOperationException("Rollback observer failed");

        var error = Assert.Throws<InvalidOperationException>(() =>
            AutomaticCutOffBatch.Apply(plates, Options, new CutOffSettings()));

        Assert.Contains("Restoring cut-offs also failed", error.Message);
        Assert.Contains("Plate 2", error.Message);
        Assert.Contains("may be incomplete", error.Message);
        Assert.IsType<AggregateException>(error.InnerException);
        Assert.Empty(plates[0].CutOffs);
        Assert.Equal(originals[0], plates[0].Parts.ToArray());
    }

    private static Plate CreatePlate(int quadrant = 1, double width = 36, double length = 120)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(70, 0));
        program.Codes.Add(new LinearMove(70, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        var plate = new Plate(width, length) { Quadrant = quadrant, PartSpacing = 0.5, Quantity = 2 };
        var drawing = new Drawing($"rectangle-q{quadrant}", program);
        var location = new Vector(quadrant is 2 or 3 ? -80 : 10, quadrant is 3 or 4 ? -28 : 8);
        plate.Parts.Add(new Part(drawing, location));
        return plate;
    }
}
