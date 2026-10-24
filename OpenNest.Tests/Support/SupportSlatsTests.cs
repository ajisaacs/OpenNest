using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Tests.Support;

/// <summary>
/// Whether a cut part stays level on a slat bed: each slat a strip of its thickness running the
/// table's full length or width, and a part's contacts the corners of where its material crosses one.
/// </summary>
public class SupportSlatsTests
{
    private static SupportMaterial Rectangle(double x1, double y1, double x2, double y2) =>
        SupportMaterial.FromPolygons(
            new[] { new Vector(x1, y1), new Vector(x1, y2), new Vector(x2, y2), new Vector(x2, y1) });

    [Fact]
    public void Square_AcrossTwoSlats_RestsOnTheSlatsOuterEdges()
    {
        var slats = new SupportSlats(SlatAxis.Y, pitch: 2, thickness: 0.125, offset: 1);

        var support = SupportAnalyzer.Analyze(slats, 1, Rectangle(0, 0, 4, 4));

        Assert.True(support.IsComplete);
        Assert.Equal(8, support.Contacts.Count);
        Assert.Contains(new Vector(0.9375, 0), support.Contacts);
        Assert.Contains(new Vector(3.0625, 4), support.Contacts);
        Assert.Equal(1.0625, support.Margin, 12);
        Assert.True(support.IsSupported);
    }

    [Fact]
    public void Strip_AcrossOneSlat_BalancesOnItsThicknessButTipsUnderTheStabilityMargin()
    {
        var slats = new SupportSlats(SlatAxis.Y, pitch: 20, thickness: 0.125, offset: 5, stabilityMargin: 0.25);

        var support = SupportAnalyzer.Analyze(slats, 1, Rectangle(0, 0, 10, 1));

        Assert.Equal(4, support.Contacts.Count);
        Assert.Equal(0.0625, support.Margin, 12);
        Assert.False(support.IsSupported);
    }

    [Fact]
    public void Strip_BetweenSlatsAlongItsLength_FallsThrough()
    {
        var slats = new SupportSlats(SlatAxis.X, pitch: 2, thickness: 0.125, offset: 1);

        var support = SupportAnalyzer.Analyze(slats, 1, Rectangle(0, 1.25, 10, 1.75));

        Assert.Empty(support.Contacts);
        Assert.Equal(double.NegativeInfinity, support.Margin);
    }

    [Fact]
    public void Part_NarrowerThanASlat_RestsOnItsOwnCorners()
    {
        var slats = new SupportSlats(SlatAxis.X, pitch: 4, thickness: 0.5, offset: 1);

        var support = SupportAnalyzer.Analyze(slats, 1, Rectangle(0, 0.8, 3, 1.2));

        Assert.Equal(4, support.Contacts.Distinct().Count());
        Assert.Contains(new Vector(0, 0.8), support.Contacts);
        Assert.Contains(new Vector(3, 1.2), support.Contacts);
        Assert.Equal(0.2, support.Margin, 12);
    }

    [Fact]
    public void Vertex_RestingOnASlat_IsAContact()
    {
        var slats = new SupportSlats(SlatAxis.X, pitch: 10, thickness: 0.125, offset: 0.05);
        var triangle = SupportMaterial.FromPolygons(new[] { new Vector(0, 0), new Vector(4, 5), new Vector(8, 0) });

        var contacts = slats.Contacts(triangle, 1, Vector.Zero);

        Assert.Contains(new Vector(0, 0), contacts);
        Assert.Contains(new Vector(8, 0), contacts);
        // The bottom edge lies on the slat, so only the slat's top edge crosses the outline.
        Assert.Contains(contacts, c => System.Math.Abs(c.Y - 0.1125) < 1e-12);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, -1)]
    public void Quadrant_MeasuresTheSlatsFromTheOriginIntoTheSheet(int quadrant, double sign)
    {
        var slats = new SupportSlats(SlatAxis.X, pitch: 2, thickness: 0.125, offset: 0.5);
        var sheet = new Box(sign < 0 ? -10 : 0, sign < 0 ? -4 : 0, 10, 4);

        var strips = slats.SlatsIn(sheet, quadrant);

        Assert.Equal(2, strips.Count);
        Assert.Contains(strips, s => System.Math.Abs(s.Center.Y - sign * 0.5) < 1e-12);
        Assert.All(strips, s => Assert.Equal(10, s.Length, 12));
        Assert.All(strips, s => Assert.Equal(0.125, s.Width, 12));
    }

    [Fact]
    public void SlatsIn_CutsTheEdgeSlatsOffToTheSheet()
    {
        var slats = new SupportSlats(SlatAxis.Y, pitch: 4, thickness: 0.5, offset: 0);

        var strips = slats.SlatsIn(new Box(0, 0, 8, 3), 1);

        Assert.Equal(3, strips.Count);
        Assert.Equal(0.25, strips[0].Length, 12);
        Assert.Equal(0.5, strips[1].Length, 12);
        Assert.Equal(0.25, strips[2].Length, 12);
        Assert.Equal(3, strips[0].Width, 12);
    }

    [Fact]
    public void ScaledAndEqual_CompareEveryLength()
    {
        var slats = new SupportSlats(SlatAxis.Y, 4, 0.125, 1, 0.125, 0.25);

        var scaled = Assert.IsType<SupportSlats>(slats.Scaled(2));
        Assert.Equal(new SupportSlats(SlatAxis.Y, 8, 0.25, 2, 0.25, 0.5), scaled);
        Assert.Equal(slats, new SupportSlats(SlatAxis.Y, 4, 0.125, 1, 0.125, 0.25));
        Assert.NotEqual<SupportBed>(slats, new SupportSlats(SlatAxis.X, 4, 0.125, 1, 0.125, 0.25));
        Assert.NotEqual<SupportBed>(slats, new SupportGrid(4, 4, 1, 1, 0.125, 0.25));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportSlats(SlatAxis.Y, 0, 0.125));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportSlats(SlatAxis.Y, 4, -0.125));
    }
}
