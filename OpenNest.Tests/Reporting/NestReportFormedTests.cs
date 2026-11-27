using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.Reporting;

namespace OpenNest.Tests.Reporting;

/// <summary>Formed parts (bent or rolled after cutting) are marked for sorting.</summary>
public sealed class NestReportFormedTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "opennest-formed-" + Guid.NewGuid().ToString("N"));

    public NestReportFormedTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    private static NestReportSnapshot Capture(Nest nest) =>
        NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

    /// <summary>
    /// The report test job with "Bracket" 1 bent (a bend line), "Rotated" 2 formed (marked by
    /// hand: its bend lines were lost) and the other "Bracket" 3 flat.
    /// </summary>
    private static Nest CreateFormedNest()
    {
        var nest = NestReportTestData.CreateNest();
        var parts = nest.Plates[0].Parts;
        parts[0].BaseDrawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 5),
            EndPoint = new Vector(10, 5),
        });
        parts[2].BaseDrawing.FormedOverride = true;
        return nest;
    }

    [Fact]
    public void Capture_MarksBentAndHandMarkedDrawingsAndTheirPlacements()
    {
        var snapshot = Capture(CreateFormedNest());

        Assert.Equal(new[] { true, true, false, false }, snapshot.Drawings.Select(d => d.Formed));
        var placements = snapshot.Plates[0].Parts;
        Assert.Equal(new[] { true, true, true, false }, placements.Select(p => p.Formed));
        Assert.True(snapshot.HasFormedParts);
    }

    [Fact]
    public void Capture_UncheckedBentDrawingIsFlat()
    {
        var nest = CreateFormedNest();
        nest.Plates[0].Parts[0].BaseDrawing.FormedOverride = false;

        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        Assert.False(snapshot.Drawings[0].Formed);
        Assert.False(snapshot.Plates[0].Parts[0].Formed);
    }

    [SkippableFact]
    public void Write_PlatePageMarksFormedPartsInDiagramTableAndLegend()
    {
        var nest = CreateFormedNest();
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 265);
        Assert.Equal(new[] { "1F", "1F", "2F", "3" }, plan.Labels.Select(l => l.Id).Order());

        var path = Path.Combine(directory, "formed.pdf");
        NestPdfWriter.Write(snapshot, path);
        var plate = ReportPdf.Text(path, 2);

        Assert.Matches(@"ID\s+Drawing\s+Formed\s+Parts per plate", plate);
        Assert.Matches(@"1F\s+Bracket\s+Yes\s+2\s+4", plate);
        Assert.Matches(@"2F\s+Rotated\s+Yes\s+1\s+2", plate);
        Assert.Matches(@"(?m)\b3\s+Bracket\s+1\s+2", plate);
        Assert.Contains("F Formed: goes to forming.", plate);
    }

    [SkippableFact]
    public void Write_JobWithoutFormedPartsHasNoFormedColumnOrLegend()
    {
        var path = Path.Combine(directory, "flat.pdf");
        NestPdfWriter.Write(Capture(NestReportTestData.CreateNest()), path);

        Assert.DoesNotContain("Formed", ReportPdf.Text(path, 2));
    }

    [Fact]
    public void Plan_GroupedTinyFormedPartsCarryTheMarkOnTheirSharedLabel()
    {
        var nest = NestReportTestData.CreateDenseNest();
        nest.Plates[0].Parts[2].BaseDrawing.FormedOverride = true;
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 265);

        Assert.Equal("2F", Assert.Single(plan.Groups).Label.Id);
    }

    [Fact]
    public void LabelText_FormedTabbedPartShowsBothMarks()
    {
        var nest = NestReportTestData.CreateTabbedNest();
        nest.Plates[0].Parts[0].BaseDrawing.FormedOverride = true;
        var plate = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt).Plates[0];

        Assert.Equal("1F*", NestReportDiagram.LabelText(plate, 0));
    }
}
