using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffShiftLifecycleTests
{
    private static readonly AutomaticCutOffOptions Shifted = new()
    {
        Spacing = 35,
        MinimumStripLength = 17.5,
        MinimumTailLength = 12,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BatchApply_SaveReload_RerunAndMove_PreserveShiftedDefinitionsAndClearance(int quadrant)
    {
        var nest = new Nest("shifted lifecycle");
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        for (var index = 0; index < plate.Parts.Count; index++)
        {
            // The nest drawing collection keys by name; distinct synthetic shapes
            // must have distinct names for the writer to retain every part.
            var drawing = plate.Parts[index].BaseDrawing;
            drawing.Name = $"shift rectangle {index}";
            nest.Drawings.Add(drawing);
        }
        nest.Plates.Add(plate);
        var settings = new CutOffSettings();
        var realParts = plate.Parts.ToArray();
        var realPrograms = realParts.Select(p => p.Program).ToArray();
        var proposal = Assert.Single(AutomaticCutOffBatch.Create(new[] { plate }, Shifted, settings));
        Assert.Equal(3, proposal.Definitions.Count);
        Assert.Empty(plate.CutOffs);
        Assert.Equal(realParts, plate.Parts.ToArray());

        var applied = Assert.Single(AutomaticCutOffBatch.Apply(new[] { plate }, Shifted, settings));
        Assert.Equal(proposal.Definitions.Select(c => c.Position.X), applied.Definitions.Select(c => c.Position.X));
        var locations = plate.CutOffs.Select(c => c.Position.X).ToArray();
        AssertCutsClearRealRectangles(plate, settings.PartClearance);
        Assert.Equal(realPrograms, realParts.Select(p => p.Program));
        Assert.Empty(Assert.Single(AutomaticCutOffBatch.Apply(new[] { plate }, Shifted, settings)).Definitions);
        Assert.Equal(locations, plate.CutOffs.Select(c => c.Position.X));

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();
        var reloaded = Assert.Single(loaded.Plates);
        Assert.Equal(locations, reloaded.CutOffs.Select(c => c.Position.X));
        Assert.Equal(realParts.Length, reloaded.Parts.Count(p => !p.BaseDrawing.IsCutOff));
        var reloadedPlan = AutomaticCutOffPlanner.Create(reloaded, Shifted, settings);
        Assert.Equal(proposal.OccupiedSpan, reloadedPlan.OccupiedSpan);
        Assert.Empty(reloadedPlan.Definitions);
        AssertCutsClearRealRectangles(reloaded, settings.PartClearance);

        var moved = reloaded.Parts.First(p => !p.BaseDrawing.IsCutOff &&
            System.Math.Abs(p.BoundingBox.Right - p.BoundingBox.Left - 10) < 1e-6);
        moved.Offset(quadrant is 2 or 3 ? new Vector(-1, 0) : new Vector(1, 0));
        reloaded.RegenerateCutOffs(settings);
        Assert.Equal(locations, reloaded.CutOffs.Select(c => c.Position.X));
        AssertCutsClearRealRectangles(reloaded, settings.PartClearance);
    }

    [Fact]
    public void ShiftedBatch_LimitedSeparatorOnLaterPlateRefusesAllWithoutMutation()
    {
        var plates = new[] { AutomaticCutOffShiftTests.UserCase(), AutomaticCutOffShiftTests.UserCase() };
        var settings = new CutOffSettings();
        var separator = AutomaticCutOffPlanner.Create(plates[1], Shifted, settings).TailSeparatorX!.Value;
        var manual = new CutOff(new Vector(separator, 0), CutOffAxis.Vertical) { EndLimit = 2 };
        plates[1].CutOffs.Add(manual);
        var before = plates.Select(p => p.Parts.ToArray()).ToArray();

        var result = AutomaticCutOffBatch.Apply(plates, Shifted, settings);

        Assert.True(result[1].HasBlockingDiagnostics);
        Assert.Empty(plates[0].CutOffs);
        Assert.Same(manual, Assert.Single(plates[1].CutOffs));
        for (var i = 0; i < plates.Length; i++)
            Assert.Equal(before[i], plates[i].Parts.ToArray());
    }

    [Fact]
    public void ShiftedBatch_LateRegenerationFailureRollsBackEveryTouchedPlate()
    {
        var plates = new[] { AutomaticCutOffShiftTests.UserCase(), AutomaticCutOffShiftTests.UserCase() };
        var before = plates.Select(p => p.Parts.ToArray()).ToArray();
        var programs = plates.Select(p => p.Parts.Select(part => part.Program).ToArray()).ToArray();
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
            AutomaticCutOffBatch.Apply(plates, Shifted, new CutOffSettings()));

        Assert.False(failOnce);
        Assert.Contains("original cut-offs were restored", error.Message);
        for (var i = 0; i < plates.Length; i++)
        {
            Assert.Empty(plates[i].CutOffs);
            Assert.Equal(before[i], plates[i].Parts.ToArray());
            Assert.Equal(programs[i], plates[i].Parts.Select(p => p.Program).ToArray());
        }
    }

    private static void AssertCutsClearRealRectangles(Plate plate, double clearance)
    {
        var material = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).Select(p => p.BoundingBox).ToArray();
        foreach (var cut in plate.Parts.Where(p => p.BaseDrawing.IsCutOff))
        {
            var codes = cut.Program.Codes;
            Assert.Equal(0, codes.Count % 2);
            for (var index = 0; index < codes.Count; index += 2)
            {
                var from = Assert.IsType<RapidMove>(codes[index]).EndPoint + cut.Location;
                var to = Assert.IsType<LinearMove>(codes[index + 1]).EndPoint + cut.Location;
                var left = System.Math.Min(from.X, to.X);
                var right = System.Math.Max(from.X, to.X);
                var bottom = System.Math.Min(from.Y, to.Y);
                var top = System.Math.Max(from.Y, to.Y);
                Assert.All(material, box => Assert.True(
                    right <= box.Left - clearance || left >= box.Right + clearance ||
                    top <= box.Bottom - clearance || bottom >= box.Top + clearance,
                    $"Cut {from} -> {to} crosses a rectangle or its clearance."));
            }
        }
    }
}
