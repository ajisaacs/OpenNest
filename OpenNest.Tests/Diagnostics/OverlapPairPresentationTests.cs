using System.Globalization;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class OverlapPairPresentationTests
{
    [Theory]
    [InlineData(Units.Inches, "in")]
    [InlineData(Units.Millimeters, "mm")]
    public void DetailsUseCapturedNamesSequenceIdsAndSuppliedUnits(Units units, string symbol)
    {
        var parts = new[] { Rectangle("cutoff"), Rectangle("left"), Rectangle("right") };
        parts[0].BaseDrawing.IsCutOff = true;
        var snapshot = PlateOverlapAnalyzer.Capture(parts);
        parts[1].BaseDrawing.Name = "changed";
        var pair = Assert.Single(PlateOverlapAnalyzer.Analyze(snapshot).Pairs);
        Assert.Equal("2/3", OverlapPairPresentation.Label(pair));
        var text = OverlapPairPresentation.Details(pair, units, CultureInfo.InvariantCulture);
        Assert.Equal($"Pair 2/3: left / right\nShared area ≈ 16 {symbol}²\nCentroid: (2, 2) {symbol}", text);
    }

    [Theory]
    [InlineData(1e-15)]
    [InlineData(1e-100)]
    [InlineData(double.Epsilon)]
    [InlineData(0.000000123456)]
    public void PositiveTinyValuesNeverFormatAsZero(double value)
    {
        var text = OverlapPairPresentation.Number(value, CultureInfo.InvariantCulture);
        Assert.True(double.Parse(text, CultureInfo.InvariantCulture) > 0, text);
    }

    [Theory]
    [InlineData(96, 1)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2)]
    public void HitRadiusIsScreenSizedDpiScaledAndInclusive(int dpi, double factor)
    {
        Assert.Equal(6 * factor, OverlapPairPresentation.MarkerHalfSize(dpi));
        Assert.Equal(10 * factor, OverlapPairPresentation.HitRadius(dpi));
        var pairs = PlateOverlapAnalyzer.Analyze(new[] { Rectangle("a"), Rectangle("b") }).Pairs;
        foreach (var zoom in new[] { 0.5, 20.0, 500.0 })
        {
            Vector ToScreen(Vector world) => new(100 + world.X * zoom, 200 - world.Y * zoom);
            var center = ToScreen(pairs[0].Centroid);
            var radius = OverlapPairPresentation.HitRadius(dpi);
            Assert.Single(OverlapPairPresentation.HitTest(pairs, ToScreen,
                center + new Vector(radius, 0), dpi));
            Assert.Empty(OverlapPairPresentation.HitTest(pairs, ToScreen,
                center + new Vector(radius + 0.01, 0), dpi));
            Assert.Empty(OverlapPairPresentation.HitTest(pairs, ToScreen,
                center + new Vector(radius, radius), dpi));
        }
    }

    [Fact]
    public void CoincidentMarkersReturnEveryPairInIdOrderNotFragmentOrder()
    {
        var pairs = PlateOverlapAnalyzer.Analyze(new[] { Rectangle("a"), Rectangle("b"), Rectangle("c") }).Pairs;
        var hits = OverlapPairPresentation.HitTest(pairs.Reverse(), p => p, new Vector(2, 2), 96);
        Assert.Equal(new[] { "1/2", "1/3", "2/3" }, hits.Select(OverlapPairPresentation.Label));
        Assert.All(hits, p => Assert.True(p.Regions.Count > 1));
    }

    private static Part Rectangle(string name)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing(name, program));
    }
}
