using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A band beyond the parts along Y is kept by a full-length horizontal separator: vertical strip
/// lines (and an X tail separator) stop at it instead of slicing the band.
/// </summary>
public class AutomaticCutOffHorizontalRemnantTests
{
    // A 48-wide, 96-long sheet with one part from X 0.5 through occupiedX and Y 1 through 21,
    // placed in the quadrant's frame.
    private static Plate MakePlate(int quadrant, double occupiedX = 95.5)
    {
        var plate = new Plate(48, 96) { Quadrant = quadrant, PartSpacing = 0.3 };
        var length = occupiedX - 0.5;
        var x = quadrant is 2 or 3 ? -occupiedX : 0.5;
        var y = quadrant is 3 or 4 ? -21 : 1;
        plate.Parts.Add(Rectangle(length, 20, new Vector(x, y)));
        return plate;
    }

    private static Part Rectangle(double length, double width, Vector location)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(length, 0));
        program.Codes.Add(new LinearMove(length, width));
        program.Codes.Add(new LinearMove(0, width));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("rectangle", program), location);
    }

    private static AutomaticCutOffPlan Plan(Plate plate, bool keepHorizontal = true, double minimumTail = 12,
        double? minimumStrip = null) =>
        AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions
        {
            Spacing = 35,
            MinimumTailLength = minimumTail,
            MinimumStripLength = minimumStrip,
            KeepHorizontalRemnant = keepHorizontal,
        }, new CutOffSettings());

    // The extent of every segment along the line; strip lines are trimmed around the part.
    private static (double Low, double High) Span(CutOff cut, bool vertical)
    {
        var along = cut.Drawing.Program.Codes.OfType<Motion>()
            .Select(m => vertical ? m.EndPoint.Y : m.EndPoint.X).ToList();
        Assert.NotEmpty(along);
        return (along.Min(), along.Max());
    }

    private static int Sign(int quadrant) => quadrant is 2 or 3 ? -1 : 1;
    private static int YSign(int quadrant) => quadrant is 3 or 4 ? -1 : 1;

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void ExistingHorizontalCutOff_StripLinesStopAtIt(int quadrant, bool keepHorizontal)
    {
        var plate = MakePlate(quadrant);
        var y = YSign(quadrant) * 23.5;
        plate.CutOffs.Add(new CutOff(new Vector(0, y), CutOffAxis.Horizontal));
        var bounds = plate.BoundingBox(false);

        var plan = Plan(plate, keepHorizontal);

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(new[] { 35.0, 70.0 }.Select(x => Sign(quadrant) * x), plan.Definitions.Select(c => c.Position.X));
        Assert.All(plan.Definitions, c => Assert.Equal(CutOffAxis.Vertical, c.Axis));
        var expected = YSign(quadrant) > 0 ? (bounds.Bottom, y) : (y, bounds.Top);
        Assert.All(plan.Definitions, c => Assert.Equal(expected, Span(c, true)));
        Assert.Equal(y, plan.HorizontalSeparatorY);
        Assert.Equal(24.5, plan.HorizontalRemnantWidth, 10);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.HorizontalRemnant);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NewHorizontalSeparator_KeepsTheBand_AndRerunAddsNothing(int quadrant)
    {
        var plate = MakePlate(quadrant);
        var settings = new CutOffSettings();
        var y = YSign(quadrant) * (21 + 0.3 + Tolerance.Epsilon);
        var bounds = plate.BoundingBox(false);

        var plan = Plan(plate);

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(y, plan.HorizontalSeparatorY);
        Assert.False(plan.HasSeparatedTail);
        var separator = plan.Definitions[^1];
        Assert.Equal(CutOffAxis.Horizontal, separator.Axis);
        Assert.Equal(y, separator.Position.Y);
        Assert.Equal((bounds.Left, bounds.Right), Span(separator, false));
        var strips = plan.Definitions.Take(plan.Definitions.Count - 1).ToList();
        Assert.Equal(new[] { 35.0, 70.0 }.Select(x => Sign(quadrant) * x), strips.Select(c => c.Position.X));
        var expected = YSign(quadrant) > 0 ? (bounds.Bottom, y) : (y, bounds.Top);
        Assert.All(strips, c => Assert.Equal(expected, Span(c, true)));

        plate.CutOffs.AddRange(plan.Definitions);
        plate.RegenerateCutOffs(settings);
        var rerun = Plan(plate);
        Assert.Empty(rerun.Definitions);
        Assert.False(rerun.HasBlockingDiagnostics);
        Assert.Equal(y, rerun.HorizontalSeparatorY);
        Assert.Equal(2, rerun.Diagnostics.Count(d => d.Code == AutomaticCutOffDiagnosticCode.ExistingCutOff));
    }

    [Fact]
    public void ShiftedStripLines_StopAtTheHorizontalSeparator()
    {
        var plate = MakePlate(1);
        var plan = Plan(plate, minimumStrip: 17.5);

        Assert.NotNull(plan.HorizontalSeparatorY);
        var strips = plan.Definitions.Where(c => c.Axis == CutOffAxis.Vertical).ToList();
        Assert.Equal(2, strips.Count);
        Assert.All(strips, c => Assert.Equal(plan.HorizontalSeparatorY, c.EndLimit));
        Assert.All(strips, c => Assert.Null(c.StartLimit));
    }

    [Fact]
    public void OptionOff_WithoutAnExistingLine_KeepsFullWidthStrips()
    {
        var plate = MakePlate(1);

        var plan = Plan(plate, keepHorizontal: false);

        Assert.Null(plan.HorizontalSeparatorY);
        Assert.Equal(new[] { 35.0, 70.0 }, plan.Definitions.Select(c => c.Position.X));
        Assert.All(plan.Definitions, c => Assert.Equal((0.0, 48.0), Span(c, true)));
    }

    [Fact]
    public void LargerXTail_WinsOverANarrowBand()
    {
        // Parts reach Y 41: the 6.7-wide band is smaller than the 65-long tail.
        var plate = new Plate(48, 96) { PartSpacing = 0.3 };
        plate.Parts.Add(Rectangle(30, 40, new Vector(0.5, 1)));

        var plan = Plan(plate);

        Assert.Null(plan.HorizontalSeparatorY);
        Assert.True(plan.HasSeparatedTail);
        Assert.All(plan.Definitions, c => Assert.Equal((0.0, 48.0), Span(c, true)));
    }

    [Fact]
    public void ExistingLineThroughParts_IsNotABand()
    {
        var plate = MakePlate(1);
        plate.CutOffs.Add(new CutOff(new Vector(0, 10), CutOffAxis.Horizontal));

        var plan = Plan(plate, keepHorizontal: false);

        Assert.Null(plan.HorizontalSeparatorY);
        Assert.All(plan.Definitions, c => Assert.Equal(48.0, Span(c, true).High));
    }

    [Fact]
    public void BandBelowTheMinimumTail_IsNotKept()
    {
        var plate = MakePlate(1);
        plate.CutOffs.Add(new CutOff(new Vector(0, 40), CutOffAxis.Horizontal));

        var plan = Plan(plate, keepHorizontal: false);

        Assert.Null(plan.HorizontalSeparatorY);
        Assert.All(plan.Definitions, c => Assert.Equal(48.0, Span(c, true).High));
    }

    [Fact]
    public void BesideTheBand_TheXTailSeparatorAlsoStopsAtIt()
    {
        // Parts through X 65: the 26.7-wide band beats the 30.7-long tail, and the corner
        // piece (30.7 by 21.3) is kept by a separator that stops at the band.
        var plate = MakePlate(1, occupiedX: 65);
        var y = 21 + 0.3 + Tolerance.Epsilon;
        var x = 65 + 0.3 + Tolerance.Epsilon;

        var plan = Plan(plate);

        Assert.Equal(y, plan.HorizontalSeparatorY);
        Assert.Equal(x, plan.TailSeparatorX);
        Assert.Equal(new[] { 35.0, x }, plan.Definitions.Where(c => c.Axis == CutOffAxis.Vertical).Select(c => c.Position.X));
        Assert.All(plan.Definitions.Where(c => c.Axis == CutOffAxis.Vertical), c => Assert.Equal((0.0, y), Span(c, true)));
    }

    [Fact]
    public void ClampOnTheProposedLine_FallsBackToFullWidthStrips()
    {
        var plate = MakePlate(1);
        plate.ClampZones.Add(new ClampZone("far", new Box(40, 20, 5, 5)));

        var plan = Plan(plate);

        Assert.Null(plan.HorizontalSeparatorY);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.NoSafeHorizontalSeparator);
        Assert.All(plan.Definitions, c => Assert.Equal(CutOffAxis.Vertical, c.Axis));
    }
}
