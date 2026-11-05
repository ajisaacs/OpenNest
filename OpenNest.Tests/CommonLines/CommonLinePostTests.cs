using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// Posting common lines cut once (docs/common-line-cutting.md): the CI Fiber post writes each
/// shared cut in the cut-off form with its lead-in and each remainder in the contour form,
/// compensated on its perimeter's side; posts that cannot write them refuse the nest.
/// </summary>
public class CommonLinePostTests
{
    private static Nest PlannedPair()
    {
        var nest = new Nest("N26-295") { Thickness = 0.1875, Material = new Material("Mild Steel") };
        var plate = CommonLinePlanningTests.Row(2);
        nest.Plates.Add(plate);
        var result = CommonLinePlanningTests.Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, CommonLinePlanningTests.Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        return nest;
    }

    private static string Body(string program)
    {
        var lines = program.Split("\r\n");
        var start = Array.IndexOf(lines, "N0:") + 1;
        var end = Array.LastIndexOf(lines, "/L \"L0\"");
        return string.Join("\n", lines[start..end]);
    }

    [Fact]
    public void CIFiber_WritesTheSharedCutUncompensatedAndEachRemainderOnItsPerimetersSide()
    {
        var nest = PlannedPair();
        var post = new CIFiberPostProcessor(new CIFiberPostConfig { ConfigurationName = "CI FIBER 4020" });
        using var stream = new MemoryStream();

        post.Post(nest, stream);

        // Part 1 cuts the shared line on the centerline (x 8.006), no G41/G42, entered and left
        // through the notches; then its remainder, pierced in that kerf and compensated G42 as its
        // counterclockwise perimeter would be. Part 2's remainder enters the same kerf at its other end.
        const string golden = """
            ( Sheet number - 1 )
            ( Part #1 )
            ( PART:PT42 )
            V.E.R4=1
            N1:
            /L "L0"
            V.E.R3=1
            G0X8.006Y2.13
            /L "L4"
            G1X8.006Y2.38
            /L "L6"
            G1X8.006Y14.06
            /L "ZHSOFF"
            N2:
            /L "L0"
            V.E.R3=2
            G0X8.006Y13.81
            /L "L4"
            G42
            G1X8Y14.06
            /L "L6"
            G1X7.62Y14.44
            G1X2.38Y14.44
            G1X2Y14.06
            G1X2Y2.38
            G1X2.38Y2
            G1X7.62Y2
            G1X8Y2.38
            /L "ZHSOFF"
            ( PART END )
            ( Part #2 )
            ( PART:PT42 )
            V.E.R4=2
            N3:
            /L "L0"
            V.E.R3=3
            G0X8.006Y2.63
            /L "L4"
            G42
            G1X8.012Y2.38
            /L "L6"
            G1X8.392Y2
            G1X13.632Y2
            G1X14.012Y2.38
            G1X14.012Y14.06
            G1X13.632Y14.44
            G1X8.392Y14.44
            G1X8.012Y14.06
            /L "ZHSOFF"
            ( PART END )
            """;
        Assert.Equal(golden.ReplaceLineEndings("\n"), Body(Encoding.UTF8.GetString(stream.ToArray())));
    }

    [Fact]
    public void CIFiber_CompensatesAClockwisePerimetersRemaindersG41()
    {
        // Imported drawings are wound clockwise: the same pair drawn that way.
        var drawing = CommonLinePlanningTests.Pt42();
        var reversed = new Program();
        var points = drawing.Program.Codes.OfType<Motion>().Select(m => m.EndPoint).Reverse().ToArray();
        reversed.Codes.Add(new RapidMove(points[0]));
        foreach (var point in points.Skip(1))
            reversed.Codes.Add(new LinearMove(point));
        var nest = new Nest("N26-295") { Thickness = 0.1875, Material = new Material("Mild Steel") };
        var plate = CommonLinePlanningTests.Row(2, drawing: new Drawing("PT42", reversed));
        nest.Plates.Add(plate);
        var result = CommonLinePlanningTests.Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, CommonLinePlanningTests.Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        using var stream = new MemoryStream();

        new CIFiberPostProcessor(new CIFiberPostConfig()).Post(nest, stream);

        var lines = Body(Encoding.UTF8.GetString(stream.ToArray())).Split("\n");
        Assert.Equal(2, lines.Count(line => line == "G41"));
        Assert.DoesNotContain("G42", lines);
    }

    [Fact]
    public void PostsThatCannotWriteSinglePassCommonLines_RefuseTheNest()
    {
        var nest = PlannedPair();
        using var stream = new MemoryStream();

        var cincinnati = Assert.Throws<InvalidOperationException>(() => new CincinnatiPostProcessor().Post(nest, stream));
        var gravograph = Assert.Throws<InvalidOperationException>(() => new GravographISPostProcessor().Post(nest, stream));

        Assert.Contains("cannot write single-pass common lines", cincinnati.Message);
        Assert.Contains("Cut Common Lines Twice", gravograph.Message);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Host_RefusesAPostWithoutSupport_AndPassesOneWithIt()
    {
        var nest = PlannedPair();

        var refused = Assert.Throws<InvalidOperationException>(() =>
            CommonLineCuts.EnsurePostable(nest, new NoCommonLinePost()));
        Assert.Contains("Plate 1, part 1 cuts a common line once", refused.Message);
        CommonLineCuts.EnsurePostable(nest, new CIFiberPostProcessor());

        // Cut twice, nothing is single-pass, so any post may write it.
        nest.Plates[0].CutCommonLinesTwice = true;
        var again = CommonLinePlanningTests.Plan(nest.Plates[0]);
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([again]).Status);
        CommonLineCuts.EnsurePostable(nest, new NoCommonLinePost());
    }

    private sealed class NoCommonLinePost : IPostProcessor
    {
        public string Name => "Plain";
        public string Author => "Tests";
        public string Description => "A post that knows nothing of common lines.";
        public void Post(Nest nest, Stream outputStream) => throw new NotSupportedException();
        public void Post(Nest nest, string outputFile) => throw new NotSupportedException();
    }
}
