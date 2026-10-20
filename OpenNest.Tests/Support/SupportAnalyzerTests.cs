using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Tests.Support;

/// <summary>
/// Whether a cut part stays level on the laser support grid: its center of gravity against the
/// convex hull of the grid points under its material, and where one tab steadies it best.
/// </summary>
public class SupportAnalyzerTests
{
    private static SupportMaterial Rectangle(double x1, double y1, double x2, double y2, params Vector[][] holes) =>
        SupportMaterial.FromPolygons(
            new[] { new Vector(x1, y1), new Vector(x1, y2), new Vector(x2, y2), new Vector(x2, y1) }, holes);

    private static Vector[] Square(double x1, double y1, double x2, double y2) =>
        new[] { new Vector(x1, y1), new Vector(x2, y1), new Vector(x2, y2), new Vector(x1, y2) };

    [Fact]
    public void Square_OverManyPoints_IsSupportedByItsDistanceToTheOutermostContacts()
    {
        var grid = new SupportGrid(1, 1, 0.5, 0.5);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 4, 4));

        Assert.True(support.IsComplete);
        Assert.Equal(new Vector(2, 2), support.CenterOfGravity);
        Assert.Equal(16, support.Contacts.Count);
        Assert.Equal(1.5, support.Margin, 12);
        Assert.True(support.IsSupported);
    }

    [Fact]
    public void Holes_HaveNoContactsAndMoveTheCenterOfGravity()
    {
        var grid = new SupportGrid(1, 1, 0.5, 0.5);

        var centered = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 4, 4, Square(1, 1, 3, 3)));
        var offCenter = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 4, 4, Square(0.2, 0.2, 1.8, 1.8)));

        Assert.Equal(12, centered.Contacts.Count);
        Assert.DoesNotContain(centered.Contacts, c => c.X is > 1 and < 3 && c.Y is > 1 and < 3);
        Assert.Equal(new Vector(2, 2), centered.CenterOfGravity);
        Assert.True(offCenter.CenterOfGravity.X > 2 && offCenter.CenterOfGravity.Y > 2);
    }

    [Fact]
    public void RegistrationTolerance_KeepsTheWorstShiftedMargin()
    {
        var grid = new SupportGrid(1, 1, 0.5, 0.5, registrationTolerance: 0.25);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 4, 4));

        Assert.Equal(1.25, support.Margin, 12);
    }

    [Fact]
    public void Strip_OnOneRowOfContacts_TipsAndATabOnTheFarEdgeSteadiesIt()
    {
        // One row of contacts at y = 0.25 runs under a 10 x 1 strip whose center is at y = 0.5.
        var grid = new SupportGrid(2, 4, 0, 0.25, stabilityMargin: 0.1);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 10, 1));

        Assert.False(support.IsSupported);
        Assert.Equal(-0.25, support.Margin, 12);
        Assert.True(support.TabCanSupport);
        Assert.Equal(0.25, support.BestTabMargin, 9);
        Assert.Equal(1, support.BestTabPoint!.Value.Y, 9);
        Assert.True(support.AcceptsTabAt(new Vector(5, 1)));
        Assert.False(support.AcceptsTabAt(new Vector(5, 0)));
        Assert.True(support.MarginWith(new Vector(5, 0)) < 0);
    }

    [Fact]
    public void Strip_BetweenRows_FallsThroughAndOnlyTheNearestTabIsAccepted()
    {
        var grid = new SupportGrid(1, 1, 0, 0.9, stabilityMargin: 0.1);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 10, 0.8));

        Assert.Empty(support.Contacts);
        Assert.Equal(double.NegativeInfinity, support.Margin);
        Assert.False(support.TabCanSupport);
        // The outline is sampled, so the nearest sample may sit just beside (5, 0) or (5, 0.8).
        Assert.InRange(support.BestTabMargin, -0.41, -0.4);
        Assert.True(support.AcceptsTabAt(support.BestTabPoint!.Value));
        Assert.False(support.AcceptsTabAt(new Vector(0, 0)));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, -1, 1)]
    [InlineData(3, -1, -1)]
    [InlineData(4, 1, -1)]
    public void Quadrant_MeasuresTheGridFromTheOriginIntoTheSheet(int quadrant, double signX, double signY)
    {
        var grid = new SupportGrid(1, 1, 0.25, 0.75);
        var sheet = new Box(signX < 0 ? -4 : 0, signY < 0 ? -4 : 0, 4, 4);

        var points = grid.PointsIn(sheet, quadrant);

        Assert.Equal(16, points.Count);
        Assert.Contains(new Vector(signX * 0.25, signY * 0.75), points);
        Assert.Contains(new Vector(signX * 3.25, signY * 3.75), points);
    }

    [Fact]
    public void ProgramMaterial_IsPlacedAtItsLocationWithoutLeads()
    {
        var program = new Program();
        program.MoveTo(-1, 2);
        program.Codes.Add(new LinearMove(0, 2) { Layer = LayerType.Leadin });
        program.LineTo(0, 4); program.LineTo(4, 4); program.LineTo(4, 0); program.LineTo(0, 0); program.LineTo(0, 2);

        var material = SupportMaterial.FromProgram(program, new Vector(10, 20));

        Assert.True(material.IsComplete, material.Reason);
        Assert.True(material.TryGetCenterOfGravity(out var center, out var area));
        Assert.Equal(16, area, 9);
        Assert.Equal(12, center.X, 9);
        Assert.Equal(22, center.Y, 9);
    }

    [Fact]
    public void Grid_RejectsNonPositivePitch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportGrid(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportGrid(1, 1, registrationTolerance: -1));
        Assert.Equal(new SupportGrid(2, 2, 1, 1, 0.5, 0.25), new SupportGrid(1, 1, 0.5, 0.5, 0.25, 0.125).Scaled(2));
    }
}
