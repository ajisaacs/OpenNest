using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A long strip standing beside a stack of rows (N26-2DJ: one terminal turned upright beside the
/// others) is cut where the tool reaches it, its top end, not where its center sits halfway up.
/// </summary>
public class SlenderPartOrderTests
{
    private const int Rows = 16;

    [Fact]
    public void LockedStrip_IsCutBesideTheRowsNearItsPierce()
    {
        var parameters = ExplicitContourTests.Parameters();
        var parts = Stack(parameters);
        var strip = Strip(parameters, LeadPathValidationTests.Rectangle(0, 0, 1, 14));
        strip.LeadInsLocked = true;
        parts.Add(strip);

        var position = PositionOf(strip, parts, parameters);

        // Its pierce is at its top, beside rows 8 and 9. Ordered by its center, beside rows 4
        // and 5, it was cut sixth.
        Assert.True(position is >= 8 and <= 10, $"strip cut at position {position}");
    }

    [Fact]
    public void StripWithAHoleAtItsTop_IsCutBesideTheRowsNearTheHole()
    {
        var parameters = ExplicitContourTests.Parameters();
        var parts = Stack(parameters);
        var drawing = LeadPathValidationTests.Rectangle(0, 0, 1, 14);
        drawing.MoveTo(0.75, 13.5); drawing.ArcTo(0.75, 13.5, 0.5, 13.5, RotationType.CCW);
        var strip = Strip(parameters, drawing);
        parts.Add(strip);

        var position = PositionOf(strip, parts, parameters);

        // Its hole is cut first and its outside entry leaves beside it, at its top. Ordered by its
        // center, it was cut last.
        Assert.True(position is >= 8 and <= 10, $"strip cut at position {position}");
    }

    private static int PositionOf(Part strip, List<Part> parts, CuttingParameters parameters)
    {
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts, confirmedParameters: parameters));
        Assert.True(result.ProposedOrder.Count != 0, $"{result.Status}: {string.Join("; ", result.Findings.Select(f => f.Message))}");
        return result.ProposedOrder.Select(p => p.SourcePart).ToList().IndexOf(strip);
    }

    // Sixteen 16 x 1 rows, 1.5 apart, from y 0.5, each with a hole at its right end like the
    // terminals: the stack runs on well past the strip's top.
    private static List<Part> Stack(CuttingParameters parameters) =>
        Enumerable.Range(0, Rows).Select(row =>
        {
            var clean = LeadPathValidationTests.Rectangle(0, 0, 16, 1);
            clean.MoveTo(15.75, 0.5); clean.ArcTo(15.75, 0.5, 15.5, 0.5, RotationType.CCW);
            var part = new Part(new Drawing($"row {row}", clean), new Vector(0, 0.5 + 1.5 * row));
            var prepared = PreparedContours.Capture(clean, parameters);
            var choices = Enumerable.Range(0, prepared.Count)
                .Select(contour => prepared.ClosestEntry(contour, contour == prepared.PerimeterOrdinal
                    ? new Vector(17, 0.5) : new Vector(15.75, 0.5)))
                .ToArray();
            Assert.True(part.RestoreLeadInProgram(prepared.Emit(choices), false));
            part.CuttingParameters = parameters;
            return part;
        }).ToList();

    // A 1 x 14 strip standing beside the rows, entered at its top edge.
    private static Part Strip(CuttingParameters parameters, Program clean)
    {
        var part = new Part(new Drawing("strip", clean), new Vector(17, 0.5));
        var prepared = PreparedContours.Capture(clean, parameters);
        var choices = Enumerable.Range(0, prepared.Count)
            .Select(contour => contour == prepared.PerimeterOrdinal
                ? prepared.ClosestEntry(contour, new Vector(0.5, 15))
                : prepared.ClosestEntry(contour, new Vector(0.75, 13.5)))
            .ToArray();
        Assert.True(part.RestoreLeadInProgram(prepared.Emit(choices), false));
        part.CuttingParameters = parameters;
        return part;
    }
}
