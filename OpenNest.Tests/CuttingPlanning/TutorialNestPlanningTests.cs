using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The help's "Your first nest" layout: the four sample parts Auto Nest interlocked on a 60 x 120
/// sheet over a 2 inch support grid. Plan Cutting must give it a plan that applies without warnings:
/// rapids into round holes (cut as sub-programs) that no route keeps clear raise the head at the
/// hole's call, and the 5 x 5 gussets the grid cannot hold are held by their tabs, however they
/// are turned.
/// </summary>
public class TutorialNestPlanningTests
{
    // The tutorial's cutting settings: line leads, and new settings' support tabs.
    private const string Settings = """
        {"kerf":0.01,
         "externalLeadIn":{"type":"Line","length":0.15,"approachAngle":60},
         "externalLeadOut":{"type":"Line","length":0.1,"approachAngle":60},
         "internalLeadIn":{"type":"Line","length":0.15,"approachAngle":60},
         "internalLeadOut":{"type":"None"},
         "tabConfig":{"type":"Normal","size":0.25},
         "pierceClearance":0.0625,"pierceDiameter":0.06,
         "roundLeadInAngles":true,"leadInAngleIncrement":5}
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FirstNest_PlansReady(bool supportTabs)
    {
        var nest = new NestReader(Path.Combine(PlanningCharacterizationTests.RepositoryRoot(),
            "test-data", "help-tutorial", "first-nest.nest")).Read();
        var parameters = CuttingParametersSerializer.Deserialize(Settings);
        parameters.SupportTabs = supportTabs;
        parameters.TabHeldMaxLength = new CuttingParameters().TabHeldMaxLength;
        var plate = nest.Plates[0];
        Assert.NotNull(plate.SupportBed);

        var proposal = CuttingPlanBatch.Capture([plate], parameters, preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        var result = proposal.Plates[0].Result;
        Assert.False(result.IsSafestRouteFallback);
        Assert.All(result.Findings, f => Assert.Contains("raises the head", f.Message));
        Assert.Equal(result.HeadRaises, result.ProposedOrder.Sum(p => p.CopyProgram().Codes.Count(HeadRaise.IsMarker)));

        // What Plan Cutting applies, the pre-post review must agree with: every part that needs
        // a tab has one (the round flanges' single-circle outlines included), and no rapid crosses.
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        var findings = PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings;
        Assert.DoesNotContain(findings, f => f.Kind is PostVerificationKind.PartSupport
            or PostVerificationKind.RapidCrossing or PostVerificationKind.MissingLeadIn);

        // The tutorial posts with the Cincinnati CL post, which writes a planned nest in the order
        // checked, so the review before posting is complete and finds nothing.
        var post = new CincinnatiPostProcessor(new CincinnatiPostConfig { RequireLibraries = false });
        var report = PostVerificationAnalyzer.AnalyzeForPost(nest, post);
        Assert.True(report.Findings.Count == 0, string.Join("\n", report.Findings.Select(f => f.Message)));
    }
}
