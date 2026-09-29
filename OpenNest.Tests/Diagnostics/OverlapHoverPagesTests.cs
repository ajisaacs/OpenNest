using System.Globalization;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class OverlapHoverPagesTests
{
    [Fact]
    public void CrowdedCoincidentPairsRemainAccessibleInOrderWithinEveryPageBudget()
    {
        var parts = Enumerable.Range(1, 12).Select(i => Rectangle($"part-{i}")).ToArray();
        var report = PlateOverlapAnalyzer.Analyze(parts);
        var pairs = OverlapPairPresentation.HitTest(report.Pairs.Reverse(), p => p, new Vector(2, 2), 96);
        var text = string.Join("\n\n", pairs.Select(p => OverlapPairPresentation.Details(p, Units.Millimeters, CultureInfo.InvariantCulture)));
        var pages = OverlapHoverPages.Create(text, pairs.Count, 10, 45, s => s.Length);

        Assert.True(pages.PageCount > 1);
        Assert.False(pages.NeedsLargerViewport);
        var displayed = ReadAll(pages, 10, 45);
        Assert.Equal(text.Replace("\n", ""), displayed);
        Assert.Contains("Pair 11/12: part-11 / part-12", displayed);
        Assert.Contains("Shared area ≈ 16 mm²", displayed);
        Assert.Contains("Centroid: (2, 2) mm", displayed);
        Assert.Equal(pages.PageCount - 1, pages.PageIndex);
        Assert.Contains($"Page {pages.PageCount}/{pages.PageCount}", string.Join("", pages.NavigationLines));
        Assert.Contains("66 pairs", string.Join("", pages.NavigationLines));
        Assert.Contains("PgUp/PgDn", string.Join("", pages.NavigationLines));
    }

    [Theory]
    [InlineData(8, 12)]
    [InlineData(4, 40)]
    public void OneDetailWithLongUnbrokenNamesPagesWithoutDroppingText(int rows, int width)
    {
        var name = string.Concat(Enumerable.Repeat("veryLongName😀e\u0301", 30));
        var pair = Assert.Single(PlateOverlapAnalyzer.Analyze(new[] { Rectangle(name), Rectangle("tail") }).Pairs);
        var text = OverlapPairPresentation.Details(pair, Units.Inches, CultureInfo.InvariantCulture);
        var pages = OverlapHoverPages.Create(text, 1, rows, width, s => new StringInfo(s).LengthInTextElements);

        Assert.True(pages.PageCount > 1);
        Assert.False(pages.NeedsLargerViewport);
        Assert.Equal(text.Replace("\n", ""), ReadAll(pages, rows, width, s => new StringInfo(s).LengthInTextElements));
        Assert.All(AllLines(text, rows, width), line =>
        {
            Assert.False(line.Length > 0 && char.IsLowSurrogate(line[0]));
            Assert.False(line.StartsWith("\u0301"));
        });
    }

    [Fact]
    public void NavigationClampsAtBothEndsAndNewHoverStartsAtFirstPage()
    {
        const string text = "one\ntwo\nthree\nfour\nfive\nsix";
        var pages = OverlapHoverPages.Create(text, 2, 4, 40, s => s.Length);
        Assert.True(pages.PageCount > 1);
        pages.MovePage(int.MaxValue);
        Assert.Equal(pages.PageCount - 1, pages.PageIndex);
        pages.MovePage(1);
        Assert.Equal(pages.PageCount - 1, pages.PageIndex);
        pages.MovePage(int.MinValue);
        Assert.Equal(0, pages.PageIndex);
        pages.MovePage(-1);
        Assert.Equal(0, pages.PageIndex);
        pages.MovePage(1);
        Assert.Equal(0, OverlapHoverPages.Create(text, 2, 4, 40, s => s.Length).PageIndex);
    }

    [Fact]
    public void FittingDetailsDoNotSpendSpaceOnNavigation()
    {
        var pages = OverlapHoverPages.Create("Pair 1/2\narea\ncentroid", 1, 3, 40, s => s.Length);
        Assert.Equal(1, pages.PageCount);
        Assert.Empty(pages.NavigationLines);
        Assert.Equal(new[] { "Pair 1/2", "area", "centroid" }, pages.Lines);
        pages.MovePage(1);
        Assert.Equal(0, pages.PageIndex);
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(3, 0)]
    [InlineData(1, 5)]
    [InlineData(4, 0.5)]
    public void ImpossibleViewportExplicitlyRequestsMoreSpaceInsteadOfDroppingDetails(int rows, double width)
    {
        var pages = OverlapHoverPages.Create("Pair 1/2\narea\ncentroid", 1, rows, width, s => s.Length);
        Assert.True(pages.NeedsLargerViewport);
        Assert.Empty(pages.Lines);
        Assert.Equal(0, pages.PageCount);
    }

    private static string ReadAll(OverlapHoverPages pages, int rows, double width, Func<string, double>? measure = null)
    {
        measure ??= s => s.Length;
        var content = new List<string>();
        for (var index = 0; index < pages.PageCount; index++)
        {
            Assert.Equal(index, pages.PageIndex);
            Assert.InRange(pages.Lines.Count + pages.NavigationLines.Count, 1, rows);
            Assert.All(pages.Lines.Concat(pages.NavigationLines), line => Assert.InRange(measure(line), 0, width));
            content.AddRange(pages.Lines);
            pages.MovePage(1);
        }
        return string.Concat(content);
    }

    private static IEnumerable<string> AllLines(string text, int rows, int width)
    {
        var pages = OverlapHoverPages.Create(text, 1, rows, width, s => new StringInfo(s).LengthInTextElements);
        for (var i = 0; i < pages.PageCount; i++)
        {
            foreach (var line in pages.Lines)
                yield return line;
            pages.MovePage(1);
        }
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
