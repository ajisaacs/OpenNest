using System.Text.RegularExpressions;
using OpenNest.Geometry;
using OpenNest.Reporting;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace OpenNest.Tests.Reporting;

/// <summary>Machine clamps and sheet rulers on the plate layout view.</summary>
public sealed class NestReportClampsAndRulersTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-report-clamps-" + Guid.NewGuid().ToString("N"));

    public NestReportClampsAndRulersTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    /// <summary>The standard 48 x 24 test sheet with three 3 x 1.5 clamps on its origin-side edge.</summary>
    private static Nest ClampedNest(bool removeMiddle)
    {
        var nest = NestReportTestData.CreateNest();
        var plate = nest.Plates[0];
        plate.SetClampRail(new ClampRail(3, 1.5, [6, 24, 44]));
        if (removeMiddle)
            Assert.True(plate.SetClampRemoved("clamp-2", true));
        return nest;
    }

    [Fact]
    public void Capture_ListsActiveClampsThenRemovedOnesInThePlacementFrame()
    {
        var snapshot = NestReportBuilder.Capture(ClampedNest(removeMiddle: true), NestReportTestData.GeneratedAt);

        Assert.Equal(new[]
        {
            new ReportClamp("clamp-1", new ReportBounds(4.5, 0, 7.5, 1.5), false),
            new ReportClamp("clamp-3", new ReportBounds(42.5, 0, 45.5, 1.5), false),
            new ReportClamp("clamp-2", new ReportBounds(22.5, 0, 25.5, 1.5), true),
        }, snapshot.Plates[0].Clamps);
    }

    [Fact]
    public void Capture_MovesClampsWithTheSheetIntoItsQuadrant()
    {
        var nest = new Nest("Quadrant") { Units = Units.Inches };
        var drawing = NestReportTestData.Rectangle("Part", 2, 2);
        nest.Drawings.Add(drawing);
        var plate = new Plate(24, 48) { Quantity = 1, Quadrant = 3 };
        plate.Parts.Add(new Part(drawing, new Vector(-20, -20)));
        plate.SetClampRail(new ClampRail(3, 1.5, [6]));
        nest.Plates.Add(plate);

        var report = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt).Plates[0];

        // Quadrant 3 puts the origin at the sheet's upper-right corner: the clamp sits 6 in from
        // it along the top edge, inside the sheet's (-48, -24)-(0, 0) bounds.
        var clamp = Assert.Single(report.Clamps);
        Assert.Equal(new ReportBounds(-7.5, -1.5, -4.5, 0), clamp.Bounds);
        Assert.Equal(new ReportBounds(-48, -24, 0, 0), report.Bounds);
    }

    [Fact]
    public void Capture_PlateWithoutClampsHasNone()
    {
        var snapshot = NestReportBuilder.Capture(NestReportTestData.CreateNest(), NestReportTestData.GeneratedAt);

        Assert.Empty(snapshot.Plates[0].Clamps);
    }

    [Fact]
    public void Plan_RulersHugTheSheetWithLegibleTicksAndLabelsKeepClearOfThemAndTheClamps()
    {
        var plate = NestReportBuilder.Capture(ClampedNest(removeMiddle: false), NestReportTestData.GeneratedAt).Plates[0];
        var plan = NestReportDiagram.Plan(plate, 720, 320);
        var rulers = plan.Rulers;
        var sheet = plan.Fit.Rect(plate.Bounds);
        var canvas = new XRect(0, 0, 720, 320);

        Assert.True(canvas.Contains(rulers.Horizontal), $"{rulers.Horizontal}");
        Assert.True(canvas.Contains(rulers.Vertical), $"{rulers.Vertical}");
        Assert.True(rulers.Horizontal.Top >= sheet.Bottom);
        Assert.True(rulers.Vertical.Right <= sheet.Left);
        Assert.True(rulers.Step * plan.Fit.Scale >= 40);
        Assert.Equal(0, rulers.Majors(plate.Bounds.Left, plate.Bounds.Right).First());
        Assert.Equal(0, rulers.Majors(plate.Bounds.Bottom, plate.Bounds.Top).First());
        Assert.True(rulers.Step / rulers.Divisions * plan.Fit.Scale >= 4);

        var labels = plan.Labels.Concat(plan.Groups.Select(group => group.Label)).ToList();
        Assert.NotEmpty(labels);
        Assert.DoesNotContain(labels, label => label.Box.IntersectsWith(rulers.Horizontal)
            || label.Box.IntersectsWith(rulers.Vertical));
        foreach (var clamp in plate.Clamps)
            Assert.DoesNotContain(labels, label => label.Box.IntersectsWith(plan.Fit.Rect(clamp.Bounds)));
    }

    [Fact]
    public void Ruler_TicksAreStepMultiplesAndNeverPrintNegativeZero()
    {
        var ruler = new DiagramRulers(0.5, 5, 1, default, default);

        Assert.Equal(new[] { -1, -0.5, 0, 0.5, 1 }, ruler.Majors(-1.2, 1.2));
        Assert.Equal(new[] { 0.1, 0.2, 0.3, 0.4 }, ruler.Minors(0, 0.5).Select(value => System.Math.Round(value, 9)));
        Assert.Equal("0.0", ruler.Format(-0.01));
        Assert.Equal("-1.5", ruler.Format(-1.5));
        Assert.Equal("0", new DiagramRulers(10, 5, 0, default, default).Format(-0.2));
    }

    [SkippableFact]
    public void Write_PlatePageShowsClampsRulersAndTheirLegend()
    {
        var nest = ClampedNest(removeMiddle: true);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        var path = Path.Combine(directory, "clamps.report.pdf");
        NestPdfWriter.Write(snapshot, path);

        var page = ReportPdf.Pages(path)[1];
        Assert.Matches(@"Hatched rectangles are machine clamps\.\s+A dashed rectangle crossed out is a clamp to remove before cutting\.", page);
        Assert.Contains("Rulers show sheet coordinates in inches (in).", page);
        // The horizontal ruler's numbers share one row, the units in the corner before them.
        var bounds = snapshot.Plates[0].Bounds;
        var numbers = plan.Rulers.Majors(bounds.Left, bounds.Right).Select(plan.Rulers.Format);
        Assert.Matches($"(?m)^in {string.Join(" ", numbers)}$", page);
        foreach (var value in plan.Rulers.Majors(bounds.Bottom, bounds.Top).Skip(1))
            Assert.Matches($@"(?m)^{Regex.Escape(plan.Rulers.Format(value))}\b", page);

        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var diagram = ReportPdf.ContentStreams(pdf.Pages[1]).Last();
        // The removed clamp's dash (5, 3 times its 0.6 pen) differs from the cutoff dash.
        Assert.Matches(@"\[3 1\.8\]\s*0\s+d", diagram);
    }

    [SkippableFact]
    public void Write_PlateWithoutClampsOmitsTheClampLegend()
    {
        var path = Path.Combine(directory, "plain.report.pdf");
        NestPdfWriter.Write(NestReportBuilder.Capture(NestReportTestData.CreateNest(), NestReportTestData.GeneratedAt), path);

        var page = ReportPdf.Pages(path)[1];
        Assert.DoesNotContain("clamp", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rulers show sheet coordinates in inches (in).", page);
    }
}
