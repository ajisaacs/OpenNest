using System.Drawing;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class DrawingProgramSnapshotTests
{
    private const double QuarterTurn = System.Math.PI / 2;

    [Fact]
    public void EquivalentReplacement_KeepsPlacedProgramAndLeadInState()
    {
        var drawing = MakeDrawing("unchanged");
        var part = MakePart(drawing, withLeadIns: true);
        var program = part.Program;
        var parameters = part.CuttingParameters;
        var bounds = part.BoundingBox;
        var snapshot = DrawingProgramSnapshot.Capture(new[] { drawing }, Fingerprint);

        drawing.Program = (Program)drawing.Program.Clone();
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(part) });

        Assert.Empty(updated);
        Assert.Same(program, part.Program);
        Assert.Same(bounds, part.BoundingBox);
        Assert.Same(parameters, part.CuttingParameters);
        Assert.True(part.HasManualLeadIns);
        Assert.True(part.LeadInsLocked);
        Assert.Equal(QuarterTurn, part.Rotation, 6);
        Assert.Equal(new Vector(20, 30), part.Location);
    }

    [Fact]
    public void MetadataEdits_KeepProgramsAndLeadInsAcrossPlates()
    {
        var drawing = MakeDrawing("before rename");
        var first = MakePart(drawing, withLeadIns: true);
        var second = first.CloneAtOffset(new Vector(15, 0));
        var program = first.Program;
        var parameters = first.CuttingParameters;
        var snapshot = DrawingProgramSnapshot.Capture(new[] { drawing }, Fingerprint);

        drawing.Name = "after rename";
        drawing.Quantity.Required = 42;
        drawing.Color = Color.CornflowerBlue;
        drawing.Customer = "New customer";
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(first), MakePlate(second) });

        Assert.Empty(updated);
        foreach (var part in new[] { first, second })
        {
            Assert.Same(program, part.Program);
            Assert.Same(parameters, part.CuttingParameters);
            Assert.True(part.HasManualLeadIns);
            Assert.True(part.LeadInsLocked);
            Assert.Equal(QuarterTurn, part.Rotation, 6);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InPlaceEdit_UpdatesOnlyMatchingDrawingReferencesAndPreservesPose(bool withLeadIns)
    {
        // Drawing.Equals/GetHashCode use the editable name, not reference identity.
        var changedDrawing = MakeDrawing("same name");
        var unchangedDrawing = MakeDrawing("same name");
        var first = MakePart(changedDrawing, withLeadIns);
        var second = first.CloneAtOffset(new Vector(15, 5));
        var unchanged = MakePart(unchangedDrawing, withLeadIns: true);
        var unchangedProgram = unchanged.Program;
        var unchangedParameters = unchanged.CuttingParameters;
        var originalProgram = first.Program;
        var snapshot = DrawingProgramSnapshot.Capture(
            new[] { changedDrawing, unchangedDrawing }, Fingerprint);

        changedDrawing.Name = "renamed during edit";
        EnlargeRectangle(changedDrawing.Program);
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(first, unchanged), MakePlate(second) });

        Assert.Equal(new[] { first, second }, updated);
        Assert.NotSame(originalProgram, first.Program);
        Assert.NotSame(originalProgram, second.Program);
        AssertCleanAtCurrentPose(first, new Vector(20, 30), QuarterTurn);
        AssertCleanAtCurrentPose(second, new Vector(35, 35), QuarterTurn);
        Assert.Same(unchangedProgram, unchanged.Program);
        Assert.Same(unchangedParameters, unchanged.CuttingParameters);
        Assert.True(unchanged.HasManualLeadIns);
        Assert.True(unchanged.LeadInsLocked);
    }

    [Fact]
    public void HoleOnlyEdit_UpdatesPartWhenMainProgramTextIsUnchanged()
    {
        var drawing = MakeDrawing("hole edit", withHole: true);
        var part = MakePart(drawing, withLeadIns: true);
        var mainText = drawing.Program.ToString();
        var snapshot = DrawingProgramSnapshot.Capture(new[] { drawing }, Fingerprint);

        var hole = drawing.Program.SubPrograms[7];
        // The incremental circle's centre changes without replacing either program.
        var arc = Assert.IsType<ArcMove>(hole.Codes[1]);
        arc.CenterPoint = new Vector(-1.5, 0);
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(part) });

        Assert.Equal(mainText, drawing.Program.ToString());
        Assert.Same(part, Assert.Single(updated));
        AssertCleanAtCurrentPose(part, new Vector(20, 30), QuarterTurn);
    }

    [Fact]
    public void SharedDrawingProgram_InPlaceEditUpdatesBothDrawingsParts()
    {
        var firstDrawing = MakeDrawing("first");
        var secondDrawing = new Drawing("second", firstDrawing.Program);
        var first = MakePart(firstDrawing, withLeadIns: true);
        var second = MakePart(secondDrawing, withLeadIns: true);
        var snapshot = DrawingProgramSnapshot.Capture(new[] { firstDrawing, secondDrawing }, Fingerprint);

        EnlargeRectangle(firstDrawing.Program);
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(first, second) });

        Assert.Equal(new[] { first, second }, updated);
        AssertCleanAtCurrentPose(first, new Vector(20, 30), QuarterTurn);
        AssertCleanAtCurrentPose(second, new Vector(20, 30), QuarterTurn);
    }

    [Fact]
    public void CutOffAndUncapturedDrawings_KeepTheirPlacedPrograms()
    {
        var captured = MakeDrawing("same name");
        var uncaptured = MakeDrawing("same name");
        var cutOff = MakeDrawing("cutoff");
        cutOff.IsCutOff = true;
        var uncapturedPart = MakePart(uncaptured, withLeadIns: true);
        var cutOffPart = new Part(cutOff);
        var uncapturedProgram = uncapturedPart.Program;
        var cutOffProgram = cutOffPart.Program;
        var snapshot = DrawingProgramSnapshot.Capture(new[] { captured, cutOff }, Fingerprint);

        EnlargeRectangle(captured.Program);
        EnlargeRectangle(uncaptured.Program);
        EnlargeRectangle(cutOff.Program);
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(uncapturedPart, cutOffPart) });

        Assert.Empty(updated);
        Assert.Same(uncapturedProgram, uncapturedPart.Program);
        Assert.True(uncapturedPart.HasManualLeadIns);
        Assert.True(uncapturedPart.LeadInsLocked);
        Assert.Same(cutOffProgram, cutOffPart.Program);
    }

    [Fact]
    public void Capture_EagerlyFingerprintsEachDrawingOnceAndDoesNotRecheckPerPart()
    {
        var drawing = MakeDrawing("many parts");
        var first = MakePart(drawing, withLeadIns: false);
        var second = first.CloneAtOffset(new Vector(15, 0));
        var calls = 0;
        var snapshot = DrawingProgramSnapshot.Capture(new[] { drawing, drawing }, program =>
        {
            calls++;
            return Fingerprint(program);
        });
        Assert.Equal(1, calls);

        EnlargeRectangle(drawing.Program);
        var updated = snapshot.UpdateChangedParts(new[] { MakePlate(first, second) });

        Assert.Equal(2, calls);
        Assert.Equal(new[] { first, second }, updated);
    }

    private static Drawing MakeDrawing(string name, bool withHole = false)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 10)));
        program.Codes.Add(new LinearMove(new Vector(10, 10)));
        program.Codes.Add(new LinearMove(new Vector(10, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));

        if (withHole)
        {
            var hole = new Program();
            hole.Codes.Add(new RapidMove(new Vector(1, 0)));
            hole.Codes.Add(new ArcMove(new Vector(1, 0), Vector.Zero, RotationType.CW));
            hole.Mode = Mode.Incremental;
            program.SubPrograms[7] = hole;
            program.Codes.Add(new SubProgramCall { Id = 7, Program = hole, Offset = new Vector(5, 5) });
        }

        return new Drawing(name, program);
    }

    private static Part MakePart(Drawing drawing, bool withLeadIns)
    {
        var part = new Part(drawing);
        part.Rotate(QuarterTurn);
        part.Location = new Vector(20, 30);
        if (withLeadIns)
        {
            part.ApplyLeadIns(new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
                ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 },
            }, new Vector(-5, -5));
            part.LeadInsLocked = true;
            Assert.Contains(part.Program.Codes.OfType<LinearMove>(), move => move.Layer == LayerType.Leadin);
        }

        return part;
    }

    private static Plate MakePlate(params Part[] parts)
    {
        var plate = new Plate();
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static void EnlargeRectangle(Program program)
    {
        Assert.IsType<LinearMove>(program.Codes[2]).EndPoint = new Vector(12, 10);
        Assert.IsType<LinearMove>(program.Codes[3]).EndPoint = new Vector(12, 0);
    }

    // A test-only fingerprint keeps these Core tests independent of the IO writer.
    // Like the UI callback, it includes hole sub-program text as well as main text.
    private static string Fingerprint(Program program) => program.ToString() + "\0"
        + string.Join("\0", program.SubPrograms.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{Fingerprint(pair.Value)}"));

    private static void AssertCleanAtCurrentPose(Part part, Vector location, double rotation)
    {
        var expected = new Part(part.BaseDrawing);
        expected.Rotate(rotation);
        expected.Location = location;

        Assert.False(part.HasManualLeadIns);
        Assert.False(part.LeadInsLocked);
        Assert.Null(part.CuttingParameters);
        Assert.Equal(location, part.Location);
        Assert.Equal(rotation, part.Rotation, 6);
        Assert.Equal(Fingerprint(expected.Program), Fingerprint(part.Program));
        Assert.Equal(expected.BoundingBox.X, part.BoundingBox.X, 6);
        Assert.Equal(expected.BoundingBox.Y, part.BoundingBox.Y, 6);
        Assert.Equal(expected.BoundingBox.Length, part.BoundingBox.Length, 6);
        Assert.Equal(expected.BoundingBox.Width, part.BoundingBox.Width, 6);
    }
}
