using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>No post may cut a supplied blank's display perimeter, planned or not.</summary>
public class DisplayPerimeterPostTests
{
    public static IEnumerable<object[]> Posts() => [["cincinnati"], ["cifiber"], ["gravograph"]];

    private static IPostProcessor Post(string name) => name switch
    {
        "cincinnati" => new CincinnatiPostProcessor(new CincinnatiPostConfig { ConfigurationName = "CL940", PostedAccuracy = 4 }),
        "cifiber" => new CIFiberPostProcessor(new CIFiberPostConfig { ConfigurationName = "CI FIBER 8K" }),
        _ => new GravographISPostProcessor(),
    };

    private static string Output(string post, Nest nest)
    {
        // The engraver's writer refuses this fixture's sheet for its work envelope, so compare the
        // geometry its extractor hands the writer: the step that reads part programs.
        if (post == "gravograph")
            return string.Join("\n", new NestPolylineExtractor().ExtractLayered(nest).Select(poly =>
                poly.Layer + ":" + string.Join(";", poly.Points.Select(v => FormattableString.Invariant($"{v.X:R},{v.Y:R}")))));
        using var stream = new MemoryStream();
        Post(post).Post(nest, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static (Nest Nest, Part Part) DoorNest()
    {
        var nest = new Nest("Door") { Units = Units.Inches };
        var drawing = new Drawing("door", DisplayPerimeterTests.Door());
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(20, 30));
        var part = new Part(drawing, Vector.Zero);
        plate.Parts.Add(part);
        nest.Plates.Add(plate);
        return (nest, part);
    }

    private static (Nest Nest, Part Part) PlannedDoorNest()
    {
        var (nest, part) = DoorNest();
        var proposal = CuttingPlanBatch.Capture([nest.Plates[0]], ExplicitContourTests.Parameters(), false).Plan();
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: false).Status);
        Assert.True(DisplayPerimeterTail.HasTail(part.Program));
        return (nest, part);
    }

    [Theory]
    [MemberData(nameof(Posts))]
    public void PlannedDoor_PostsExactlyItsCutsAndNeverTheOutline(string post)
    {
        var (planned, _) = PlannedDoorNest();
        var (stripped, part) = PlannedDoorNest();
        // The same applied plan with its tail removed by hand: what the machine may run.
        Assert.True(part.RestoreLeadInProgram(DisplayPerimeterTail.WithoutTail(part.Program), locked: true));

        Assert.Equal(Output(post, stripped), Output(post, planned));
    }

    [Theory]
    [MemberData(nameof(Posts))]
    public void UnplannedDoor_IsRefusedByEveryPost(string post)
    {
        var (nest, _) = DoorNest();

        var error = Assert.Throws<InvalidOperationException>(() => Output(post, nest));

        Assert.Contains("display perimeter", error.Message);
        Assert.Contains("Plan Cutting", error.Message);
    }

    [Fact]
    public void UnplannedDoor_PrePostReviewNamesTheDisplayPerimeter()
    {
        var (nest, _) = DoorNest();

        var report = PostVerificationAnalyzer.Analyze(nest);

        Assert.Contains(report.Findings, f => f.Message.Contains("display perimeter", StringComparison.OrdinalIgnoreCase));
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void PlannedDoor_PrePostReviewIsClean()
    {
        var (nest, _) = PlannedDoorNest();

        var report = PostVerificationAnalyzer.Analyze(nest);

        Assert.DoesNotContain(report.Findings, f => f.Message.Contains("display perimeter", StringComparison.OrdinalIgnoreCase));
    }
}
