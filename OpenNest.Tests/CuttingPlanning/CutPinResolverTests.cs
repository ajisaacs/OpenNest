using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.CuttingPlanning;

public class CutPinResolverTests
{
    // The program frame is the source frame moved by -Offset.
    private static readonly Vector Offset = new(5, -2);

    /// <summary>A 10 x 6 plate with a radius-1 hole at (3, 3), program-local; pins go in source coordinates.</summary>
    private static Drawing Bracket(params Vector[] sourcePins)
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0),
            new Line(10, 0, 10, 6),
            new Line(10, 6, 0, 6),
            new Line(0, 6, 0, 0),
            new Circle(3, 3, 1),
        };
        var drawing = new Drawing("Synthetic bracket", ConvertGeometry.ToProgram(entities));
        drawing.Source.Offset = Offset;
        foreach (var point in sourcePins)
            drawing.CutPins.Add(Pin(point));
        return drawing;
    }

    private static ContourCutPin Pin(Vector sourcePoint) =>
        new(sourcePoint, new LineLeadIn { Length = 0.25, ApproachAngle = 90 }, new NoLeadOut(), new NormalTab { Size = 0.03 });

    private static Vector Source(double x, double y) => new Vector(x, y) + Offset;

    [Fact]
    public void NoPins_ResolveToNothing()
    {
        Assert.Empty(CutPinResolver.Resolve(Bracket(), 0));
    }

    [Fact]
    public void Pins_ResolveToThePerimeterAndHoleInTheProgramFrame()
    {
        var drawing = Bracket(Source(10, 6), Source(4, 3));

        var results = CutPinResolver.Resolve(drawing, 0);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(CutPinStatus.Resolved, r.Status));
        Assert.Equal((1, new Vector(10, 6)), (results[0].ContourOrdinal, results[0].Point));
        Assert.Equal((0, 0), (results[1].ContourOrdinal, results[1].EntityOrdinal));
        AssertNear(new Vector(4, 3), results[1].Point);
        Assert.Same(drawing.CutPins[1], results[1].Pin);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(37)]
    public void Pins_FollowTheRotationAboutTheProgramOrigin(double degrees)
    {
        var rotation = Angle.ToRadians(degrees);
        var drawing = Bracket(Source(10, 6));

        var result = Assert.Single(CutPinResolver.Resolve(drawing, rotation));

        Assert.True(result.IsResolved);
        AssertNear(new Vector(10 * System.Math.Cos(rotation) - 6 * System.Math.Sin(rotation),
            10 * System.Math.Sin(rotation) + 6 * System.Math.Cos(rotation)), result.Point);
    }

    [Fact]
    public void Resolutions_AreEntriesOfTheSameRotatedPreparation()
    {
        var rotation = Angle.ToRadians(37);
        var drawing = Bracket(Source(10, 3), Source(3, 4));
        var program = (Program)drawing.Program.Clone();
        program.Rotate(rotation);
        var contours = PreparedContours.Capture(program, ExplicitContourTests.Parameters());

        var results = CutPinResolver.Resolve(drawing, rotation, contours);

        Assert.All(results, r =>
        {
            Assert.True(r.IsResolved);
            var choice = contours.Entry(r.ContourOrdinal, r.EntityOrdinal, r.Point);
            Assert.Equal(r.Point, choice.Point);
        });
        Assert.Equal(contours.PerimeterOrdinal, results[0].ContourOrdinal);
    }

    [Fact]
    public void PinWithinClosureTolerance_SnapsOntoTheEntity()
    {
        var drawing = Bracket(Source(10, 3) + new Vector(5e-7, 0));

        var result = Assert.Single(CutPinResolver.Resolve(drawing, 0));

        Assert.True(result.IsResolved);
        Assert.Equal(new Vector(10, 3), result.Point);
    }

    [Fact]
    public void PinOffTheGeometry_IsNotOnAContourAndOthersStillResolve()
    {
        var drawing = Bracket(Source(10, 3) + new Vector(1e-5, 0), Source(3, 2));

        var results = CutPinResolver.Resolve(drawing, 0);

        Assert.Equal(CutPinStatus.NotOnContour, results[0].Status);
        Assert.Equal(-1, results[0].ContourOrdinal);
        AssertNear(new Vector(10.00001, 3), results[0].Point);
        Assert.True(results[1].IsResolved);
    }

    [Fact]
    public void TwoPinsOnOneContour_AreBothUnused()
    {
        var drawing = Bracket(Source(0, 0), Source(3, 4), Source(10, 6));

        var results = CutPinResolver.Resolve(drawing, 0);

        Assert.Equal(CutPinStatus.ContourPinnedTwice, results[0].Status);
        Assert.Equal(CutPinStatus.ContourPinnedTwice, results[2].Status);
        Assert.Equal(1, results[0].ContourOrdinal);
        Assert.True(results[1].IsResolved);
    }

    [Fact]
    public void PinTouchingTwoContours_IsAmbiguous()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
            new Circle(3, 3, 1), new Circle(5, 3, 1),
        };
        var drawing = new Drawing("Touching holes", ConvertGeometry.ToProgram(entities));
        drawing.CutPins.Add(Pin(new Vector(4, 3)));

        var result = Assert.Single(CutPinResolver.Resolve(drawing, 0));

        Assert.Equal(CutPinStatus.OnSeveralContours, result.Status);
    }

    [Fact]
    public void RotatedDrawingProgram_IsNotTheSourceFrame()
    {
        var drawing = Bracket(Source(10, 6));
        drawing.Program.Rotate(Angle.HalfPI);

        var result = Assert.Single(CutPinResolver.Resolve(drawing, 0));

        Assert.Equal(CutPinStatus.UnsupportedGeometry, result.Status);
        Assert.False(result.Point.IsValid());
    }

    [Fact]
    public void OpenGeometry_IsUnsupported()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 6);
        var drawing = new Drawing("Open", program);
        drawing.CutPins.Add(Pin(new Vector(10, 0)));

        Assert.Equal(CutPinStatus.UnsupportedGeometry, Assert.Single(CutPinResolver.Resolve(drawing, 0)).Status);
    }

    [Fact]
    public void Part_ResolvesAtItsRotation()
    {
        var drawing = Bracket(Source(10, 6));
        var part = new Part(drawing);
        part.Rotate(Angle.HalfPI);

        var result = Assert.Single(CutPinResolver.Resolve(part));

        AssertNear(new Vector(-6, 10), result.Point);
    }

    [Fact]
    public void CanonicalCopy_CarriesNoPins()
    {
        // A canonical copy's program is rotated about its center, so source-frame pins would
        // not map onto it; its parts are rebound to the original drawing, which keeps them.
        var drawing = Bracket(Source(10, 6));

        var copy = CanonicalFrame.AsCanonicalCopy(drawing);

        Assert.Empty(copy.CutPins);
        Assert.Single(drawing.CutPins);
    }

    [Fact]
    public void SplitPieces_CarryNoPins()
    {
        var drawing = Bracket(Source(10, 6));

        var pieces = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new(5.0, CutOffAxis.Vertical) },
            new SplitParameters { Type = SplitType.Straight });

        Assert.NotEmpty(pieces);
        Assert.All(pieces, piece => Assert.Empty(piece.CutPins));
    }

    private static void AssertNear(Vector expected, Vector actual)
    {
        Assert.True(expected.DistanceTo(actual) <= 1e-9, $"Expected {expected}, got {actual}.");
    }
}
