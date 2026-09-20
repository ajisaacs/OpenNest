using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

public class SortStripsTests
{
    private static Part MakeRectPart(double x, double y, double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("rect", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    [Fact]
    public void SortColumnsByHeight_NonUniformGaps_DoesNotExceedOriginalSpan()
    {
        // Three columns with non-uniform gaps between them (5, then 1) and heights
        // ordered so the sort-by-height pass must reorder them (tallest first, then
        // shortest, then medium). The tallest column's original position leaves a
        // 5-unit gap to its neighbor; that single sampled gap must not get replayed
        // as the spacing for the whole staircase once it's no longer the leading pair.
        var tall = MakeRectPart(0, 0, 10, 30); // Left 0-10,  gap of 5 to next
        var shortCol = MakeRectPart(15, 0, 5, 5); // Left 15-20, gap of 1 to next
        var medium = MakeRectPart(21, 0, 20, 15); // Left 21-41

        var originalRight = new[] { tall, shortCol, medium }.Max(p => p.BoundingBox.Right);
        var originalLeft = new[] { tall, shortCol, medium }.Min(p => p.BoundingBox.Left);
        var originalSpan = originalRight - originalLeft;

        var parts = new List<Part> { tall, shortCol, medium };
        IterativeShrinkFiller.SortColumnsByHeight(parts, spacing: 1.0);

        var newRight = parts.Max(p => p.BoundingBox.Right);
        var newLeft = parts.Min(p => p.BoundingBox.Left);
        var newSpan = newRight - newLeft;

        Assert.True(
            newSpan <= originalSpan + 1e-9,
            $"Resequenced columns must not exceed the original footprint: original span {originalSpan}, new span {newSpan}"
        );
    }
}
