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
    public void Strip_WhoseTabsTieAlongTheFarEdge_IsTabbedAcrossItsCenterOfGravity()
    {
        // The row's own edge caps the margin at 0.25, so tabs from about x = 2.5 to 7.5 on the
        // far edge tie; the one across the center of gravity is chosen.
        var grid = new SupportGrid(2, 4, 0, 0.25, stabilityMargin: 0.1);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 10, 1));

        Assert.Equal(0.25, support.MarginWith(new Vector(3, 1)), 9);
        Assert.Equal(0.25, support.MarginWith(new Vector(7, 1)), 9);
        Assert.Equal(0.25, support.BestTabMargin, 9);
        Assert.Equal(1, support.BestTabPoint!.Value.Y, 9);
        Assert.InRange(support.BestTabPoint!.Value.X, 4.95, 5.05);
    }

    [Fact]
    public void Strip_RestingOnlyNearOneEnd_IsTabbedAtTheFarEndNotAcrossItsCenter()
    {
        // Two contacts at x = 1 hold a 10 x 2 strip whose center of gravity is at (5, 1); only a
        // tab at the far end reaches past it, so the tie-break does not pull it to mid-side.
        var grid = new SupportGrid(20, 1, 1, 0.5, stabilityMargin: 0.1);

        var support = SupportAnalyzer.Analyze(grid, 1, Rectangle(0, 0, 10, 2));

        Assert.Equal(-4, support.Margin, 9);
        Assert.True(support.TabCanSupport);
        Assert.Equal(10, support.BestTabPoint!.Value.X, 9);
        Assert.InRange(support.BestTabPoint!.Value.Y, 0.9, 1.1);
        Assert.True(support.MarginWith(new Vector(5, 2)) < 0);
        Assert.False(support.AcceptsTabAt(new Vector(5, 2)));
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

    // The 10 x 1 strip with a 2 x 0.2 bump on its top edge: inside corners at (4, 1) and (6, 1).
    private static SupportMaterial BumpedStrip() =>
        SupportMaterial.FromPolygons(new[]
        {
            new Vector(0, 0), new Vector(0, 1), new Vector(4, 1), new Vector(4, 1.2),
            new Vector(6, 1.2), new Vector(6, 1), new Vector(10, 1), new Vector(10, 0),
        });

    [Fact]
    public void BumpedStrip_EquallyGoodTabPoints_KeepTheTabOutOfTheInsideCorners()
    {
        // Along the top edge the margin is held by the contact row, so the inside corners tie
        // with the edge around them and, as vertices, used to be found first.
        var grid = new SupportGrid(2, 4, 0, 0.25, stabilityMargin: 0.1);

        var support = SupportAnalyzer.Analyze(grid, 1, BumpedStrip(), insideCornerClearance: 0.25);

        Assert.Equal(new[] { new Vector(4, 1), new Vector(6, 1) }, support.InsideCorners.OrderBy(c => c.X));
        Assert.True(support.TabCanSupport);
        Assert.Equal(support.MarginWith(new Vector(4, 1)), support.BestTabMargin, 9);
        var best = support.BestTabPoint!.Value;
        Assert.True(best.DistanceTo(new Vector(4, 1)) > 0.5 && best.DistanceTo(new Vector(6, 1)) > 0.5, best.ToString());
        Assert.False(support.AcceptsTabAt(new Vector(4, 1)));
        Assert.False(support.AcceptsTabAt(new Vector(6.2, 1)));
        Assert.True(support.AcceptsTabAt(new Vector(6.3, 1)));
    }

    [Fact]
    public void InsideCorners_CountAChordedFilletByItsWholeTurnAndIgnoreShallowBends()
    {
        // An L whose inside corner is a quarter-round fillet chorded into 6 small turns, and a
        // 10 degree inward bend along its bottom edge.
        var outline = new List<Vector> { new(0, 0), new(5, 0.4), new(10, 0), new(10, 2) };
        for (var i = 0; i <= 6; i++)
        {
            var angle = -System.Math.PI / 2 - i * System.Math.PI / 12;
            outline.Add(new Vector(3 + System.Math.Cos(angle), 3 + System.Math.Sin(angle)));
        }
        outline.Add(new Vector(2, 10));
        outline.Add(new Vector(0, 10));

        var corners = SupportAnalyzer.InsideCorners(outline);

        Assert.Equal(7, corners.Length);
        Assert.All(corners, c => Assert.Equal(1, c.DistanceTo(new Vector(3, 3)), 9));
        Assert.Equal(corners, SupportAnalyzer.InsideCorners(Enumerable.Reverse(outline).ToList()).OrderBy(outline.IndexOf));
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
