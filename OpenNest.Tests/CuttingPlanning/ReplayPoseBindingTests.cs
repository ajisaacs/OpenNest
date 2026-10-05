using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class ReplayPoseBindingTests
{
    [Theory]
    [InlineData("near-x")]
    [InlineData("near-y")]
    [InlineData("zero-x")]
    [InlineData("zero-y")]
    [InlineData("zero-rotation")]
    [InlineData("matching-bits")]
    public void ExactReplay_BindsEveryPoseScalarByBits(string fault)
    {
        var part = new Part(new Drawing("pose", LeadPathValidationTests.Rectangle(0, 0, 10, 10)));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], new Vector(-2, 5),
            confirmedParameters: ExplicitContourTests.Parameters()));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, ready.Status);
        var source = Assert.Single(snapshot.Placements);
        var proposal = Assert.Single(ready.ProposedOrder);
        var x = source.Location.X; var y = source.Location.Y; var rotation = source.Rotation;
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        if (fault == "near-x") x += 1e-10;
        if (fault == "near-y") y += 1e-10;
        if (fault == "zero-x") x = negativeZero;
        if (fault == "zero-y") y = negativeZero;
        if (fault == "zero-rotation") rotation = negativeZero;
        // These deliberately remain equal under the old geometric/numeric operators.
        Assert.True(source.Location == new Vector(x, y));
        Assert.True(source.Rotation == rotation);
        var sameBits = BitConverter.DoubleToInt64Bits(source.Location.X) == BitConverter.DoubleToInt64Bits(x)
            && BitConverter.DoubleToInt64Bits(source.Location.Y) == BitConverter.DoubleToInt64Bits(y)
            && BitConverter.DoubleToInt64Bits(source.Rotation) == BitConverter.DoubleToInt64Bits(rotation);
        Assert.Equal(fault == "matching-bits", sameBits);
        var selected = new FixedProgramPlacement(source.SourcePart, source.SourceOrdinal, new Vector(x, y), rotation,
            source.LeadInsLocked, proposal.Execution, proposal.CopyProgram(), source.Prepared, source.Material, proposal.ContourChoices)
            .Propose(proposal.CopyProgram(), proposal.Execution, proposal.ContourChoices);
        var replay = CuttingPlanService.ReplayPrograms(snapshot, [selected], 0, default);
        Assert.Equal(sameBits ? CuttingPlanStatus.Ready : CuttingPlanStatus.InvalidInput, replay.Status);
        Assert.Equal(sameBits, replay.IndependentlyReplayed);
        if (sameBits) Assert.Single(replay.ProposedOrder);
        else Assert.Empty(replay.ProposedOrder);
    }
}
