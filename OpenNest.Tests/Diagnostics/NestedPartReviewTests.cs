using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class NestedPartReviewTests
{
    [Fact]
    public void InnerCutBeforeItsHost_WithAClearHoleLead_HasNoNestedFinding()
    {
        var report = Analyze(Inner(), Host(new Vector(12, 10)));

        Assert.Empty(Nested(report));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    [Fact]
    public void InnerCutAfterItsHost_WarnsAndRequiresFreshConsent()
    {
        var report = Analyze(Host(new Vector(12, 10)), Inner());

        var finding = Assert.Single(Find(report, PostVerificationKind.NestedCutOrder));
        Assert.Equal(2, finding.PartNumber);
        Assert.Equal(1, finding.OtherPartNumber);
        Assert.NotNull(finding.Location);
        Assert.False(report.CanPost(false));
        Assert.True(report.CanPost(true));
        Assert.Contains("Nested part order: 1 warning(s)", report.ToDisplayText());
    }

    [Fact]
    public void PostingEntryPoint_EvenForAnOrderPreservingPost_RequiresFreshConsent()
    {
        var nest = NestWith(Host(new Vector(12, 10)), Inner());

        var report = PostVerificationAnalyzer.AnalyzeForPost(nest, new OrderPreservingPost());

        Assert.Single(Find(report, PostVerificationKind.NestedCutOrder));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
        Assert.False(report.CanPost(false));
        Assert.True(PostVerificationAnalyzer.AnalyzeForPost(NestWith(Inner(), Host(new Vector(12, 10))),
            new OrderPreservingPost()).CanPost(false));
    }

    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void HoleLeadCrossingTheInsert_WarnsAtItsSubprogramPose(Mode mode)
    {
        // The hole is a subprogram call; its lead-in starts at the hole centre, inside the insert.
        var report = Analyze(Inner(), Host(new Vector(10, 10), mode));

        var finding = Assert.Single(Find(report, PostVerificationKind.NestedLead));
        Assert.Equal(2, finding.PartNumber);
        Assert.Equal(1, finding.OtherPartNumber);
        Assert.Contains("cutting contour 1", finding.Message);
        Assert.False(report.CanPost(false));
        Assert.Contains("Nested part leads: 1 warning(s)", report.ToDisplayText());
    }

    [Fact]
    public void InsertLeadEnteringTheHostMaterial_Warns()
    {
        // The insert's lead-in starts beyond the cutout wall, in the host's material.
        var report = Analyze(Inner(leadStart: new Vector(16, 10)), Host(new Vector(12, 10)));

        var finding = Assert.Single(Find(report, PostVerificationKind.NestedLead));
        Assert.Equal(1, finding.PartNumber);
        Assert.Equal(2, finding.OtherPartNumber);
    }

    [Fact]
    public void LockedHostLead_IsReportedOnceAsAFixedLead()
    {
        var host = Host(new Vector(10, 10));
        host.LeadInsLocked = true;

        var report = Analyze(Inner(), host);

        Assert.Contains(Find(report, PostVerificationKind.FixedLead), f => f.PartNumber == 2 && f.OtherPartNumber == 1);
        Assert.Empty(Find(report, PostVerificationKind.NestedLead));
    }

    [Fact]
    public void InsertStraddlingTheCutoutWall_IsIncompleteNotClear()
    {
        var report = Analyze(Square(14, 9, 2), Host(new Vector(12, 10)));

        Assert.Contains(Find(report, PostVerificationKind.Incomplete), f =>
            f.PartNumber == 1 && f.OtherPartNumber == 2 && f.Message.Contains("Containment"));
        Assert.Empty(Find(report, PostVerificationKind.NestedCutOrder));
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void PartInAConcavePocketOutsideTheHost_HasNoNestedFinding()
    {
        var outline = new[] { (10.0, 0.0), (0.0, 0.0), (0.0, 10.0), (3.0, 10.0), (3.0, 3.0), (7.0, 3.0),
            (7.0, 10.0), (10.0, 10.0), (10.0, 5.0) };
        var program = new Program();
        program.MoveTo(10, 5);
        foreach (var (x, y) in outline)
            program.LineTo(x, y);
        var pocketed = new Part(new Drawing("pocketed", program));

        var report = Analyze(pocketed, Square(4.5, 5, 1));

        Assert.Empty(Nested(report));
        Assert.DoesNotContain(Find(report, PostVerificationKind.Incomplete), f => f.Message.Contains("Containment"));
    }

    private static PostVerificationFinding[] Nested(PostVerificationReport report) => report.Findings
        .Where(f => f.Kind is PostVerificationKind.NestedCutOrder or PostVerificationKind.NestedLead).ToArray();

    private static PostVerificationFinding[] Find(PostVerificationReport report, PostVerificationKind kind) =>
        report.Findings.Where(f => f.Kind == kind).ToArray();

    private static PostVerificationReport Analyze(params Part[] parts) =>
        PostVerificationAnalyzer.Analyze(NestWith(parts));

    private static Nest NestWith(params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        foreach (var part in parts)
            plate.Parts.Add(part);
        return nest;
    }

    // A 20x20 square host with a radius-5 cutout centred at (10, 10). Its placed program cuts the
    // hole first through a subprogram call (lead-in from holeLead to (15, 10)), then the perimeter.
    private static Part Host(Vector holeLead, Mode mode = Mode.Absolute)
    {
        var clean = new Program();
        clean.MoveTo(15, 10);
        clean.ArcTo(15, 10, 10, 10, RotationType.CCW);
        clean.MoveTo(20, 10);
        Outline(clean);
        var hole = new Program();
        hole.MoveTo(holeLead.X - 10, holeLead.Y - 10);
        hole.Codes.Add(new LinearMove(5, 0) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(5, 0, 0, 0) { Layer = LayerType.Display });
        hole.Mode = mode;
        var placed = new Program();
        placed.Codes.Add(new SubProgramCall { Program = hole, Offset = new Vector(10, 10), Id = 1 });
        placed.MoveTo(20.5, 10);
        placed.Codes.Add(new LinearMove(20, 10) { Layer = LayerType.Leadin });
        Outline(placed);
        placed.Mode = mode;
        var host = new Part(new Drawing("host", clean));
        Assert.True(host.RestoreLeadInProgram(placed, false));
        return host;

        static void Outline(Program program)
        {
            program.LineTo(20, 20);
            program.LineTo(0, 20);
            program.LineTo(0, 0);
            program.LineTo(20, 0);
            program.LineTo(20, 10);
        }
    }

    // A 2x2 insert at (9, 9) inside the host's cutout, cut from its right-edge midpoint.
    private static Part Inner(Vector? leadStart = null) =>
        Square(9, 9, 2, leadStart ?? new Vector(11.5, 10));

    private static Part Square(double x, double y, double size, Vector? leadStart = null)
    {
        var clean = new Program();
        clean.MoveTo(size, size / 2);
        Contour(clean, size);
        var part = new Part(new Drawing("square", clean), new Vector(x, y));
        var lead = leadStart ?? new Vector(x + size + 0.25, y + size / 2);
        var placed = new Program();
        placed.MoveTo(lead.X - x, lead.Y - y);
        placed.Codes.Add(new LinearMove(size, size / 2) { Layer = LayerType.Leadin });
        Contour(placed, size);
        Assert.True(part.RestoreLeadInProgram(placed, false));
        return part;

        static void Contour(Program program, double size)
        {
            program.LineTo(size, 0);
            program.LineTo(0, 0);
            program.LineTo(0, size);
            program.LineTo(size, size);
            program.LineTo(size, size / 2);
        }
    }

    private sealed class OrderPreservingPost : IPostProcessor, IPostVerificationSupport
    {
        public string Name => "order-preserving test post";
        public string Author => "tests";
        public string Description => "Writes nothing.";
        public bool PreservesPlacedProgramOrder => true;
        public void Post(Nest nest, Stream outputStream) => throw new NotSupportedException();
        public void Post(Nest nest, string outputFile) => throw new NotSupportedException();
    }
}
