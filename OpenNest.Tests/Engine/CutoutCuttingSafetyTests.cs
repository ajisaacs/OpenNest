using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Engine;

/// <summary>
/// Cutouts-first placements are only cut-safe while the insert is cut before its host and the
/// host's hole leads clear it. Auto Nest commits inserts first; posting review must warn once
/// that order is lost, and a fresh Plan Cutting run on those actual parts must restore it.
/// </summary>
public class CutoutCuttingSafetyTests
{
    [Fact]
    public void CommittedCutoutNest_CutsTheInsertFirstAndWarnsWhenReordered()
    {
        var frame = new OpenNest.Shapes.RingShape { OuterDiameter = 20, InnerDiameter = 10 }.GetDrawing();
        frame.Name = "neutral-frame";
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 6);
        program.LineTo(0, 6);
        program.LineTo(0, 0);
        var insert = new Drawing("neutral-insert", program);
        var request = new NestPipelineRequest("Irregular", new[] {
            new NestItem { Drawing = frame, Quantity = 1 },
            new NestItem { Drawing = insert, Quantity = 1 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) },
            new NestJobOptions(maxPlates: 1));
        var preview = NestPipeline.Run(request);
        Assert.Empty(preview.RefusedCutoutTrial);
        Assert.True(preview.IsValid, string.Join("; ", preview.Violations));
        var nest = new Nest();
        nest.Drawings.Add(frame);
        nest.Drawings.Add(insert);
        using (var manager = new PlateManager(nest))
            NestPipelineCommit.ApplyToEmptyPlates(preview, manager);
        var plate = Assert.Single(nest.Plates, p => p.Parts.Count != 0);
        Assert.Equal(new[] { insert, frame }, plate.Parts.Select(p => p.BaseDrawing));
        var inner = plate.Parts[0];
        var host = plate.Parts[1];
        var committed = PostVerificationAnalyzer.Analyze(nest);
        Assert.DoesNotContain(committed.Findings, f => f.Kind is PostVerificationKind.NestedCutOrder
            or PostVerificationKind.NestedLead or PostVerificationKind.Incomplete);

        // Reordered (for example by hand), the frame's cutout is cut before the insert inside it.
        plate.Parts.Reorder(new[] { host, inner });
        var before = PostVerificationAnalyzer.Analyze(nest);
        var order = Assert.Single(before.Findings, f => f.Kind == PostVerificationKind.NestedCutOrder);
        Assert.Equal((2, 1), (order.PartNumber, order.OtherPartNumber));
        Assert.False(before.CanPost(false));

        // Keeping that order is refused, so Apply cannot install it.
        var kept = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), preservePartOrder: true));
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, kept.Status);
        Assert.NotEqual(CuttingCommitStatus.Applied, CuttingPlanService.Apply([kept]).Status);
        Assert.Equal(new[] { host, inner }, plate.Parts);

        var planned = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));
        Assert.True(planned.Status == CuttingPlanStatus.Ready,
            planned.Status + ": " + string.Join("; ", planned.Findings.Select(f => f.Message)));
        Assert.Equal(new[] { inner, host }, planned.ProposedOrder.Select(o => o.SourcePart));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([planned]).Status);

        var after = PostVerificationAnalyzer.Analyze(nest);
        Assert.Empty(after.Findings);
        Assert.True(after.CanPost(false));
    }
}
