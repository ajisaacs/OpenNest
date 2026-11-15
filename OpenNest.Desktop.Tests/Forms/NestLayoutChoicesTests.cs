using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Forms;

public class NestLayoutChoicesTests
{
    private static readonly Drawing Bracket = new("bracket", Rectangle());

    // 10 long (X) by 20 wide (Y).
    private static Program Rectangle()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 20);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        return program;
    }

    // 96 long (X) by 48 wide (Y).
    private static NestPlateStock Stock(int quadrant = 1) =>
        new("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25, quadrant: quadrant);

    private static ProposedPlate Sheet(NestPlateStock stock, params (double X, double Y)[] at)
    {
        var parts = at.Select(p =>
        {
            var part = new Part(Bracket) { Location = new Vector(p.X, p.Y) };
            part.UpdateBounds();
            return part;
        }).ToList();
        return new ProposedPlate(0, stock, parts);
    }

    private static NestLayoutChoice Layout(params ProposedPlate[] sheets) => new(sheets);

    [Fact]
    public void VerticalStrips_LeaveTheDistanceToTheRightEdge()
    {
        // Two columns to the full height: 76 free along the length, nothing above.
        var remnant = SheetRemnant.Of(Sheet(Stock(), (0, 0), (0, 28), (10, 0), (10, 28)));

        Assert.Equal(SheetEdge.Right, remnant!.Value.Edge);
        Assert.Equal(76, remnant.Value.Distance, 6);
        Assert.Equal("76 to right edge", remnant.Value.ToString());
    }

    [Fact]
    public void HorizontalStrips_LeaveTheDistanceToTheTop()
    {
        // One row along the whole length: 28 free above it.
        var remnant = SheetRemnant.Of(Sheet(Stock(), (0, 0), (40, 0), (86, 0)));

        Assert.Equal(SheetEdge.Top, remnant!.Value.Edge);
        Assert.Equal(28, remnant.Value.Distance, 6);
        Assert.Equal("28 to top", remnant.Value.ToString());
    }

    [Fact]
    public void TheFreeStripIsMeasuredAwayFromTheSheetOrigin()
    {
        // Quadrant 3: the sheet spans -96..0 by -48..0 and parts pack from its top-right corner.
        var remnant = SheetRemnant.Of(Sheet(Stock(3), (-10, -20), (-10, -48), (-20, -48), (-20, -20)));

        Assert.Equal(SheetEdge.Left, remnant!.Value.Edge);
        Assert.Equal(76, remnant.Value.Distance, 6);
    }

    [Fact]
    public void AnEmptySheet_HasNoFreeStrip() => Assert.Null(SheetRemnant.Of(Sheet(Stock())));

    [Fact]
    public void TheSummaryNamesTheLastSheetsFreeStrip()
    {
        var stock = Stock();
        var layout = Layout(Sheet(stock, (0, 0), (86, 28)), Sheet(stock, (0, 0), (0, 28)));

        Assert.Equal(2, layout.SheetCount);
        Assert.Equal(4, layout.PartCount);
        Assert.EndsWith("86 to right edge on sheet 2", layout.Summary);
        Assert.StartsWith("2 sheets · 4 parts", layout.Summary);
    }

    [Fact]
    public void ANewBestReplacesItsFamilyAndKeepsTheOtherDirection()
    {
        var stock = Stock();
        var choices = new NestLayoutChoices();
        var vertical = Layout(Sheet(stock, (0, 0), (0, 28), (10, 0)));
        var horizontal = Layout(Sheet(stock, (0, 0), (40, 0), (86, 0)));
        var tighter = Layout(Sheet(stock, (0, 0), (0, 28), (10.5, 0)));

        Assert.True(choices.Add(vertical, best: true));
        Assert.True(choices.Add(horizontal, best: false));
        Assert.Equal(new[] { vertical, horizontal }, choices.Top);

        // Same sheet count and direction as the old best: it takes its place.
        Assert.True(choices.Add(tighter, best: true));
        Assert.Equal(new[] { tighter, horizontal }, choices.Top);
        Assert.Same(tighter, choices.Best);
    }

    [Fact]
    public void AnAlternativeNeverDisplacesTheBestOfItsFamily_AndOnlyABetterOneDisplacesAnother()
    {
        var stock = Stock();
        var choices = new NestLayoutChoices();
        var best = Layout(Sheet(stock, (0, 0), (0, 28), (10, 0)));
        var sameFamily = Layout(Sheet(stock, (0, 0), (0, 28), (10, 0), (10, 28)));
        var row = Layout(Sheet(stock, (0, 0), (86, 0)));
        var fullerRow = Layout(Sheet(stock, (0, 0), (40, 0), (86, 0)));

        choices.Add(best, best: true);
        Assert.False(choices.Add(sameFamily, best: false));
        Assert.True(choices.Add(row, best: false));
        Assert.False(choices.Add(Layout(Sheet(stock, (0, 0), (86, 0))), best: false));
        Assert.True(choices.Add(fullerRow, best: false));

        Assert.Equal(new[] { best, fullerRow }, choices.Top);
    }

    [Fact]
    public void TheListIsCappedBestFirstThenByPartsPlaced()
    {
        var stock = Stock();
        var choices = new NestLayoutChoices(capacity: 2);
        var best = Layout(Sheet(stock, (0, 0), (0, 28)));
        var twoSheets = Layout(Sheet(stock, (0, 0)), Sheet(stock, (0, 0)));
        var row = Layout(Sheet(stock, (0, 0), (40, 0), (86, 0)));

        choices.Add(best, best: true);
        choices.Add(twoSheets, best: false);
        choices.Add(row, best: false);

        Assert.Equal(new[] { best, row }, choices.Top);
    }

    [Fact]
    public void LayoutReports_CarryEveryPlanAndMarkTheBest()
    {
        var reports = new List<NestLayoutReport>();
        var described = new List<OpenNest.Engine.NestProgress>();
        var progress = JobEngineNest.CreateLayoutProgress("Irregular", new Inline<OpenNest.Engine.NestProgress>(described.Add),
            new Inline<NestLayoutReport>(reports.Add));
        var sheets = new[] { Sheet(Stock(), (0, 0)) };
        NestPipelineResult Accept(CancellationToken _) => null!;

        progress.Report(new NestJobProgress(NestJobStage.PlanImproved, "s", 0, 0, 0) { PlanSheets = sheets, AcceptPlan = Accept });
        progress.Report(new NestJobProgress(NestJobStage.PlanAlternative, "s", 0, 0, 0) { PlanSheets = sheets, AcceptPlan = Accept });

        Assert.Empty(described);
        Assert.Equal(new[] { true, false }, reports.Select(r => r.Best));
        Assert.All(reports, r => Assert.Same(sheets, r.Layout.Sheets));
        Assert.All(reports, r => Assert.NotNull(r.Layout.Accept));
    }

    [Fact]
    public void AnAlternativePlan_IsNeverDescribedAsAStage()
    {
        var described = new List<OpenNest.Engine.NestProgress>();

        JobEngineNest.CreateProgress("Irregular", new Inline<OpenNest.Engine.NestProgress>(described.Add))
            .Report(new NestJobProgress(NestJobStage.PlanAlternative, "s", 0, 0, 0) { PlanSheets = [] });

        Assert.Empty(described);
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
