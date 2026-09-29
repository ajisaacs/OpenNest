using System.IO.Compression;
using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Apply_AppendsPlannedDefinitions_AfterExistingMixedSequence(int quadrant)
    {
        var settings = new CutOffSettings { PartClearance = 0.75, Overtravel = 2 };
        var nest = MakeNest(quadrant, settings);
        var plate = Assert.Single(nest.Plates);
        var realParts = CaptureRealParts(plate);
        var manual = Assert.Single(plate.CutOffs);
        var manualDefinition = Definition(manual);
        var originalSequence = plate.Parts.ToArray();

        var plan = CreatePlan(plate, settings);

        Assert.Equal(originalSequence, plate.Parts.ToArray());
        Assert.Same(manual, Assert.Single(plate.CutOffs));
        Assert.Equal(3, plan.PreviewParts.Count);
        Assert.All(plan.PreviewParts, preview => Assert.DoesNotContain(preview, plate.Parts));
        var previewPrograms = plan.PreviewParts.Select(p => NestWriter.GetProgramText(p.Program)).ToArray();

        Apply(plate, plan, settings);

        Assert.Equal(new[] { manual }.Concat(plan.Definitions), plate.CutOffs);
        Assert.Equal(manualDefinition, Definition(manual));
        AssertSequence(plate, realParts[0].Part, realParts[1].Part);
        AssertOnePartPerDefinition(plate);
        AssertRealPartsUnchanged(plate, realParts);
        for (var i = 0; i < plan.Definitions.Count; i++)
        {
            var materialized = plate.Parts[i + 3];
            Assert.Same(plan.Definitions[i].Drawing, materialized.BaseDrawing);
            Assert.NotSame(plan.PreviewParts[i], materialized);
            Assert.Equal(previewPrograms[i], NestWriter.GetProgramText(materialized.Program));
        }

        var repeated = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, settings);
        Assert.Empty(repeated.Definitions);
        Assert.Empty(repeated.PreviewParts);
        Assert.False(repeated.HasBlockingDiagnostics);
        Assert.True(repeated.HasSeparatedTail);
        Assert.Equal(plan.TailSeparatorX, repeated.TailSeparatorX);
        Assert.Equal(plan.UsedSpan, repeated.UsedSpan);
        Assert.Equal(plan.TailLength, repeated.TailLength);
        Assert.Equal(3, repeated.Diagnostics.Count(d => d.Code == AutomaticCutOffDiagnosticCode.ExistingCutOff));

        // Accepting the empty repeat and regenerating must not duplicate or resequence anything.
        Apply(plate, repeated, settings);
        Assert.Equal(new[] { manual }.Concat(plan.Definitions), plate.CutOffs);
        AssertSequence(plate, realParts[0].Part, realParts[1].Part);
        AssertOnePartPerDefinition(plate);
        AssertRealPartsUnchanged(plate, realParts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SaveAndReload_PreservesDefinitionsLimitsAndExactMixedSequence(int quadrant)
    {
        // NestReader uses default CutOffSettings; nondefault toolpath fidelity is not this contract.
        var settings = new CutOffSettings();
        var nest = MakeNest(quadrant, settings);
        var plate = Assert.Single(nest.Plates);
        var realParts = CaptureRealParts(plate);
        Apply(plate, CreatePlan(plate, settings), settings);
        var definitions = plate.CutOffs.Select(Definition).ToArray();

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        AssertArchiveContainsOnlyRealParts(stream, plate);
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();
        var loadedPlate = Assert.Single(loaded.Plates);
        var loadedRealParts = loadedPlate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToArray();

        Assert.Equal(quadrant, loadedPlate.Quadrant);
        Assert.Equal(plate.Size, loadedPlate.Size);
        Assert.Equal(plate.PartSpacing, loadedPlate.PartSpacing);
        Assert.Equal(plate.Quantity, loadedPlate.Quantity);
        Assert.Equal(definitions, loadedPlate.CutOffs.Select(Definition));
        Assert.Equal(realParts.Select(p => p.Location), loadedRealParts.Select(p => p.Location));
        Assert.Equal(realParts.Select(p => p.ProgramText), loadedRealParts.Select(p => NestWriter.GetProgramText(p.Program)));
        Assert.Equal(realParts.Select(p => p.Rotation), loadedRealParts.Select(p => p.Rotation));
        AssertSequence(loadedPlate, loadedRealParts[0], loadedRealParts[1]);
        AssertOnePartPerDefinition(loadedPlate);
        var loadedDrawing = Assert.Single(loaded.Drawings);
        Assert.False(loadedDrawing.IsCutOff);
        Assert.All(loadedRealParts, p => Assert.Same(loadedDrawing, p.BaseDrawing));
        Assert.Equal(realParts[0].Required, loadedDrawing.Quantity.Required);
        Assert.Equal(realParts[0].Nested, loadedDrawing.Quantity.Nested);
        AssertRealPartsUnchanged(plate, realParts);

        var loadedState = CaptureRealParts(loadedPlate);
        loadedPlate.RegenerateCutOffs(settings);
        Assert.Equal(definitions, loadedPlate.CutOffs.Select(Definition));
        AssertSequence(loadedPlate, loadedRealParts[0], loadedRealParts[1]);
        AssertOnePartPerDefinition(loadedPlate);
        AssertRealPartsUnchanged(loadedPlate, loadedState);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 10)]
    [InlineData(3, 10)]
    [InlineData(4, 10)]
    public void MovePartAfterReload_RegeneratesSafeSegments_WithoutMovingDefinitions(int quadrant, double offsetX)
    {
        var settings = new CutOffSettings();
        var nest = MakeNest(quadrant, settings);
        var source = Assert.Single(nest.Plates);
        Apply(source, CreatePlan(source, settings), settings);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();
        var plate = Assert.Single(loaded.Plates);
        var definitions = plate.CutOffs.ToArray();
        var definitionValues = definitions.Select(Definition).ToArray();
        var realParts = CaptureRealParts(plate);
        var moved = realParts[1].Part;
        var separator = definitions[^1];
        var separatorPart = plate.Parts[^1];
        var before = Assert.Single(Segments(separatorPart));
        var bounds = plate.BoundingBox(false);
        Assert.Equal(bounds.Bottom, before.From.Y);
        Assert.Equal(bounds.Top, before.To.Y);
        Assert.Equal(separator.Position.X, before.From.X);
        Assert.Equal(separator.Position.X, before.To.X);
        AssertCutsAvoidRectangles(plate, settings.PartClearance);

        // Move the outer part across the old separator: its fixed line must now be trimmed,
        // not shifted to a new tail or left as a stale full-width cut through the part.
        var offset = new Vector(quadrant is 2 or 3 ? -offsetX : offsetX, quadrant is 3 or 4 ? -10 : 10);
        moved.Offset(offset);
        Assert.Equal(realParts[1].Location + offset, moved.Location);
        var movedState = CaptureRealParts(plate);
        plate.RegenerateCutOffs(settings);

        Assert.Equal(definitions, plate.CutOffs.ToArray());
        Assert.Equal(definitionValues, plate.CutOffs.Select(Definition));
        AssertSequence(plate, realParts[0].Part, moved);
        AssertOnePartPerDefinition(plate);
        AssertRealPartsUnchanged(plate, movedState);
        AssertRealPartsUnchanged(plate, new[] { realParts[0], realParts[1] with { Location = moved.Location } });
        Assert.NotSame(separatorPart, plate.Parts[^1]);
        Assert.DoesNotContain(separatorPart, plate.Parts);
        var after = Segments(plate.Parts[^1]);
        Assert.Equal(2, after.Length);
        Assert.All(after, s =>
        {
            Assert.Equal(separator.Position.X, s.From.X);
            Assert.Equal(separator.Position.X, s.To.X);
        });
        AssertCutsAvoidRectangles(plate, settings.PartClearance);

        var regeneratedPrograms = plate.Parts.Where(p => p.BaseDrawing.IsCutOff)
            .Select(p => NestWriter.GetProgramText(p.Program)).ToArray();
        plate.RegenerateCutOffs(settings);
        Assert.Equal(definitionValues, plate.CutOffs.Select(Definition));
        Assert.Equal(regeneratedPrograms, plate.Parts.Where(p => p.BaseDrawing.IsCutOff)
            .Select(p => NestWriter.GetProgramText(p.Program)));
        AssertSequence(plate, realParts[0].Part, moved);
        AssertOnePartPerDefinition(plate);
        AssertRealPartsUnchanged(plate, movedState);
        AssertCutsAvoidRectangles(plate, settings.PartClearance);
    }

    private static Nest MakeNest(int quadrant, CutOffSettings settings)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(20, 0));
        program.Codes.Add(new LinearMove(20, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        Assert.True(Assert.Single(ShapeBuilder.GetShapes(ConvertProgram.ToGeometry(program))).IsClosed());
        var drawing = new Drawing("square", program);
        Assert.Equal(400, drawing.Area);
        drawing.Quantity.Required = 10;
        var nest = new Nest("automatic-cutoff-lifecycle");
        nest.Drawings.Add(drawing);
        var plate = new Plate(81, 120) { Quadrant = quadrant, PartSpacing = 0.5, Quantity = 2 };
        var negativeX = quadrant is 2 or 3;
        var negativeY = quadrant is 3 or 4;
        var first = new Part(drawing, new Vector(negativeX ? -30 : 10, negativeY ? -40 : 20));
        var second = new Part(drawing, new Vector(negativeX ? -80 : 60, negativeY ? -40 : 20));
        plate.Parts.Add(first);
        plate.Parts.Add(second);
        var manual = new CutOff(new Vector(negativeX ? -15 : 15, negativeY ? -10 : 10), CutOffAxis.Horizontal)
        {
            StartLimit = negativeX ? -25 : 5,
            EndLimit = negativeX ? -5 : 25,
        };
        plate.CutOffs.Add(manual);
        plate.RegenerateCutOffs(settings);
        var manualPart = plate.Parts[^1];
        plate.Parts.Remove(manualPart);
        plate.Parts.Insert(1, manualPart);
        Assert.Equal(new[] { first, manualPart, second }, plate.Parts);
        Assert.Equal(4, drawing.Quantity.Nested);
        nest.Plates.Add(plate);
        return nest;
    }

    private static AutomaticCutOffPlan CreatePlan(Plate plate, CutOffSettings settings)
    {
        var plan = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, settings);
        var sign = plate.Quadrant is 2 or 3 ? -1 : 1;
        var separator = 80 + System.Math.Max(plate.PartSpacing, settings.PartClearance) + Tolerance.Epsilon;
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.True(plan.HasSeparatedTail);
        Assert.Equal(80, plan.OccupiedSpan);
        Assert.Equal(separator, plan.UsedSpan);
        Assert.Equal(120 - separator, plan.TailLength);
        Assert.Equal(sign * separator, plan.TailSeparatorX);
        Assert.Equal(new[] { sign * 35.0, sign * 70.0, sign * separator }, plan.Definitions.Select(c => c.Position.X));
        Assert.All(plan.Definitions, c =>
        {
            Assert.Equal(CutOffAxis.Vertical, c.Axis);
            Assert.Null(c.StartLimit);
            Assert.Null(c.EndLimit);
        });
        return plan;
    }

    private static void Apply(Plate plate, AutomaticCutOffPlan plan, CutOffSettings settings)
    {
        Assert.False(plan.HasBlockingDiagnostics);
        foreach (var definition in plan.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);
    }

    private static void AssertSequence(Plate plate, Part first, Part second) =>
        Assert.Collection(plate.Parts,
            p => Assert.Same(first, p),
            p => Assert.Same(plate.CutOffs[0].Drawing, p.BaseDrawing),
            p => Assert.Same(second, p),
            p => Assert.Same(plate.CutOffs[1].Drawing, p.BaseDrawing),
            p => Assert.Same(plate.CutOffs[2].Drawing, p.BaseDrawing),
            p => Assert.Same(plate.CutOffs[3].Drawing, p.BaseDrawing));

    private static void AssertOnePartPerDefinition(Plate plate)
    {
        Assert.Equal(4, plate.CutOffs.Count);
        Assert.Equal(4, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
        Assert.All(plate.CutOffs, c =>
        {
            Assert.True(c.Drawing.IsCutOff);
            Assert.Single(plate.Parts.Where(p => ReferenceEquals(p.BaseDrawing, c.Drawing)));
        });
    }

    private static (double X, double Y, CutOffAxis Axis, double? Start, double? End) Definition(CutOff cut) =>
        (cut.Position.X, cut.Position.Y, cut.Axis, cut.StartLimit, cut.EndLimit);

    private sealed record RealPartState(Part Part, Program Program, string ProgramText, Vector Location,
        double Rotation, Program DrawingProgram, string DrawingText, int Required, int Nested);

    private static RealPartState[] CaptureRealParts(Plate plate) => plate.Parts
        .Where(p => !p.BaseDrawing.IsCutOff)
        .Select(p => new RealPartState(p, p.Program, NestWriter.GetProgramText(p.Program), p.Location,
            p.Rotation, p.BaseDrawing.Program, NestWriter.GetProgramText(p.BaseDrawing.Program),
            p.BaseDrawing.Quantity.Required, p.BaseDrawing.Quantity.Nested)).ToArray();

    private static void AssertRealPartsUnchanged(Plate plate, RealPartState[] expected)
    {
        Assert.Equal(expected.Select(s => s.Part), plate.Parts.Where(p => !p.BaseDrawing.IsCutOff));
        Assert.All(expected, s =>
        {
            Assert.Same(s.Program, s.Part.Program);
            Assert.Equal(s.ProgramText, NestWriter.GetProgramText(s.Part.Program));
            Assert.Equal(s.Location, s.Part.Location);
            Assert.Equal(s.Rotation, s.Part.Rotation);
            Assert.Same(s.DrawingProgram, s.Part.BaseDrawing.Program);
            Assert.Equal(s.DrawingText, NestWriter.GetProgramText(s.Part.BaseDrawing.Program));
            Assert.Equal(s.Required, s.Part.BaseDrawing.Quantity.Required);
            Assert.Equal(s.Nested, s.Part.BaseDrawing.Quantity.Nested);
        });
    }

    private static void AssertArchiveContainsOnlyRealParts(MemoryStream stream, Plate plate)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("nest.json")!.Open());
        using var json = JsonDocument.Parse(reader.ReadToEnd());
        var savedPlate = Assert.Single(json.RootElement.GetProperty("plates").EnumerateArray());
        var savedParts = savedPlate.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal(2, savedParts.Length);
        Assert.Single(json.RootElement.GetProperty("drawings").EnumerateArray());
        Assert.Single(archive.Entries.Where(e => e.FullName.StartsWith("programs/", StringComparison.Ordinal)));
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("parts/", StringComparison.Ordinal));
        var realParts = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToArray();
        for (var i = 0; i < realParts.Length; i++)
        {
            Assert.Equal(realParts[i].Location.X, savedParts[i].GetProperty("x").GetDouble());
            Assert.Equal(realParts[i].Location.Y, savedParts[i].GetProperty("y").GetDouble());
        }
        var cuts = savedPlate.GetProperty("cutOffs").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 3, 4, 5 }, cuts.Select(c => c.GetProperty("sequence").GetInt32()));
    }

    private static (Vector From, Vector To)[] Segments(Part part)
    {
        Assert.NotEmpty(part.Program.Codes);
        Assert.Equal(0, part.Program.Codes.Count % 2);
        return Enumerable.Range(0, part.Program.Codes.Count / 2).Select(i => (
            Assert.IsType<RapidMove>(part.Program.Codes[2 * i]).EndPoint + part.Location,
            Assert.IsType<LinearMove>(part.Program.Codes[2 * i + 1]).EndPoint + part.Location)).ToArray();
    }

    private static void AssertCutsAvoidRectangles(Plate plate, double clearance)
    {
        // Independent axis-aligned rectangle oracle: inspect every emitted cutting move,
        // not rapids, counts, or the same CutOff.ComputeSegments algorithm under test.
        var rectangles = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).Select(p => p.BoundingBox).ToArray();
        foreach (var cutPart in plate.Parts.Where(p => p.BaseDrawing.IsCutOff))
            foreach (var segment in Segments(cutPart))
            {
                var minX = System.Math.Min(segment.From.X, segment.To.X);
                var maxX = System.Math.Max(segment.From.X, segment.To.X);
                var minY = System.Math.Min(segment.From.Y, segment.To.Y);
                var maxY = System.Math.Max(segment.From.Y, segment.To.Y);
                Assert.True(minX == maxX || minY == maxY);
                Assert.True(minX < maxX || minY < maxY);
                Assert.All(rectangles, box => Assert.True(
                    maxX <= box.Left - clearance || minX >= box.Right + clearance ||
                    maxY <= box.Bottom - clearance || minY >= box.Top + clearance,
                    $"Cut {segment.From} -> {segment.To} crosses a rectangle or its clearance."));
            }
    }
}
