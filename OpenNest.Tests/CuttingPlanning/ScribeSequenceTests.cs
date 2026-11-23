using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A part's scribe marks are etched in a short tour: the marks on one side of the part before
/// the ones on the other, an open mark from whichever end is nearer.
/// </summary>
public class ScribeSequenceTests
{
    private static Shape Mark(double x1, double y1, double x2, double y2)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(x1, y1), new Vector(x2, y2)));
        return shape;
    }

    private static double Travel(IReadOnlyList<Shape> marks)
    {
        var travel = 0.0;
        for (var i = 1; i < marks.Count; i++)
        {
            var end = ((Line)marks[i - 1].Entities[^1]).EndPoint;
            travel += end.DistanceTo(((Line)marks[i].Entities[0]).StartPoint);
        }
        return travel;
    }

    // Two bends 5 apart across a 40-wide part, each etched at both edges: drawn bend by bend,
    // the marks cross the part three times; sequenced, they cross it once.
    [Fact]
    public void BendEtches_AreEtchedOneSideThenTheOther()
    {
        var drawn = new List<Shape>
        {
            Mark(0, 5, 0.5, 5), Mark(39.5, 5, 40, 5),
            Mark(0, 10, 0.5, 10), Mark(39.5, 10, 40, 10),
        };

        var ordered = ContourCuttingStrategy.SequenceScribes(drawn);

        Assert.Equal(4, ordered.Count);
        var sides = ordered.Select(m => ((Line)m.Entities[0]).StartPoint.X < 20).ToList();
        Assert.Equal(1, sides.Zip(sides.Skip(1)).Count(pair => pair.First != pair.Second));
        Assert.True(Travel(ordered) < Travel(drawn));
        // Reversed marks are copies: the drawn entities are untouched.
        Assert.Equal(new Vector(0, 5), ((Line)drawn[0].Entities[0]).StartPoint);
    }

    // PT56: a bend mark at each end of two bends on an 80-long part, cut from its right end.
    // Etching the right end first then the left leaves a rapid back across the part.
    [Fact]
    public void BendEtches_EndNearWhereTheCutStarts()
    {
        var drawn = new List<Shape>
        {
            Mark(0, 5, 1, 5), Mark(79, 5, 80, 5),
            Mark(0, 8, 1, 8), Mark(79, 8, 80, 8),
        };

        var ordered = ContourCuttingStrategy.SequenceScribes(drawn, new Vector(80, 6));

        Assert.All(ordered.Take(2), m => Assert.True(((Line)m.Entities[0]).StartPoint.X < 40));
        Assert.All(ordered.Skip(2), m => Assert.True(((Line)m.Entities[0]).StartPoint.X > 40));
    }

    [Fact]
    public void SingleMark_IsKept()
    {
        var mark = Mark(0, 0, 1, 0);
        Assert.Same(mark, Assert.Single(ContourCuttingStrategy.SequenceScribes(new[] { mark })));
        Assert.Empty(ContourCuttingStrategy.SequenceScribes(Array.Empty<Shape>()));
    }
}
