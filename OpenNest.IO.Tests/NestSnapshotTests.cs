using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

public class NestSnapshotTests
{
    [Fact]
    public void Restore_KeepsEmptyPlatesInPlace_AndEveryPlacement()
    {
        var live = Fixture(out var parameters);

        var restored = NestSnapshot.Capture(live).Restore(live);

        Assert.Equal(live.Plates.Count, restored.Plates.Count);
        Assert.Equal(new[] { 3, 0, 1, 0 }, restored.Plates.Select(p => p.Parts.Count(x => !x.BaseDrawing.IsCutOff)));
        for (var i = 0; i < live.Plates.Count; i++)
        {
            var expected = live.Plates[i];
            var actual = restored.Plates[i];
            Assert.Equal(expected.Size.Width, actual.Size.Width);
            Assert.Equal(expected.Size.Length, actual.Size.Length);
            Assert.Equal(expected.CutOffs.Count, actual.CutOffs.Count);
            Assert.Equal(expected.Parts.Count, actual.Parts.Count);
            for (var k = 0; k < expected.Parts.Count; k++)
            {
                Assert.Equal(expected.Parts[k].Location.X, actual.Parts[k].Location.X, 9);
                Assert.Equal(expected.Parts[k].Location.Y, actual.Parts[k].Location.Y, 9);
                Assert.Equal(expected.Parts[k].Rotation, actual.Parts[k].Rotation, 9);
                Assert.Equal(expected.Parts[k].HasManualLeadIns, actual.Parts[k].HasManualLeadIns);
                Assert.Equal(expected.Parts[k].LeadInsLocked, actual.Parts[k].LeadInsLocked);
            }
        }

        var leaded = restored.Plates[0].Parts[0];
        Assert.Equal(CuttingParametersSerializer.Serialize(parameters),
            CuttingParametersSerializer.Serialize(leaded.CuttingParameters));
        Assert.Null(restored.Plates[0].Parts[1].CuttingParameters);
    }

    [Fact]
    public void Files_StillOmitEmptyPlates()
    {
        var live = Fixture(out _);
        using var stream = new MemoryStream();

        new NestWriter(live).Write(stream);
        var read = new NestReader(new MemoryStream(stream.ToArray())).Read();

        Assert.Equal(2, read.Plates.Count);
    }

    [Fact]
    public void Capture_LeavesTheModifiedDateAlone()
    {
        var live = Fixture(out _);
        var date = new DateTime(2024, 3, 4, 5, 6, 7);
        live.DateLastModified = date;

        var snapshot = NestSnapshot.Capture(live);

        Assert.Equal(date, live.DateLastModified);
        Assert.Equal(date, snapshot.Restore(live).DateLastModified);
    }

    [Fact]
    public void Restore_ReusesUnchangedDrawings_AndReplacesChangedOnes()
    {
        var live = Fixture(out _);
        var square = live.Drawings.ElementAt(0);
        var bar = live.Drawings.ElementAt(1);
        var snapshot = NestSnapshot.Capture(live);
        bar.Quantity.Required = 99; // Changed since the copy: the copy's bar comes back.

        var restored = snapshot.Restore(live);

        Assert.Same(square, restored.Drawings.ElementAt(0));
        Assert.NotSame(bar, restored.Drawings.ElementAt(1));
        Assert.Equal(5, restored.Drawings.ElementAt(1).Quantity.Required);
        var drawings = restored.Drawings.ToArray();
        Assert.All(restored.Plates.SelectMany(p => p.Parts).Where(p => !p.BaseDrawing.IsCutOff),
            part => Assert.Contains(drawings, d => ReferenceEquals(d, part.BaseDrawing)));
        Assert.Equal(2, square.Quantity.Nested); // Counted once, from the restored nest.
        Assert.Equal(2, drawings[1].Quantity.Nested);
    }

    [Fact]
    public void Restore_ReplacesADrawingWhoseProgramChanged()
    {
        var live = Fixture(out _);
        var square = live.Drawings.ElementAt(0);
        var snapshot = NestSnapshot.Capture(live);
        var before = NestWriter.GetProgramText(square.Program);
        square.Program = ConvertGeometry.ToProgram(new List<Entity>
        {
            new Line(0, 0, 5, 0),
            new Line(5, 0, 5, 5),
            new Line(5, 5, 0, 5),
            new Line(0, 5, 0, 0),
        });

        var restored = snapshot.Restore(live).Drawings.ElementAt(0);

        Assert.NotSame(square, restored);
        Assert.Equal(before, NestWriter.GetProgramText(restored.Program));
    }

    [Fact]
    public void Restore_WithoutALiveNest_ReadsFreshDrawings()
    {
        var live = Fixture(out _);

        var restored = NestSnapshot.Capture(live).Restore();

        Assert.DoesNotContain(restored.Drawings, d => live.Drawings.Any(l => ReferenceEquals(l, d)));
        Assert.Equal(live.Drawings.Select(d => d.Name), restored.Drawings.Select(d => d.Name));
    }

    [Fact]
    public void Restore_KeepsLeadInProgramsOnReusedDrawings()
    {
        var live = Fixture(out _);
        var expected = NestWriter.GetProgramText(live.Plates[0].Parts[0].Program);

        var restored = NestSnapshot.Capture(live).Restore(live);

        var part = restored.Plates[0].Parts[0];
        Assert.Same(live.Drawings.ElementAt(0), part.BaseDrawing);
        Assert.True(part.HasManualLeadIns);
        Assert.True(part.LeadInsLocked);
        Assert.Equal(expected, NestWriter.GetProgramText(part.Program));
    }

    // Plates: [3 parts, one with locked manual leads] [empty] [1 part + a cut-off] [empty].
    private static Nest Fixture(out CuttingParameters parameters)
    {
        var nest = new Nest("snapshot");
        nest.PlateDefaults.Size = new Size(40, 20);
        var square = Square();
        var bar = new Drawing("Bar", ConvertGeometry.ToProgram(new List<Entity>
        {
            new Line(0, 0, 8, 0),
            new Line(8, 0, 8, 2),
            new Line(8, 2, 0, 2),
            new Line(0, 2, 0, 0),
        }));
        bar.Quantity.Required = 5;
        nest.Drawings.Add(square);
        nest.Drawings.Add(bar);

        var first = nest.CreatePlate();
        parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
            InternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
            ExternalLeadOut = new NoLeadOut(),
            InternalLeadOut = new NoLeadOut(),
        };
        var leaded = new Part(square, new Vector(1, 1));
        leaded.ApplyLeadIns(parameters, Vector.Zero);
        leaded.LeadInsLocked = true;
        first.Parts.Add(leaded);
        first.Parts.Add(new Part(square, new Vector(6, 1)));
        var rotated = new Part(bar, new Vector(12, 1));
        rotated.Rotate(System.Math.PI / 2);
        first.Parts.Add(rotated);

        nest.CreatePlate();

        var third = nest.CreatePlate();
        third.Parts.Add(new Part(bar, new Vector(1, 1)));
        third.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        third.RegenerateCutOffs(new CutOffSettings());

        nest.CreatePlate();
        return nest;
    }

    private static Drawing Square() =>
        new("Square", ConvertGeometry.ToProgram(new List<Entity>
        {
            new Line(0, 0, 4, 0),
            new Line(4, 0, 4, 4),
            new Line(4, 4, 0, 4),
            new Line(0, 4, 0, 0),
        }));
}
