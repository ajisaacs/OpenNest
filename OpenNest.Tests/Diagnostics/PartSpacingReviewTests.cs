using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class PartSpacingReviewTests
{
    private const double Spacing = 0.25;

    [Fact]
    public void GapUnderSpacing_WarnsWithMeasuredGapAndMarker()
    {
        var report = Analyze(Spacing, Square(0, 0, 4), Square(4.1, 0, 4));

        var finding = Assert.Single(Find(report, PostVerificationKind.PartSpacing));
        Assert.Equal((1, 1, 2), (finding.PlateNumber, finding.PartNumber, finding.OtherPartNumber));
        Assert.Contains("0.100 apart", finding.Message);
        Assert.Contains("(0.250)", finding.Message);
        Assert.NotNull(finding.Location);
        Assert.Equal(4.05, finding.Location.Value.X, 3);
        Assert.InRange(finding.Location.Value.Y, 0, 4);
        Assert.Empty(Find(report, PostVerificationKind.Overlap));
        Assert.Contains("Part spacing: 1 warning(s)", report.ToDisplayText());
        Assert.False(report.CanPost(false));
        Assert.True(report.CanPost(true));
    }

    [Fact]
    public void ClearReport_OmitsPartSpacingLine()
    {
        var report = Analyze(Spacing, Square(0, 0, 4), Square(4.5, 0, 4));

        Assert.Empty(Find(report, PostVerificationKind.PartSpacing));
        Assert.DoesNotContain("Part spacing", report.ToDisplayText());
    }

    [Fact]
    public void Touching_WarnsWhenSpacingIsRequired()
    {
        var finding = Assert.Single(Find(Analyze(Spacing, Square(0, 0, 4), Square(4, 0, 4)),
            PostVerificationKind.PartSpacing));
        Assert.Contains("0.000 apart", finding.Message);
    }

    [Fact]
    public void ZeroSpacing_AllowsContact()
    {
        var report = Analyze(0, Square(0, 0, 4), Square(4, 0, 4));
        Assert.Empty(Find(report, PostVerificationKind.PartSpacing));
        Assert.Empty(Find(report, PostVerificationKind.Overlap));
    }

    [Fact]
    public void OverlappingPair_IsReportedOnlyAsOverlap()
    {
        var report = Analyze(Spacing, Square(0, 0, 4), Square(3, 0, 4));
        Assert.Single(Find(report, PostVerificationKind.Overlap));
        Assert.Empty(Find(report, PostVerificationKind.PartSpacing));
    }

    [Theory]
    [InlineData(2.1, true)] // 0.1 from the hole edge
    [InlineData(4.0, false)] // 2 from every hole edge
    public void PartInsideCutout_IsHeldToSpacingFromTheHoleEdge(double insert, bool warns)
    {
        var frame = new Program();
        frame.Codes.AddRange(SquareProgram(0, 0, 10).Codes);
        frame.Codes.AddRange(SquareProgram(2, 2, 6).Codes);
        var report = Analyze(Spacing, new Part(new Drawing("frame", frame)), Square(insert, insert, 2));

        Assert.Equal(warns, Find(report, PostVerificationKind.PartSpacing).Length == 1);
        Assert.Empty(Find(report, PostVerificationKind.Overlap));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    [Fact]
    public void RotatedPart_UsesItsPlacedPose()
    {
        // A 1x4 bar turned a quarter turn spans x in [-4, 0] about its location, so at x = 8.1
        // its left edge sits 0.1 from the square. Read unrotated (x in [8.1, 9.1]) it would clear.
        var bar = new Part(new Drawing("bar", Rectangle(1, 4)));
        bar.Rotate(System.Math.PI / 2);
        bar.Location = new Vector(8.1, 0);

        Assert.Equal(-4, bar.BoundingBox.Left - bar.Location.X, 6);
        Assert.Single(Find(Analyze(Spacing, Square(0, 0, 4), bar), PostVerificationKind.PartSpacing));
    }

    [Fact]
    public void LeadsAndCutOffs_AreNotMaterial()
    {
        var leaded = new Program();
        leaded.MoveTo(4.2, 2);
        leaded.Codes.Add(new LinearMove(5, 2) { Layer = LayerType.Leadin });
        leaded.LineTo(5, 0);
        leaded.LineTo(9, 0);
        leaded.LineTo(9, 4);
        leaded.LineTo(5, 4);
        leaded.LineTo(5, 2);
        var cutoff = new Program();
        cutoff.MoveTo(0, 4.05);
        cutoff.LineTo(4, 4.05);

        var report = Analyze(Spacing, Square(0, 0, 4), new Part(new Drawing("leaded", leaded)),
            new Part(new Drawing("cutoff", cutoff) { IsCutOff = true }));

        Assert.Empty(Find(report, PostVerificationKind.PartSpacing));
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.2494)]
    [InlineData(0.2496)]
    [InlineData(0.25)]
    [InlineData(0.3)]
    public void AgreesWithAutoNestLayoutCheck(double gap)
    {
        var a = Square(0, 0, 4, "a");
        var b = Square(4 + gap, 1, 4, "b");
        var nest = NestWith(Spacing, a, b);
        var drawings = new Dictionary<Drawing, (string Name, int Quantity)>
        {
            [a.BaseDrawing] = ("a", 1),
            [b.BaseDrawing] = ("b", 1),
        };
        var layout = NestLayoutCheck.Validate(new() { (nest.Plates[0], new List<Part> { a, b }) }, drawings)
            .Where(v => v.Contains("spacing")).ToList();

        var review = Find(PostVerificationAnalyzer.Analyze(nest), PostVerificationKind.PartSpacing);

        Assert.Equal(layout.Count, review.Length);
        Assert.Equal(gap < Spacing - SpacingOutline.SpacingSlack, review.Length == 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(17)]
    public void CirclesExactlyAtSpacing_AreClear(double degrees)
    {
        // Interactive fills place native arcs exactly at the spacing, with no engine margin.
        var angle = degrees * System.Math.PI / 180;
        var pitch = 4 + Spacing;
        var report = Analyze(Spacing, Circle(0, 0, "a"),
            Circle(pitch * System.Math.Cos(angle), pitch * System.Math.Sin(angle), "b"));

        Assert.Empty(Find(report, PostVerificationKind.PartSpacing));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    public void CirclesJustInsideSpacing_Warn(double degrees)
    {
        // 0.001 short: twice the slack, so a hand-nudged pair still warns.
        var angle = degrees * System.Math.PI / 180;
        var pitch = 4 + Spacing - 0.001;
        var report = Analyze(Spacing, Circle(0, 0, "a"),
            Circle(pitch * System.Math.Cos(angle), pitch * System.Math.Sin(angle), "b"));

        var finding = Assert.Single(Find(report, PostVerificationKind.PartSpacing));
        Assert.Contains("0.249 apart", finding.Message);
    }

    private static Part Circle(double x, double y, string name)
    {
        var program = new Program();
        program.MoveTo(2, 0);
        program.Codes.Add(new ArcMove(2, 0, 0, 0));
        return new Part(new Drawing(name, program), new Vector(x, y));
    }

    private static PostVerificationFinding[] Find(PostVerificationReport report, PostVerificationKind kind) =>
        report.Findings.Where(f => f.Kind == kind).ToArray();

    private static PostVerificationReport Analyze(double spacing, params Part[] parts) =>
        PostVerificationAnalyzer.Analyze(NestWith(spacing, parts));

    private static Nest NestWith(double spacing, params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.PartSpacing = spacing;
        foreach (var part in parts)
            plate.Parts.Add(part);
        return nest;
    }

    private static Part Square(double x, double y, double size, string name = "square") =>
        new(new Drawing(name, SquareProgram(0, 0, size)), new Vector(x, y));

    private static Program SquareProgram(double x, double y, double size)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.LineTo(x + size, y);
        program.LineTo(x + size, y + size);
        program.LineTo(x, y + size);
        program.LineTo(x, y);
        return program;
    }

    private static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, height);
        program.LineTo(0, height);
        program.LineTo(0, 0);
        return program;
    }
}
