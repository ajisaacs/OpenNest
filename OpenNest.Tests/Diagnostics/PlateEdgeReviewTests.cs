using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using static OpenNest.Tests.TestHelpers;

namespace OpenNest.Tests.Diagnostics;

public class PlateEdgeReviewTests
{
    [Fact]
    public void PartInsideWorkArea_IsClear()
    {
        var report = Analyze(Edges(0.5), Square(1, 1, 4));

        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
        Assert.DoesNotContain("Off the plate", report.ToDisplayText());
        Assert.DoesNotContain("Outside work area", report.ToDisplayText());
    }

    [Fact]
    public void PartAtEdgeSpacing_IsClear()
    {
        var report = Analyze(Edges(0.5), Square(0.5, 0.5, 4));

        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void PartInsideEdgeSpacing_WarnsWithGapAndSide()
    {
        var report = Analyze(Edges(0.5), Square(0.2, 2, 4));

        var finding = Assert.Single(Find(report, PostVerificationKind.EdgeSpacing));
        Assert.Equal((1, 1), (finding.PlateNumber, finding.PartNumber));
        Assert.Equal("'square' is outside the work area: 0.200 from the plate's left edge, inside its "
            + "0.500 edge spacing.", finding.Message);
        Assert.Equal(0.1, finding.Location!.Value.X, 6);
        Assert.Contains("Outside work area: 1 warning(s)", report.ToDisplayText());
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void FarEdges_FollowThePlateSize()
    {
        // Default plate is 120 long (X) and 60 wide (Y).
        var finding = Assert.Single(Find(Analyze(Edges(0.5), Square(115.7, 20, 4)),
            PostVerificationKind.EdgeSpacing));
        Assert.Contains("right edge", finding.Message);

        finding = Assert.Single(Find(Analyze(Edges(0.5), Square(20, 55.9, 4)), PostVerificationKind.EdgeSpacing));
        Assert.Contains("top edge", finding.Message);
    }

    [Fact]
    public void ShortfallWithinSlack_IsClear()
    {
        var report = Analyze(Edges(0.5), Square(0.5 - SpacingOutline.SpacingSlack / 2, 2, 4));

        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void PartPastSheet_IsOffPlateNotEdgeSpacing()
    {
        var report = Analyze(Edges(0.5), Square(-1, 2, 4));

        var finding = Assert.Single(Find(report, PostVerificationKind.OffPlate));
        Assert.Contains("1.000 past the plate's left edge", finding.Message);
        Assert.Equal(0, finding.Location!.Value.X, 6);
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
        Assert.Contains("Off the plate: 1 warning(s)", report.ToDisplayText());
    }

    [Fact]
    public void PartEntirelyOffSheet_IsOffPlate()
    {
        var finding = Assert.Single(Find(Analyze(new Spacing(), Square(130, 10, 4)), PostVerificationKind.OffPlate));
        Assert.Contains("past the plate's right edge", finding.Message);
    }

    [Fact]
    public void ZeroEdgeSpacing_AllowsContactWithTheEdge()
    {
        var report = Analyze(new Spacing(), Square(0, 0, 4), Square(116, 56, 4));

        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void LeadInPastSheet_IsOffPlate()
    {
        // The part keeps zero edge spacing, but its lead-in starts 0.25 off the left edge.
        var program = new Program();
        program.MoveTo(-0.25, 2);
        program.Codes.Add(new LinearMove(0, 2) { Layer = LayerType.Leadin });
        program.LineTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 2);
        var part = new Part(new Drawing("leaded", program), new Vector(0, 0));

        var report = Analyze(new Spacing(), part);

        var finding = Assert.Single(Find(report, PostVerificationKind.OffPlate));
        Assert.Contains("0.250 past the plate's left edge", finding.Message);
    }

    [Fact]
    public void LeadInsideEdgeSpacing_IsClear()
    {
        // Leads may run into the edge spacing; only material is held to it.
        var program = new Program();
        program.MoveTo(-0.25, 2);
        program.Codes.Add(new LinearMove(0, 2) { Layer = LayerType.Leadin });
        program.LineTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 2);
        var part = new Part(new Drawing("leaded", program), new Vector(0.5, 0.5));

        var report = Analyze(Edges(0.5), part);

        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void Quadrant3Sheet_ChecksItsSignedFrame()
    {
        var nest = new Nest();
        nest.PlateDefaults.Quadrant = 3;
        var plate = nest.CreatePlate();
        plate.EdgeSpacing = Edges(0.5);
        plate.Parts.Add(Square(-4.2, -10, 4));

        var finding = Assert.Single(Find(PostVerificationAnalyzer.Analyze(nest), PostVerificationKind.EdgeSpacing));
        Assert.Contains("right edge", finding.Message);
    }

    [Fact]
    public void OutlinedSheet_WarnsOverObstacleAndNearOutline()
    {
        var outline = new SheetOutline(
            new[] { new Vector(0, 0), new Vector(120, 0), new Vector(120, 60), new Vector(0, 60) },
            new[] { new[] { new Vector(50, 20), new Vector(70, 20), new Vector(70, 40), new Vector(50, 40) } });

        var over = NestWith(Edges(0.5), Square(48, 25, 4));
        over.Plates[0].Outline = outline;
        Assert.Single(Find(PostVerificationAnalyzer.Analyze(over), PostVerificationKind.OffPlate));

        var near = NestWith(Edges(0.5), Square(45.8, 25, 4));
        near.Plates[0].Outline = outline;
        var report = PostVerificationAnalyzer.Analyze(near);
        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        var finding = Assert.Single(Find(report, PostVerificationKind.EdgeSpacing));
        Assert.Contains("is outside the work area: inside the 0.500 edge spacing of the plate's outline "
            + "or one of its obstacles", finding.Message);

        var clear = NestWith(Edges(0.5), Square(10, 10, 4));
        clear.Plates[0].Outline = outline;
        report = PostVerificationAnalyzer.Analyze(clear);
        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void CutOffParts_AreExempt()
    {
        var drawing = new Drawing("cutoff", Line(-1, 30, 125, 30)) { IsCutOff = true };
        var report = Analyze(Edges(0.5), new Part(drawing, new Vector(0, 0)));

        Assert.Empty(Find(report, PostVerificationKind.OffPlate));
        Assert.Empty(Find(report, PostVerificationKind.EdgeSpacing));
    }

    [Fact]
    public void InvalidEdgeSpacing_IsIncomplete()
    {
        var report = Analyze(new Spacing(double.NaN, 0, 0, 0), Square(10, 10, 4));

        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Incomplete
            && f.Message.StartsWith("Plate edge check:"));
    }

    private static Program Line(double x1, double y1, double x2, double y2)
    {
        var program = new Program();
        program.MoveTo(x1, y1);
        program.LineTo(x2, y2);
        return program;
    }

    private static Spacing Edges(double all) => new(all, all, all, all);

    private static PostVerificationFinding[] Find(PostVerificationReport report, PostVerificationKind kind) =>
        report.Findings.Where(f => f.Kind == kind).ToArray();

    private static PostVerificationReport Analyze(Spacing edges, params Part[] parts) =>
        PostVerificationAnalyzer.Analyze(NestWith(edges, parts));

    private static Nest NestWith(Spacing edges, params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.EdgeSpacing = edges;
        foreach (var part in parts)
            plate.Parts.Add(part);
        return nest;
    }

    private static Part Square(double x, double y, double size, string name = "square") =>
        new(new Drawing(name, MakeSquareProgram(0, 0, size)), new Vector(x, y));
}
