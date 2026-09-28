using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>
/// Copying and rotating a part that already carries lead-ins must keep its true
/// rotation, lead-in state and geometry, so Remove Lead-ins (and saving) still
/// see the part the user placed.
/// </summary>
public class PartLeadInTransformTests
{
    private const double QuarterTurn = System.Math.PI / 2;

    /// <summary>10x10 square with a radius-1 hole at (5, 5), so lead-in
    /// assignment emits both a perimeter lead-in and a hole sub-program.</summary>
    private static Drawing MakeSquareWithHole()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        pgm.Codes.Add(new RapidMove(new Vector(6, 5)));
        pgm.Codes.Add(new ArcMove(new Vector(6, 5), new Vector(5, 5), RotationType.CW));
        return new Drawing("square-with-hole", pgm);
    }

    private static CuttingParameters Parameters() =>
        new()
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
            ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 },
        };

    private static Part MakeLeadInPart(Drawing drawing, double rotation)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Offset(20, 5);
        part.ApplyLeadIns(Parameters(), new Vector(-5, -5));
        return part;
    }

    /// <summary>Clean part of the drawing at the given rotation and location: what
    /// Remove Lead-ins must restore.</summary>
    private static Part MakeCleanPart(Drawing drawing, double rotation, Vector location)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Location = location;
        return part;
    }

    private static void AssertSameBox(Box expected, Box actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Length, actual.Length, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
    }

    private static int LeadInMoveCount(Program program) =>
        program.Codes.OfType<LinearMove>().Count(m => m.Layer == LayerType.Leadin)
        + program.Codes.OfType<ArcMove>().Count(m => m.Layer == LayerType.Leadin);

    [Fact]
    public void Clone_KeepsLeadInStateAndRotation()
    {
        var drawing = MakeSquareWithHole();
        var source = MakeLeadInPart(drawing, QuarterTurn);
        source.LeadInsLocked = true;

        var clone = (Part)source.Clone();

        Assert.True(clone.HasManualLeadIns);
        Assert.True(clone.LeadInsLocked);
        Assert.Same(source.CuttingParameters, clone.CuttingParameters);
        Assert.Equal(source.Rotation, clone.Rotation, 6);
        Assert.Equal(LeadInMoveCount(source.Program), LeadInMoveCount(clone.Program));
        AssertSameBox(source.BoundingBox, clone.BoundingBox);
    }

    [Fact]
    public void CloneAtOffset_KeepsLeadInStateAndRotation()
    {
        var drawing = MakeSquareWithHole();
        var source = MakeLeadInPart(drawing, QuarterTurn);

        var copy = source.CloneAtOffset(new Vector(3, 4));

        Assert.True(copy.HasManualLeadIns);
        Assert.Equal(source.Rotation, copy.Rotation, 6);
        Assert.Equal(source.Location.X + 3, copy.Location.X, 6);
        Assert.Equal(source.Location.Y + 4, copy.Location.Y, 6);
    }

    [Fact]
    public void Clone_ThenRemoveLeadIns_RestoresCleanRotatedPart()
    {
        var drawing = MakeSquareWithHole();
        var source = MakeLeadInPart(drawing, QuarterTurn);

        var clone = (Part)source.Clone();
        clone.RemoveLeadIns();

        var expected = MakeCleanPart(drawing, QuarterTurn, source.Location);
        Assert.False(clone.HasManualLeadIns);
        Assert.Equal(QuarterTurn, clone.Rotation, 6);
        AssertSameBox(expected.BoundingBox, clone.BoundingBox);
    }

    [Fact]
    public void Clone_DoesNotShareHoleSubPrograms()
    {
        var drawing = MakeSquareWithHole();
        var source = MakeLeadInPart(drawing, QuarterTurn);
        var sourceBox = source.Program.BoundingBox();

        var clone = (Part)source.Clone();
        clone.Rotate(QuarterTurn);

        // Rotating the copy must not rotate the source's hole lead-in.
        AssertSameBox(sourceBox, source.Program.BoundingBox());
        Assert.Equal(QuarterTurn, source.Rotation, 6);
    }

    [Fact]
    public void Rotate_LeadInPart_ReportsCumulativeRotation()
    {
        var drawing = MakeSquareWithHole();
        var part = MakeLeadInPart(drawing, QuarterTurn);

        part.Rotate(QuarterTurn);

        Assert.True(part.HasManualLeadIns);
        Assert.Equal(System.Math.PI, part.Rotation, 6);
    }

    [Fact]
    public void RotateAboutOrigin_LeadInPart_ReportsCumulativeRotation()
    {
        var drawing = MakeSquareWithHole();
        var part = MakeLeadInPart(drawing, QuarterTurn);

        part.Rotate(QuarterTurn, new Vector(50, 50));

        Assert.Equal(System.Math.PI, part.Rotation, 6);
    }

    [Fact]
    public void Rotate_LeadInPart_ThenRemove_RestoresCleanPartAtNewRotation()
    {
        var drawing = MakeSquareWithHole();
        var part = MakeLeadInPart(drawing, QuarterTurn);

        part.Rotate(QuarterTurn);
        var location = part.Location;
        part.RemoveLeadIns();

        var expected = MakeCleanPart(drawing, System.Math.PI, location);
        Assert.Equal(System.Math.PI, part.Rotation, 6);
        AssertSameBox(expected.BoundingBox, part.BoundingBox);
    }

    [Fact]
    public void Rotate_LeadInPart_RotatesHoleLeadInWithPart()
    {
        var drawing = MakeSquareWithHole();
        var part = MakeLeadInPart(drawing, 0);
        var before = part.Program.BoundingBox();

        part.Rotate(QuarterTurn);

        // A quarter turn about the program origin maps (x, y) to (-y, x): the lead-in
        // program's box, holes included, must turn with it rather than stay behind.
        var after = part.Program.BoundingBox();
        Assert.Equal(-(before.Y + before.Width), after.X, 6);
        Assert.Equal(before.X, after.Y, 6);
        Assert.Equal(before.Width, after.Length, 6);
        Assert.Equal(before.Length, after.Width, 6);
    }
}
