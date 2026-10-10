using System.Text.RegularExpressions;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Reporting;
using PdfSharp.Pdf.IO;

namespace OpenNest.Tests.Reporting;

/// <summary>Slice 2: pagination, overflow, dense labels and late failures on real PDFs.</summary>
public sealed class NestPdfLayoutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-pdf-layout-" + Guid.NewGuid().ToString("N"));

    public NestPdfLayoutTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [SkippableFact]
    public void Write_MultiPlateJobHasOnePagePerPlateWithTotalsAcrossPlatesAndSameNamedReferences()
    {
        var path = Write(NestReportTestData.CreateMultiPlateNest());
        var pages = ReportPdf.Pages(path);

        Assert.Equal(5, pages.Length);
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Assert.Equal((612, 792), ReportPdf.EffectiveSize(pdf.Pages[0]));
            for (var page = 1; page < 5; page++)
                Assert.Equal((792, 612), ReportPdf.EffectiveSize(pdf.Pages[page]));
        }
        var summary = pages[0];
        Assert.Matches(@"Distinct layouts:\s+4", summary);
        Assert.Matches(@"Total physical sheets:\s+7", summary);
        // Bracket 1: 2/sheet x 2 + 1 x 3 + 1 x 1 = 8. Same-named 3: 1 x 2 + 1 x 1 = 3.
        Assert.Matches(@"1\s+Bracket\s+5\s+8\s+0\s+3\s+1-2, 4", summary);
        Assert.Matches(@"2\s+Rotated\s+1\s+3\s+0\s+2\s+1, 3", summary);
        Assert.Matches(@"3\s+Bracket\s+7\s+3\s+4\s+0\s+1, 3", summary);
        Assert.Matches(@"4\s+Unplaced\s+3\s+0\s+3\s+0\s+-", summary);
        Assert.Matches(@"3\s+30 x 60 in\s+1\s+2", summary);
        for (var plate = 1; plate <= 4; plate++)
        {
            var page = pages[plate];
            Assert.Contains($"Plate {plate} of 4", page);
            Assert.Contains($"Page {plate + 1} of 5", page);
            Assert.Contains("Nest report: Report test job", page);
        }
        Assert.Matches(@"1\s+Bracket\s+1\s+3", pages[2]);
        Assert.Matches(@"2\s+Rotated\s+1\s+1", pages[3]);
        Assert.Matches(@"3\s+Bracket\s+1\s+1", pages[3]);
        Assert.DoesNotMatch(@"(?m)^1\s+Bracket", pages[3]);
    }

    [SkippableFact]
    public void Write_ManyDrawingsAndPartsContinueTablesWithRepeatedHeadersAndNoLostRows()
    {
        var nest = NestReportTestData.CreateMultiPlateNest();
        var plate = nest.Plates[0];
        for (var i = 0; i < 70; i++)
        {
            var drawing = NestReportTestData.Rectangle($"Demand {i:D2}", 1, 1, i + 1);
            nest.Drawings.Add(drawing);
            if (i < 40)
                plate.Parts.Add(new Part(drawing, new Vector(1 + i % 20 * 2.2, 14 + i / 20 * 2.2)));
        }
        var path = Write(nest);
        var pages = ReportPdf.Pages(path);
        var total = pages.Length;

        var summaryPages = pages.TakeWhile(page => !page.Contains("Plate 1 of 4")).ToList();
        Assert.True(summaryPages.Count >= 2, $"summary pages: {summaryPages.Count}");
        var summaryText = string.Join("\n", summaryPages);
        // Every row exactly once; the Parts heading row repeats on every continuation page.
        for (var id = 1; id <= 74; id++)
            Assert.Single(Regex.Matches(summaryText, $@"(?m)^\s*{id}\s+(?:Bracket|Rotated|Unplaced|Demand)\b"));
        Assert.All(summaryPages.Skip(1), page => Assert.Matches(@"ID\s+Part\s+Drawing\s+Required\s+Nested\s+Shortage", page));
        // Placements first (4-43 on plate 1), then unplaced demand by ordinal name.
        Assert.Matches(@"43\s+Demand 39\s+40\s+2\s+38\s+0\s+1", summaryText);
        Assert.Matches(@"73\s+Demand 69\s+70\s+0\s+70\s+0\s+-", summaryText);
        Assert.Matches(@"74\s+Unplaced\s+3\s+0\s+3\s+0\s+-", summaryText);

        var platePages = pages.Where(page => page.Contains("Plate 1 of 4")).ToList();
        Assert.True(platePages.Count >= 2, $"plate 1 pages: {platePages.Count}");
        Assert.All(platePages, page => Assert.Contains("Nest report: Report test job", page));
        // Every page carrying plate-table rows repeats the table heading.
        var tablePages = platePages.Where(page => Regex.IsMatch(page, @"(?m)^\d+\s+Demand \d{2}\s+1\s+2$")).ToList();
        Assert.True(tablePages.Count >= 2, $"plate table pages: {tablePages.Count}");
        Assert.All(tablePages, page => Assert.Matches(@"ID\s+Drawing\s+Parts per plate\s+Total \(2 plates\)", page));
        var plateText = string.Join("\n", platePages);
        for (var i = 0; i < 40; i++)
            Assert.Matches($@"{i + 4}\s+Demand {i:D2}\s+1\s+2", plateText);
        Assert.DoesNotContain("Demand 40", plateText);
        for (var page = 1; page <= total; page++)
            Assert.Contains($"Page {page} of {total}", pages[page - 1]);
    }

    [SkippableFact]
    public void Write_LongNamesAndNotesWrapWithoutLosingTextOrOverflowingColumns()
    {
        var nest = NestReportTestData.CreateMultiPlateNest();
        var longName = "PN-" + string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 7)) + " revision C with long description";
        nest.Plates[0].Parts[2].BaseDrawing.Name = longName;
        nest.Notes = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"note{i}"));
        nest.Customer = "Customer " + string.Concat(Enumerable.Repeat("X", 120));
        var path = Write(nest);
        var pages = ReportPdf.Pages(path);

        var all = string.Join("\n", pages);
        var notes = Regex.Matches(all, @"note(\d+)").Select(match => int.Parse(match.Groups[1].Value)).ToList();
        Assert.Equal(Enumerable.Range(0, 900), notes);
        Assert.Contains(string.Concat(Enumerable.Repeat("X", 120)), Regex.Replace(all, @"\s+", ""));
        // The long drawing name is wrapped inside its column on the summary and plate tables.
        var summaryPage = Array.FindIndex(pages, page => page.Contains("Required") && page.Contains("2"));
        var platePage = Array.FindIndex(pages, page => page.Contains("Plate 1 of 4"));
        Assert.True(summaryPage >= 0 && platePage > summaryPage);
        Assert.Equal(longName.Replace(" ", ""), ColumnText(path, summaryPage + 1, "Drawing", "Required"));
        Assert.Equal(longName.Replace(" ", ""), ColumnText(path, platePage + 1, "Drawing", "Parts"));
        // Column headings stay on one line.
        Assert.Matches(@"ID\s+Part\s+Drawing\s+Required\s+Nested\s+Shortage\s+Extra\s+Plates", pages[summaryPage]);
    }

    [SkippableTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Write_MillimeterJobInEveryQuadrantDrawsInsideTheSheetOutline(int quadrant)
    {
        var nest = NestReportTestData.CreateMultiPlateNest();
        nest.Units = Units.Millimeters;
        nest.Thickness = 3;
        foreach (var plate in nest.Plates)
        {
            plate.Quadrant = quadrant;
            // Move every placement (and cutoff) into the sheet's own quadrant.
            var bounds = plate.BoundingBox(false);
            foreach (var part in plate.Parts)
                part.Offset(bounds.Left, bounds.Bottom);
        }
        var path = Write(nest);
        var pages = ReportPdf.Pages(path);

        Assert.Equal(5, pages.Length);
        Assert.Contains("millimeters (mm)", pages[0]);
        Assert.Contains("3 mm", pages[0]);
        Assert.Matches(@"24 x 48 mm", pages[2]);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var stream = ReportPdf.ContentStreams(pdf.Pages[1]).Last();
        stream = stream[(stream.IndexOf("W* n", StringComparison.Ordinal) + 4)..];
        var sheet = Regex.Match(stream, @"([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) re\s+S");
        Assert.True(sheet.Success);
        double Value(int group) => double.Parse(sheet.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);
        var (left, top, width, height) = (Value(1), Value(2), Value(3), Value(4));
        var points = Regex.Matches(stream, @"(?m)^([-\d.]+) ([-\d.]+) [ml]$")
            .Select(match => (X: double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Y: double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        Assert.NotEmpty(points);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, left - 1e-3, left + width + 1e-3);
            Assert.InRange(point.Y, top - 1e-3, top + height + 1e-3);
        });
    }

    [SkippableFact]
    public void Write_TinyRepeatedPartsShareOneDottedGroupOnThePlatePage()
    {
        var nest = NestReportTestData.CreateDenseNest();
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        var group = Assert.Single(plan.Groups);
        Assert.Equal("2", group.Label.Id);
        Assert.Equal(Enumerable.Range(2, 24), group.Parts.Order());
        Assert.Equal(2, plan.Labels.Count);
        Assert.DoesNotContain(plan.Labels, label => label.Id == "2");
        Assert.All(group.Parts, index => Assert.True(group.Boundary.Contains(plan.Fit.Rect(snapshot.Plates[0].Parts[index].Geometry.Bounds))));
        Assert.False(group.Boundary.IntersectsWith(group.Label.Box));
        var labels = plan.Labels.Append(group.Label).ToList();
        var font = NestReportDiagram.LabelFont();
        foreach (var label in labels)
        {
            Assert.True(label.Box.Width >= ReportText.Size(label.Id, font).Width);
            Assert.True(new PdfSharp.Drawing.XRect(0, 0, 720, 320).Contains(label.Box));
            Assert.DoesNotContain(labels, other => other != label && other.Box.IntersectsWith(label.Box));
        }
        var path = Write(nest);
        var pages = ReportPdf.Pages(path);
        Assert.Equal(2, pages.Length);
        Assert.Contains("A dotted outline groups like parts under one ID", pages[1]);
        Assert.DoesNotContain("detail", pages[1]);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var diagram = ReportPdf.ContentStreams(pdf.Pages[1]).Last();
        Assert.Matches(@"\[0\.6 1\.8\]\s*0\s+d", diagram);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_GroupDoesNotEncloseUnrelatedPartsOrCutoffs(bool cutoff)
    {
        var nest = NestReportTestData.CreateDenseNest();
        var drawing = NestReportTestData.Rectangle("Obstacle", 0.1, 0.1);
        drawing.IsCutOff = cutoff;
        nest.Plates[0].Parts.Add(new Part(drawing, new Vector(63.2, 31.2)));
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        Assert.Empty(plan.Groups);
        Assert.Equal(2, plan.Labels.Count);
    }

    [Fact]
    public void Plan_SeparatedClustersOfTheSamePartGetSeparateGroups()
    {
        var nest = NestReportTestData.CreateDenseNest();
        var plate = nest.Plates[0];
        for (var i = 14; i < 26; i++)
            plate.Parts[i].Offset(25, 0);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        Assert.Equal(2, plan.Groups.Count);
        Assert.All(plan.Groups, group => Assert.Equal(12, group.Parts.Count));
        Assert.False(plan.Groups[0].Boundary.IntersectsWith(plan.Groups[1].Boundary));
    }

    [SkippableFact]
    public void Write_LabelOnHoledPartAvoidsTheHole()
    {
        var snapshot = NestReportBuilder.Capture(NestReportTestData.CreateNest(), NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        var label = plan.Labels.First(label => label.Part == 0);
        var hole = snapshot.Plates[0].Parts[0].Geometry.Contours.SelectMany(c => c.Segments).Single(s => s.Center != null);
        var holeBox = plan.Fit.Rect(new ReportBounds(hole.Center!.X - hole.Radius, hole.Center.Y - hole.Radius,
            hole.Center.X + hole.Radius, hole.Center.Y + hole.Radius));
        Assert.False(label.Box.IntersectsWith(holeBox));
    }

    [Fact]
    public void Plan_LabelsSitAtThePolyLabelPoleLikePlateView()
    {
        // L-shape: the bounding-box center lies in the notch, outside the material.
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        foreach (var (x, y) in new[] { (10.0, 0.0), (10, 2), (2, 2), (2, 10), (0, 10), (0, 0) })
            program.Codes.Add(new LinearMove(x, y));
        var nest = new Nest("L test") { Units = Units.Inches };
        var drawing = new Drawing("L bracket", program);
        nest.Drawings.Add(drawing);
        var plate = new Plate(24, 48) { Quantity = 1 };
        plate.Parts.Add(new Part(drawing, new Vector(4, 4)));
        plate.Parts.Add(new Part(NestReportTestData.CreateHoledDrawing("Holed", 1), new Vector(20, 4)));
        nest.Plates.Add(plate);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);

        foreach (var label in plan.Labels)
        {
            var geometry = snapshot.Plates[0].Parts[label.Part].Geometry;
            var origin = new Vector(geometry.Bounds.Left, geometry.Bounds.Bottom);
            var rings = geometry.Contours.Select(contour => ClipperBridge.Flatten(ToShape(contour),
                NestReportDiagram.ChordTolerance(geometry.Bounds), circumscribe: false))
                .OrderByDescending(ring => ring.Area()).ToList();
            foreach (var ring in rings)
                ring.Offset(-origin.X, -origin.Y);
            var pole = PolyLabel.Find(rings[0], rings.Skip(1).ToList(), NestReportDiagram.PolePrecision(geometry.Bounds));
            var expected = plan.Fit.Point(pole.X + origin.X, pole.Y + origin.Y);
            // Pole of the placement-independent copy, to within the search precision.
            var tolerance = NestReportDiagram.PolePrecision(geometry.Bounds) * plan.Fit.Scale;
            Assert.InRange(label.Box.X + label.Box.Width / 2 - expected.X, -tolerance, tolerance);
            Assert.InRange(label.Box.Y + label.Box.Height / 2 - expected.Y, -tolerance, tolerance);
        }
        var l = plan.Labels.Single(label => label.Part == 0);
        var center = plan.Fit.Model(new PdfSharp.Drawing.XPoint(l.Box.X + l.Box.Width / 2, l.Box.Y + l.Box.Height / 2));
        // The pole of this L is in its corner square, about 1.17 in from both outer edges.
        Assert.InRange(center.X - 4, 0.9, 1.4);
        Assert.InRange(center.Y - 4, 0.9, 1.4);
    }

    [Fact]
    public void Plan_IdenticalRepeatedPartsGetTheSameLabelPositionOnEveryCopy()
    {
        // A square with a central hole has four equally deep poles; every copy must pick the same one.
        var snapshot = NestReportBuilder.Capture(NestReportTestData.CreateDenseNest(), NestReportTestData.GeneratedAt);
        var plan = NestReportDiagram.Plan(snapshot.Plates[0], 720, 320);
        var offsets = plan.Labels.GroupBy(label => label.Id).SelectMany(group =>
        {
            var relative = group.Select(label =>
            {
                var bounds = plan.Fit.Rect(snapshot.Plates[0].Parts[label.Part].Geometry.Bounds);
                return (X: label.Box.X - bounds.X, Y: label.Box.Y - bounds.Y);
            }).ToList();
            return relative.Select(offset => (offset.X - relative[0].X, offset.Y - relative[0].Y));
        });
        Assert.All(offsets, delta =>
        {
            Assert.InRange(delta.Item1, -0.01, 0.01);
            Assert.InRange(delta.Item2, -0.01, 0.01);
        });
        Assert.Equal(2, plan.Labels.Count(label => label.Id == "1"));
    }

    private static OpenNest.Geometry.Shape ToShape(ReportContour contour)
    {
        var shape = new OpenNest.Geometry.Shape();
        foreach (var segment in contour.Segments)
        {
            var start = new Vector(segment.Start.X, segment.Start.Y);
            if (segment.Center == null)
                shape.Entities.Add(new Line(start, new Vector(segment.End.X, segment.End.Y)));
            else
                shape.Entities.Add(new Circle(new Vector(segment.Center.X, segment.Center.Y), segment.Radius));
        }
        return shape;
    }

    [Fact]
    public void Write_TabbedLeadInFixtureSurvivesSaveReloadWithStaleTabFlags()
    {
        var nest = NestReportTestData.CreateTabbedNest();
        var original = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        var loaded = new NestReader(new MemoryStream(stream.ToArray())).Read();
        foreach (var part in loaded.Plates[0].Parts)
        {
            if (part.CuttingParameters != null)
                part.CuttingParameters.TabsEnabled = false;
        }
        if (loaded.Plates[0].CuttingParameters != null)
            loaded.Plates[0].CuttingParameters.TabsEnabled = false;

        var reloaded = NestReportBuilder.Capture(loaded, NestReportTestData.GeneratedAt);

        var before = Assert.Single(original.Plates[0].Parts).Geometry;
        var after = Assert.Single(reloaded.Plates[0].Parts).Geometry;
        Assert.True(after.StrokeOnly);
        Assert.Equal(before.Contours.Select(c => c.Closed), after.Contours.Select(c => c.Closed));
        Assert.Equal(before.Contours.Select(c => c.Segments.Length), after.Contours.Select(c => c.Segments.Length));
        Assert.Equal(before.Bounds.Left, after.Bounds.Left, 6);
        Assert.Equal(before.Bounds.Bottom, after.Bounds.Bottom, 6);
        Assert.Equal(before.Bounds.Right, after.Bounds.Right, 6);
        Assert.Equal(before.Bounds.Top, after.Bounds.Top, 6);
        Write(loaded, "reloaded.pdf");
    }

    [Fact]
    public void Capture_InvalidLaterPlateFailsWithItsNumberAndNoOutput()
    {
        var nest = NestReportTestData.CreateMultiPlateNest();
        ((LinearMove)nest.Plates[3].Parts[0].Program.Codes[1]).EndPoint = new Vector(double.NaN, 0);
        var path = Path.Combine(directory, "keep.report.pdf");
        File.WriteAllText(path, "keep");
        var before = NestReportBuilderTests.Fingerprint(nest);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NestPdfWriter.Write(NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt), path));

        Assert.Contains("Plate 4, part 1", error.Message);
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
        Assert.Equal(before, NestReportBuilderTests.Fingerprint(nest));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(40_000)]
    public void Write_LateMultiPageWriteFailureLeavesExistingReportAndSourceUnchanged(long failAfter)
    {
        var nest = NestReportTestData.CreateDenseNest();
        var path = Path.Combine(directory, "existing.report.pdf");
        var original = "%PDF previous report"u8.ToArray();
        File.WriteAllBytes(path, original);
        var before = NestReportBuilderTests.Fingerprint(nest);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        Assert.Throws<IOException>(() => NestPdfWriter.Write(snapshot, path,
            stream => new NestPdfWriterTests.FailingStream(stream, failAfter)));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
        Assert.Equal(before, NestReportBuilderTests.Fingerprint(nest));
    }

    [Fact]
    public void Write_SuccessLeavesSourceQuantitiesProgramsAndTimestampsUnchanged()
    {
        var nest = NestReportTestData.CreateDenseNest();
        var before = NestReportBuilderTests.Fingerprint(nest);
        Write(nest);
        Assert.Equal(before, NestReportBuilderTests.Fingerprint(nest));
    }

    [Fact]
    public void Ranges_CompressesConsecutivePlateNumbersLosslessly()
    {
        Assert.Equal("-", ReportText.Ranges([]));
        Assert.Equal("1", ReportText.Ranges([1]));
        Assert.Equal("1-3, 5, 7-8", ReportText.Ranges([1, 2, 3, 5, 7, 8]));
        Assert.Equal("A", ReportText.RowName(0));
        Assert.Equal("Z", ReportText.RowName(25));
        Assert.Equal("AA", ReportText.RowName(26));
    }

    [Fact]
    public void Wrap_BreaksUnbrokenTokensWithoutDroppingCharacters()
    {
        ReportFonts.Initialize();
        var font = new PdfSharp.Drawing.XFont(ReportFonts.Family, 9);
        var text = "short " + string.Concat(Enumerable.Repeat("0123456789", 8)) + "\nsecond  line";
        var lines = ReportText.Wrap(text, font, 60);
        Assert.All(lines, line => Assert.True(ReportText.Size(line, font).Width <= 59));
        Assert.Equal(text.Replace("\n", "").Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
        Assert.Contains("second  line", lines);
    }

    /// <summary>Text of the 2 row inside one table column, read from word boxes in reading order.</summary>
    private static string ColumnText(string path, int page, string column, string next)
    {
        var words = ReportPdf.Words(path, page);
        var heading = words.First(word => word.Text == column);
        var limit = words.First(word => word.Text == next && System.Math.Abs(word.Top - heading.Top) < 1);
        var above = words.First(word => word.Text == "1" && word.Left < heading.Left && word.Top > heading.Top);
        var below = words.First(word => word.Text == "3" && word.Left < heading.Left && word.Top > above.Top);
        var cell = words.Where(word => word.Left >= heading.Left - 1 && word.Right <= limit.Left
                && word.Top > above.Bottom && word.Bottom < below.Top)
            .OrderBy(word => word.Top).ThenBy(word => word.Left);
        // Nothing from this cell may extend into the next column.
        Assert.DoesNotContain(words, word => word.Left < limit.Left && word.Right > limit.Left
            && word.Top > above.Bottom && word.Bottom < below.Top);
        return string.Concat(cell.Select(word => word.Text));
    }

    private string Write(Nest nest, string name = "layout.report.pdf")
    {
        var path = Path.Combine(directory, name);
        NestPdfWriter.Write(NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt), path);
        return path;
    }
}
