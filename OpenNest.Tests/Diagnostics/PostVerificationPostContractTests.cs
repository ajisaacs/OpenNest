using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.Diagnostics;

public class PostVerificationPostContractTests
{
    [Fact]
    public void ReorderingPostsRequireAcknowledgmentEvenWhenNestChecksAreClear()
    {
        var nest = ClearNest();
        Assert.False(PostVerificationAnalyzer.Analyze(nest).HasWarnings);
        IPostProcessor[] posts =
        [
            new CincinnatiPostProcessor(new CincinnatiPostConfig()),
            new GravographISPostProcessor(new GravographISPostConfig())
        ];
        foreach (var post in posts)
        {
            var report = PostVerificationAnalyzer.AnalyzeForPost(nest, post);
            Assert.Contains(report.Findings, finding => finding.Kind == PostVerificationKind.Incomplete);
            Assert.False(report.CanPost(false));
            Assert.True(report.CanPost(true));
            Assert.Contains(post.Name, report.ToDisplayText());
        }
    }

    [Fact]
    public void CIFiberUsesPlacedOrderWithoutAnExtraOrderWarning()
    {
        var report = PostVerificationAnalyzer.AnalyzeForPost(ClearNest(),
            new CIFiberPostProcessor(new CIFiberPostConfig()));
        Assert.Empty(report.Findings);
        Assert.True(report.CanPost(false));
    }

    private static Nest ClearNest()
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 0);
        var part = new Part(new Drawing("square", program));
        part.ApplyLeadIns(new CuttingParameters { ExternalLeadIn = new LineLeadIn { Length = 0.5 } },
            new Vector(-2, -2));
        var nest = new Nest();
        nest.CreatePlate().Parts.Add(part);
        return nest;
    }
}
