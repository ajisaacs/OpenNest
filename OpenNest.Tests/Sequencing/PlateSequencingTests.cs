using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Sequencing;

public class PlateSequencingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ApplyAll_WithManagedNest_SequencesEveryPlateAndPreservesParts(int plateCount)
    {
        var nest = new Nest("sequence-all");
        var drawing = TestHelpers.MakeSquareDrawing();
        nest.Drawings.Add(drawing);
        for (var i = 0; i < plateCount; i++)
        {
            var plate = TestHelpers.MakePlate(60, 120,
                new Part(drawing, new Vector(10, 5)),
                new Part(drawing, new Vector(30, 20)),
                new Part(drawing, new Vector(20, 10)));
            plate.Quantity = i + 1;
            nest.Plates.Add(plate);
        }

        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        manager.LoadFirst();
        var plates = nest.Plates.Take(plateCount).ToArray();
        var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
        var expected = plates.Select(plate => Baseline(plate, parameters)).ToArray();
        var parts = plates.SelectMany(plate => plate.Parts)
            .Select(part => (Part: part, part.Program, part.Location, part.Rotation)).ToArray();
        var nested = drawing.Quantity.Nested;
        var added = new int[plateCount];
        for (var i = 0; i < plateCount; i++)
        {
            var index = i;
            plates[i].PartAdded += (_, _) => added[index]++;
        }

        PlateSequencing.ApplyAll(nest.Plates, parameters);

        Assert.Equal(plateCount + 1, nest.Plates.Count);
        Assert.Equal(plates, nest.Plates.Take(plateCount));
        Assert.Empty(nest.Plates[^1].Parts);
        Assert.Same(plates[0], manager.CurrentPlate);
        for (var i = 0; i < plateCount; i++)
        {
            Assert.Equal(expected[i], plates[i].Parts);
            Assert.Equal(expected[i].Length, added[i]);
            Assert.Equal(i + 1, plates[i].Quantity);
        }
        Assert.Equal(nested, drawing.Quantity.Nested);
        foreach (var item in parts)
        {
            Assert.Same(item.Program, item.Part.Program);
            Assert.Equal(item.Location, item.Part.Location);
            Assert.Equal(item.Rotation, item.Part.Rotation);
        }
    }

    [Fact]
    public void ApplyAll_EmptyNestAndSentinelOnly_AreUnchanged()
    {
        var nest = new Nest("empty");
        var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
        PlateSequencing.ApplyAll(nest.Plates, parameters);
        Assert.Empty(nest.Plates);

        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        var sentinel = Assert.Single(nest.Plates);

        PlateSequencing.ApplyAll(nest.Plates, parameters);

        Assert.Same(sentinel, Assert.Single(nest.Plates));
        Assert.Empty(sentinel.Parts);
    }

    [Fact]
    public void ApplyAll_InvalidSequenceMethod_LeavesAllPlatesUntouched()
    {
        var nest = new Nest("invalid-sequence");
        nest.Plates.Add(TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(10, 5)));
        nest.Plates.Add(TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(30, 20)));
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        var plates = nest.Plates.ToArray();
        var parts = plates.Select(plate => plate.Parts.ToArray()).ToArray();

        Assert.Throws<NotSupportedException>(() => PlateSequencing.ApplyAll(nest.Plates,
            new SequenceParameters { Method = (SequenceMethod)999 }));

        Assert.Equal(plates, nest.Plates);
        for (var i = 0; i < plates.Length; i++)
            Assert.Equal(parts[i], plates[i].Parts);
    }

    [Fact]
    public void ApplyAll_WithManagedNest_PreservesCutOffDependenciesOnEveryPlate()
    {
        var nest = new Nest("sequence-all-cutoffs");
        var plates = new[]
        {
            TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(10, 10, 10)),
            TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(10, 30, 10)),
        };
        var parts = plates.Select(plate => Assert.Single(plate.Parts)).ToArray();
        foreach (var plate in plates)
        {
            plate.CutOffs.Add(new CutOff(new Vector(15, 0), CutOffAxis.Vertical));
            plate.RegenerateCutOffs(new CutOffSettings());
            nest.Plates.Add(plate);
        }
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();

        PlateSequencing.ApplyAll(nest.Plates,
            new SequenceParameters { Method = SequenceMethod.LeastCode });

        for (var i = 0; i < plates.Length; i++)
            AssertPrecedes(plates[i], Assert.Single(plates[i].CutOffs), parts[i]);
    }

    [Fact]
    public void Apply_WithoutCutOffs_PreservesExistingReversedSequencerOrder()
    {
        var plate = TestHelpers.MakePlate(60, 120,
            TestHelpers.MakePartAt(10, 5), TestHelpers.MakePartAt(30, 20),
            TestHelpers.MakePartAt(20, 10));
        var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
        var expected = Baseline(plate, parameters);

        PlateSequencing.Apply(plate, parameters);

        Assert.Equal(expected, plate.Parts);
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical, CutDirection.AwayFromOrigin)]
    [InlineData(CutOffAxis.Vertical, CutDirection.TowardOrigin)]
    [InlineData(CutOffAxis.Horizontal, CutDirection.AwayFromOrigin)]
    [InlineData(CutOffAxis.Horizontal, CutDirection.TowardOrigin)]
    public void Apply_MovesEveryCrossingCutOffBeforeEveryCrossedPart(
        CutOffAxis axis, CutDirection direction)
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var first = new Part(drawing, new Vector(10, 10));
        var second = new Part(drawing, axis == CutOffAxis.Vertical
            ? new Vector(10, 30) : new Vector(30, 10));
        var unrelated = TestHelpers.MakePartAt(60, 50);
        var plate = TestHelpers.MakePlate(60, 120, first, second, unrelated);
        plate.Quantity = 2;
        var cutA = new CutOff(new Vector(13, 13), axis);
        var cutB = new CutOff(new Vector(17, 17), axis);
        plate.CutOffs.Add(cutA);
        plate.CutOffs.Add(cutB);
        var settings = new CutOffSettings { CutDirection = direction };
        plate.RegenerateCutOffs(settings);
        var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
        var baseline = Baseline(plate, parameters);
        var programs = plate.Parts.Select(p => (Part: p, p.Program, p.Location, p.Rotation)).ToArray();
        var nested = drawing.Quantity.Nested;

        PlateSequencing.Apply(plate, parameters);

        AssertPrecedes(plate, cutA, first, second);
        AssertPrecedes(plate, cutB, first, second);
        Assert.Equal(baseline.Where(p => !p.BaseDrawing.IsCutOff),
            plate.Parts.Where(p => !p.BaseDrawing.IsCutOff));
        Assert.Equal(programs.Length, plate.Parts.Count);
        Assert.Equal(nested, drawing.Quantity.Nested);
        foreach (var item in programs)
        {
            Assert.Contains(item.Part, plate.Parts);
            Assert.Same(item.Program, item.Part.Program);
            Assert.Equal(item.Location, item.Part.Location);
            Assert.Equal(item.Rotation, item.Part.Rotation);
        }

        var sequence = plate.Parts.Select(p => p.BaseDrawing).ToArray();
        plate.RegenerateCutOffs(settings);
        Assert.Equal(sequence, plate.Parts.Select(p => p.BaseDrawing));
        AssertPrecedes(plate, cutA, first, second);
        AssertPrecedes(plate, cutB, first, second);
    }

    [Theory]
    [InlineData(SequenceMethod.RightSide)]
    [InlineData(SequenceMethod.LeftSide)]
    [InlineData(SequenceMethod.BottomSide)]
    [InlineData(SequenceMethod.EdgeStart)]
    [InlineData(SequenceMethod.LeastCode)]
    [InlineData(SequenceMethod.Advanced)]
    public void Apply_AllMethodsAndQuadrants_RespectCutOffDependencies(SequenceMethod method)
    {
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        {
            var plate = new Plate(60, 120) { Quadrant = quadrant };
            var bounds = plate.BoundingBox(false);
            var part = TestHelpers.MakePartAt(bounds.Left + 20, bounds.Bottom + 20, 10);
            part.Rotate(System.Math.PI / 4, part.Location);
            Assert.True(bounds.Contains(part.BoundingBox));
            plate.Parts.Add(part);
            var center = part.BoundingBox.Center;
            var vertical = new CutOff(center, CutOffAxis.Vertical);
            var horizontal = new CutOff(center, CutOffAxis.Horizontal);
            plate.CutOffs.Add(vertical);
            plate.CutOffs.Add(horizontal);
            plate.RegenerateCutOffs(new CutOffSettings());

            PlateSequencing.Apply(plate, new SequenceParameters { Method = method });

            AssertPrecedes(plate, vertical, part);
            AssertPrecedes(plate, horizontal, part);
        }
    }

    [Theory]
    [InlineData(CutOffAxis.Vertical, SequenceMethod.LeftSide)]
    [InlineData(CutOffAxis.Horizontal, SequenceMethod.BottomSide)]
    public void Apply_LimitedSameNamedCutOffs_OnlyMoveBeforePartsWithinTheirSpans(
        CutOffAxis axis, SequenceMethod method)
    {
        var first = TestHelpers.MakePartAt(10, 10, 10);
        var second = axis == CutOffAxis.Vertical
            ? TestHelpers.MakePartAt(10, 30, 10) : TestHelpers.MakePartAt(30, 10, 10);
        var plate = TestHelpers.MakePlate(60, 120, first, second);
        var lower = new CutOff(new Vector(15, 15), axis) { StartLimit = 5, EndLimit = 25 };
        var upper = new CutOff(new Vector(15, 15), axis) { StartLimit = 25, EndLimit = 45 };
        plate.CutOffs.Add(lower);
        plate.CutOffs.Add(upper);
        plate.RegenerateCutOffs(new CutOffSettings());
        Assert.Equal(lower.Drawing.Name, upper.Drawing.Name);
        var lowerPart = plate.Parts.Single(p => ReferenceEquals(p.BaseDrawing, lower.Drawing));
        var upperPart = plate.Parts.Single(p => ReferenceEquals(p.BaseDrawing, upper.Drawing));

        PlateSequencing.Apply(plate, new SequenceParameters { Method = method });

        Assert.Equal(new[] { upperPart, second, lowerPart, first }, plate.Parts);
        Assert.Equal(new[] { lower, upper }, plate.CutOffs);
    }

    [Fact]
    public void Apply_NonCrossingTailCutOff_KeepsItsNormalSequencePlace()
    {
        var part = TestHelpers.MakePartAt(10, 10, 10);
        var plate = TestHelpers.MakePlate(60, 120, part);
        var tail = new CutOff(new Vector(40, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(tail);
        plate.RegenerateCutOffs(new CutOffSettings());
        var parameters = new SequenceParameters { Method = SequenceMethod.LeftSide };
        var expected = Baseline(plate, parameters);
        Assert.Same(part, expected[0]);

        PlateSequencing.Apply(plate, parameters);

        Assert.Equal(expected, plate.Parts);
    }

    [Fact]
    public void Apply_OrphanedCutOff_ConservativelyPrecedesAllParts()
    {
        var part = TestHelpers.MakePartAt(10, 10, 10);
        var orphan = TestHelpers.MakePartAt(0, 0);
        orphan.BaseDrawing.IsCutOff = true;
        var plate = TestHelpers.MakePlate(60, 120, part, orphan);

        PlateSequencing.Apply(plate,
            new SequenceParameters { Method = SequenceMethod.LeftSide });

        Assert.Equal(new[] { orphan, part }, plate.Parts);
    }

    [Fact]
    public void Apply_EmptyAndCutOffOnlyPlates_PreserveAllEntries()
    {
        var plate = new Plate(60, 120);
        var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
        PlateSequencing.Apply(plate, parameters);
        Assert.Empty(plate.Parts);

        plate.CutOffs.Add(new CutOff(new Vector(10, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        var expected = Baseline(plate, parameters);

        PlateSequencing.Apply(plate, parameters);

        Assert.Equal(expected, plate.Parts);
    }

    [Fact]
    public void Apply_SaveAndReload_PreservesCorrectedMixedSequence()
    {
        var nest = new Nest("sequenced-cutoffs");
        var drawing = TestHelpers.MakeSquareDrawing();
        nest.Drawings.Add(drawing);
        var part = new Part(drawing, new Vector(10, 10));
        var plate = TestHelpers.MakePlate(60, 120, part);
        nest.Plates.Add(plate);
        var crossing = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        var tail = new CutOff(new Vector(40, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(crossing);
        plate.CutOffs.Add(tail);
        plate.RegenerateCutOffs(new CutOffSettings());
        PlateSequencing.Apply(plate,
            new SequenceParameters { Method = SequenceMethod.LeftSide });
        AssertPrecedes(plate, crossing, part);
        Assert.False(plate.Parts[1].BaseDrawing.IsCutOff);
        var expected = plate.Parts.Select(p => p.BaseDrawing.Name).ToArray();

        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();

        Assert.Equal(expected, loaded.Plates[0].Parts.Select(p => p.BaseDrawing.Name));
    }

    [Fact]
    public void Apply_InvalidSequenceMethod_LeavesPlateUntouched()
    {
        var plate = TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(10, 5));
        var before = plate.Parts.ToArray();

        Assert.Throws<NotSupportedException>(() => PlateSequencing.Apply(plate,
            new SequenceParameters { Method = (SequenceMethod)999 }));

        Assert.Equal(before, plate.Parts);
    }

    private static Part[] Baseline(Plate plate, SequenceParameters parameters) =>
        PartSequencerFactory.Create(parameters).Sequence(plate.Parts.ToList(), plate)
            .Select(p => p.Part).Reverse().ToArray();

    private static void AssertPrecedes(Plate plate, CutOff cutOff, params Part[] crossed)
    {
        var cutPart = Assert.Single(plate.Parts,
            p => ReferenceEquals(p.BaseDrawing, cutOff.Drawing));
        foreach (var part in crossed)
            Assert.True(plate.Parts.IndexOf(cutPart) < plate.Parts.IndexOf(part),
                $"{cutOff.Drawing.Name} must precede the part at {part.Location}.");
    }
}
