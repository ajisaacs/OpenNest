using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A tabbed part stays in the sheet, so a rapid may pass over it, but Plan Cutting looks for a
/// route around it first and passes over only the tabbed parts it finds no way around.
/// </summary>
public class TabbedPartAvoidanceTests
{
    private static CuttingParameters Tabbed()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.TabsEnabled = true;
        parameters.TabConfig = new NormalTab { Size = 0.2 };
        return parameters;
    }

    // The parts whose tabbed outside a rapid of the planned route passes over, by part number.
    private static int[] PassedOver(CuttingPlanResult result, Vector start)
    {
        var checker = new ReleasedContourState(reportMissingLeadIns: false) { AvoidTabbedPart = _ => true };
        var position = start;
        var crossed = new List<int>();
        foreach (var proposal in result.ProposedOrder)
        {
            var execution = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, position, default);
            crossed.AddRange(checker.Check(execution, position, proposal.SourceOrdinal + 1)
                .Select(ReleasedContourState.AvoidedTabbedPart).OfType<int>());
            position = execution.DeparturePoint;
        }
        return crossed.Distinct().Order().ToArray();
    }

    [Fact]
    public void TabbedParts_AreRoutedAround_WhenARouteExists()
    {
        // Four tabbed 10 x 10 squares in a 2 x 2 grid: left tabbed-crossable, the plan left each
        // square across the square itself.
        var plate = new Plate(300, 300);
        var square = new Drawing("square", ExplicitContourTests.Square(false));
        foreach (var i in Enumerable.Range(0, 4))
            plate.Parts.Add(new Part(square, new Vector(1 + i % 2 * 11, 1 + i / 2 * 11)));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Tabbed()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Empty(PassedOver(result, new Vector(0, 0)));
    }

    [Fact]
    public void TabbedPart_IsPassedOver_OnlyWhereNoRouteGoesAround()
    {
        // A tabbed 0.8 x 10 upright strip between squares, in a kept order that goes left, strip,
        // right, then back left: that last rapid can only cross the strip.
        var plate = new Plate(20, 20);
        var square = new Drawing("square", LeadPathValidationTests.Rectangle(0, 0, 1, 1));
        plate.Parts.Add(new Part(square, new Vector(1, 2)));
        plate.Parts.Add(new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 0.8, 10)), new Vector(4, 1)));
        plate.Parts.Add(new Part(square, new Vector(7, 4)));
        plate.Parts.Add(new Part(square, new Vector(1, 8)));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Tabbed(), preservePartOrder: true));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal([2], PassedOver(result, new Vector(0, 0)));
    }

    [Fact]
    public void WithoutTabs_NothingIsAvoidedOrPassedOver()
    {
        var plate = new Plate(300, 300);
        var square = new Drawing("square", ExplicitContourTests.Square(false));
        foreach (var i in Enumerable.Range(0, 4))
            plate.Parts.Add(new Part(square, new Vector(1 + i % 2 * 11, 1 + i / 2 * 11)));

        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: ExplicitContourTests.Parameters()));
        var result = CuttingPlanService.Plan(snapshot);

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.False(snapshot.HasTabbedParts);
        Assert.False(result.Snapshot.AvoidsTabbedParts);
    }
}
