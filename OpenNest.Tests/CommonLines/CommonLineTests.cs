using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.CommonLines;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// Common-line groups: span derivation, joining, the spacing exemption and the pre-post review.
/// The part is shaped like N26-295's PT42: 6 x 12.44 with 0.38 x 45° chamfered corners.
/// </summary>
public class CommonLineTests
{
    private const double Kerf = 0.012;
    private const double Width = 6;
    private const double Height = 12.44;
    private const double Chamfer = 0.38;

    [Fact]
    public void Spans_LongEdgesOneKerfApart_ShareTheStraightPart()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);

        var span = Assert.Single(CommonLineGeometry.Spans(a, b, Kerf));

        Assert.Equal(Height - 2 * Chamfer, span.Length, 6);
        Assert.Equal(Width + Kerf / 2, span.Start.X, 6);
        Assert.Equal(Width + Kerf / 2, span.End.X, 6);
    }

    [Theory]
    [InlineData(System.Math.PI / 2)]
    [InlineData(System.Math.PI)]
    [InlineData(3 * System.Math.PI / 2)]
    public void Spans_RotatedParts_StillShare(double rotation)
    {
        var a = Chamfered(0, 0, rotation);
        var b = Chamfered(0, 0, rotation);
        var box = a.BoundingBox;
        b.Offset(box.Length + Kerf, 0);

        var span = Assert.Single(CommonLineGeometry.Spans(a, b, Kerf));
        Assert.True(span.Length > Chamfer);
    }

    [Theory]
    [InlineData(Kerf + 0.002)]
    [InlineData(Kerf - 0.002)]
    [InlineData(0.25)]
    public void Spans_GapOffTheKerf_ShareNothing(double gap)
    {
        Assert.Empty(CommonLineGeometry.Spans(Chamfered(0, 0), Chamfered(Width + gap, 0), Kerf));
    }

    [Fact]
    public void Spans_EdgesThatDoNotFace_ShareNothing()
    {
        // Side by side but offset past the straight part: only chamfers would meet.
        Assert.Empty(CommonLineGeometry.Spans(Chamfered(0, 0), Chamfered(Width + Kerf, Height - 0.1), Kerf));
    }

    [Fact]
    public void Join_SlidesPartsToOneKerf_AndGroupsThem()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + 0.25, 0.1);
        var plate = Plate(a, b);

        var result = CommonLineJoiner.Join(plate, new[] { b, a }, Kerf, 1);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Same(result.Group, Assert.Single(plate.CommonLines));
        Assert.Equal(Width + Kerf, b.Location.X, 9);
        Assert.Equal(0.1, b.Location.Y, 9);
        Assert.Equal(Vector.Zero, a.Location);
        Assert.True(CommonLineGeometry.Check(result.Group).IsIntact);
    }

    [Fact]
    public void Join_RowOfFour_FormsOneChain()
    {
        var parts = Enumerable.Range(0, 4).Select(i => Chamfered(i * (Width + 0.25), 0)).ToArray();
        var plate = Plate(parts);

        var result = CommonLineJoiner.Join(plate, parts, Kerf, 1);

        Assert.True(result.Succeeded, result.Failure);
        for (var i = 0; i < 4; i++)
            Assert.Equal(i * (Width + Kerf), parts[i].Location.X, 9);
        Assert.Equal(3, CommonLineGeometry.Check(result.Group).Spans.Count);
    }

    [Fact]
    public void Join_NoFacingEdge_MovesNothing()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + 5, 0);
        var plate = Plate(a, b);

        var result = CommonLineJoiner.Join(plate, new[] { a, b }, Kerf, 1);

        Assert.False(result.Succeeded);
        Assert.Equal(Width + 5, b.Location.X, 9);
        Assert.Empty(plate.CommonLines);
    }

    [Fact]
    public void Join_ExistingGroupMovesAsOneAndMerges()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var c = Chamfered(2 * Width + Kerf + 0.3, 0);
        var plate = Plate(a, b, c);
        plate.JoinCommonLine(new[] { a, b }, Kerf);

        // Joining c to b anchors the pair nearest the origin and slides c.
        var result = CommonLineJoiner.Join(plate, new[] { c, b }, Kerf, 1);

        Assert.True(result.Succeeded, result.Failure);
        var group = Assert.Single(plate.CommonLines);
        Assert.Equal(3, group.Members.Count);
        Assert.Equal(2 * (Width + Kerf), c.Location.X, 9);
    }

    [Fact]
    public void Join_ZeroKerf_IsRefused()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + 0.25, 0);
        var result = CommonLineJoiner.Join(Plate(a, b), new[] { a, b }, 0, 1);
        Assert.False(result.Succeeded);
        Assert.Contains("kerf", result.Failure);
    }

    [Fact]
    public void RemovingAPart_DropsItFromItsGroup_AndAPairDisappears()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var c = Chamfered(2 * (Width + Kerf), 0);
        var plate = Plate(a, b, c);
        var group = plate.JoinCommonLine(new[] { a, b, c }, Kerf);

        plate.Parts.Remove(c);
        Assert.Equal(new[] { a, b }, group.Members);

        plate.Parts.Remove(b);
        Assert.Empty(plate.CommonLines);
    }

    [Fact]
    public void SpacingReview_GroupedPairAtKerf_IsClear_UngroupedIsNot()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var nest = NestWith(a, b);

        var ungrouped = PostVerificationAnalyzer.Analyze(nest);
        Assert.Single(ungrouped.Findings, f => f.Kind == PostVerificationKind.PartSpacing);

        nest.Plates[0].JoinCommonLine(new[] { a, b }, Kerf);
        // Unplanned programs cut every perimeter whole: these plates cut their common lines twice.
        nest.Plates[0].CutCommonLinesTwice = true;
        var grouped = PostVerificationAnalyzer.Analyze(nest);
        Assert.DoesNotContain(grouped.Findings, f => f.Kind is PostVerificationKind.PartSpacing
            or PostVerificationKind.CommonLine or PostVerificationKind.Overlap);
    }

    [Fact]
    public void SpacingReview_GroupedPairCloserThanKerf_Warns()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var nest = NestWith(a, b);
        nest.Plates[0].JoinCommonLine(new[] { a, b }, Kerf);
        // Unplanned programs cut every perimeter whole: these plates cut their common lines twice.
        nest.Plates[0].CutCommonLinesTwice = true;
        b.Offset(-Kerf / 2, 0);

        var findings = PostVerificationAnalyzer.Analyze(nest).Findings;
        var spacing = Assert.Single(findings, f => f.Kind == PostVerificationKind.PartSpacing);
        Assert.Contains("kerf", spacing.Message);
        Assert.Contains(findings, f => f.Kind == PostVerificationKind.CommonLine);
    }

    [Fact]
    public void DesktopSpacingCheck_HonorsTheGroup()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var plate = NestWith(a, b).Plates[0];
        Assert.Single(PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate)).SpacingPairs);

        plate.JoinCommonLine(new[] { a, b }, Kerf);
        Assert.Empty(PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate)).SpacingPairs);
    }

    [Fact]
    public void Review_MovedMember_BreaksTheGroup()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var nest = NestWith(a, b);
        nest.Plates[0].JoinCommonLine(new[] { a, b }, Kerf);
        // Unplanned programs cut every perimeter whole: these plates cut their common lines twice.
        nest.Plates[0].CutCommonLinesTwice = true;
        b.Offset(1, 0);

        var findings = PostVerificationAnalyzer.Analyze(nest).Findings
            .Where(f => f.Kind == PostVerificationKind.CommonLine).ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, f => Assert.Contains("no longer shares", f.Message));
        Assert.False(PostVerificationAnalyzer.Analyze(nest).CanPost(false));
    }

    [Fact]
    public void Review_PlateKerfChanged_Warns()
    {
        var a = Chamfered(0, 0);
        var b = Chamfered(Width + Kerf, 0);
        var nest = NestWith(a, b);
        nest.Plates[0].JoinCommonLine(new[] { a, b }, Kerf);
        // Unplanned programs cut every perimeter whole: these plates cut their common lines twice.
        nest.Plates[0].CutCommonLinesTwice = true;
        nest.Plates[0].CuttingParameters = new CuttingParameters { Kerf = 0.02 };

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.CommonLine);
        Assert.Contains("0.02 kerf", finding.Message);
    }

    [Fact]
    public void Review_LeadOnTheSharedEdge_CutsThePartner()
    {
        // A's lead-in starts beyond its right edge, inside B one kerf away.
        var program = new Program();
        program.Codes.Add(new RapidMove(Width + 0.1, 6));
        program.Codes.Add(new LinearMove(Width, 6) { Layer = LayerType.Leadin });
        program.Codes.Add(new LinearMove(Width, Height));
        program.Codes.Add(new LinearMove(0, Height));
        program.Codes.Add(new LinearMove(0, 0));
        program.Codes.Add(new LinearMove(Width, 0));
        program.Codes.Add(new LinearMove(Width, 6));
        var a = new Part(new Drawing("lead", program), Vector.Zero);
        var b = new Part(new Drawing("plain", Rectangle()), new Vector(Width + Kerf, 0));
        var nest = NestWith(a, b);
        nest.Plates[0].JoinCommonLine(new[] { a, b }, Kerf);
        // Unplanned programs cut every perimeter whole: these plates cut their common lines twice.
        nest.Plates[0].CutCommonLinesTwice = true;

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.CommonLine);
        Assert.Equal((1, 2), (finding.PartNumber, finding.OtherPartNumber));
        Assert.Contains("runs into", finding.Message);
    }

    private static Program Rectangle()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(Width, 0));
        program.Codes.Add(new LinearMove(Width, Height));
        program.Codes.Add(new LinearMove(0, Height));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    /// <summary>A PT42-like part: Width x Height, every corner chamfered, clockwise from the origin side.</summary>
    private static Part Chamfered(double x, double y, double rotation = 0)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(Chamfer, 0));
        program.Codes.Add(new LinearMove(Width - Chamfer, 0));
        program.Codes.Add(new LinearMove(Width, Chamfer));
        program.Codes.Add(new LinearMove(Width, Height - Chamfer));
        program.Codes.Add(new LinearMove(Width - Chamfer, Height));
        program.Codes.Add(new LinearMove(Chamfer, Height));
        program.Codes.Add(new LinearMove(0, Height - Chamfer));
        program.Codes.Add(new LinearMove(0, Chamfer));
        program.Codes.Add(new LinearMove(Chamfer, 0));
        var part = new Part(new Drawing("PT42", program));
        if (rotation != 0)
            part.Rotate(rotation);
        part.Offset(x - part.Location.X, y - part.Location.Y);
        return part;
    }

    private static Plate Plate(params Part[] parts)
    {
        var plate = new Plate(48, 120) { PartSpacing = 0.25 };
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static Nest NestWith(params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.PartSpacing = 0.25;
        foreach (var part in parts)
            plate.Parts.Add(part);
        return nest;
    }
}
