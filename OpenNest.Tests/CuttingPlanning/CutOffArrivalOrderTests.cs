using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A cut-off is entered at its start, on the origin-side edge, and left at its far end. The route
/// plans it that way: the parts before a line that splits the sheet finish beside its start, so
/// the rapid onto it crosses nothing already cut.
/// </summary>
public class CutOffArrivalOrderTests
{
    // Two columns of squares left of a full-height line, which is cut last, with the tool
    // arriving from the top right. Aimed at the line's middle, the route ends at the top of the
    // left column and the rapid down to the line's start crosses the right one. Aimed at the
    // start, it ends at the bottom of the right column, beside it.
    [Fact]
    public void ColumnsBesideASplittingLine_FinishBesideTheLinesStart()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(48, 8);
        var square = new Drawing("square", Square());
        foreach (var x in new[] { 0.5, 2.5 })
            for (var y = 2.0; y <= 42; y += 4)
                plate.Parts.Add(new Part(square, new Vector(x, y)));
        var line = new CutOff(new Vector(4, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(line);
        plate.RegenerateCutOffs(new CutOffSettings());
        PlateSequencing.PlaceCutOffs(plate, [line]);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(6, 48),
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed,
            $"{result.Status}: {string.Join("; ", result.Findings.Select(f => f.Message).Take(3))}");
        var order = result.ProposedOrder.Select(o => o.SourcePart).ToArray();
        Assert.True(order[^1].BaseDrawing.IsCutOff);
        Assert.Equal(new Vector(2.5, 2), order[^2].Location);
    }

    // A line through open scrap from (10, 0) to (10, 30) precedes nothing, so it is routed like
    // a part: reached at its start, left at its far end. Square C sits beside the start and B
    // beside the end, so the route goes C, the line, B; one that thought the tool stays at the
    // start would go the line, C, B and travel the column twice.
    [Fact]
    public void LineThroughScrap_RouteGoesOnFromItsFarEnd()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(40, 20);
        var square = new Drawing("square", Square());
        var c = new Part(square, new Vector(12, 1));
        var b = new Part(square, new Vector(12, 31));
        plate.Parts.Add(b);
        plate.Parts.Add(c);
        var line = new CutOff(new Vector(10, 0), CutOffAxis.Vertical) { StartLimit = 0, EndLimit = 30 };
        plate.CutOffs.Add(line);
        plate.RegenerateCutOffs(new CutOffSettings());
        PlateSequencing.PlaceCutOffs(plate, [line]);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed,
            $"{result.Status}: {string.Join("; ", result.Findings.Select(f => f.Message).Take(3))}");
        var order = result.ProposedOrder.Select(o => o.SourcePart).ToArray();
        Assert.Equal(3, order.Length);
        Assert.Same(c, order[0]);
        Assert.True(order[1].BaseDrawing.IsCutOff);
        Assert.Same(b, order[2]);
    }

    private static Program Square()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(1, 0);
        program.LineTo(1, 1);
        program.LineTo(0, 1);
        program.LineTo(0, 0);
        return program;
    }
}
