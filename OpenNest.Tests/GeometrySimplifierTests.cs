using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests;

public class GeometrySimplifierTests
{
    [Fact]
    public void FitCircle_PointsOnKnownCircle_ReturnsCorrectCenterAndRadius()
    {
        // 21 points on a semicircle centered at (5, 3) with radius 10
        var center = new Vector(5, 3);
        var radius = 10.0;
        var points = new List<Vector>();
        for (var i = 0; i <= 20; i++)
        {
            var angle = i * System.Math.PI / 20;
            points.Add(new Vector(
                center.X + radius * System.Math.Cos(angle),
                center.Y + radius * System.Math.Sin(angle)));
        }

        var (fitCenter, fitRadius) = GeometrySimplifier.FitCircle(points);

        Assert.InRange(fitCenter.X, 4.999, 5.001);
        Assert.InRange(fitCenter.Y, 2.999, 3.001);
        Assert.InRange(fitRadius, 9.999, 10.001);
    }

    [Fact]
    public void FitCircle_CollinearPoints_ReturnsInvalidCenter()
    {
        // Collinear points should produce degenerate result
        var points = new List<Vector>
        {
            new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)
        };

        var (fitCenter, _) = GeometrySimplifier.FitCircle(points);

        Assert.False(fitCenter.IsValid());
    }

    [Fact]
    public void Analyze_LinesFromSemicircle_FindsOneCandidate()
    {
        // Create 20 lines approximating a semicircle of radius 10
        var arc = new Arc(new Vector(0, 0), 10, 0, System.Math.PI, false);
        var points = arc.ToPoints(20);
        var shape = new Shape();
        for (var i = 0; i < points.Count - 1; i++)
            shape.Entities.Add(new Line(points[i], points[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1 };
        var candidates = simplifier.Analyze(shape);

        Assert.Single(candidates);
        Assert.Equal(0, candidates[0].StartIndex);
        Assert.Equal(19, candidates[0].EndIndex);
        Assert.Equal(20, candidates[0].LineCount);
        Assert.InRange(candidates[0].FittedArc.Radius, 9.5, 10.5);
        Assert.True(candidates[0].MaxDeviation <= 0.1);
    }

    [Fact]
    public void Analyze_TooFewLines_ReturnsNoCandidates()
    {
        // Only 2 consecutive lines — below MinLines threshold
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(1, 1)));
        shape.Entities.Add(new Line(new Vector(1, 1), new Vector(2, 0)));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Analyze_MixedEntitiesWithArc_OnlyAnalyzesLines()
    {
        // Line, Line, Line, Arc, Line, Line, Line — should find candidates only in line runs
        var shape = new Shape();
        // First run: 5 lines on a curve
        var arc1 = new Arc(new Vector(0, 0), 10, 0, System.Math.PI / 2, false);
        var pts1 = arc1.ToPoints(5);
        for (var i = 0; i < pts1.Count - 1; i++)
            shape.Entities.Add(new Line(pts1[i], pts1[i + 1]));

        // An existing arc entity (breaks the run)
        shape.Entities.Add(new Arc(new Vector(20, 0), 5, 0, System.Math.PI, false));

        // Second run: 4 lines on a different curve
        var arc2 = new Arc(new Vector(30, 0), 8, 0, System.Math.PI / 3, false);
        var pts2 = arc2.ToPoints(4);
        for (var i = 0; i < pts2.Count - 1; i++)
            shape.Entities.Add(new Line(pts2[i], pts2[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.5, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        Assert.Equal(2, candidates.Count);
        // First candidate covers indices 0-4 (5 lines)
        Assert.Equal(0, candidates[0].StartIndex);
        Assert.Equal(4, candidates[0].EndIndex);
        // Second candidate covers indices 6-9 (4 lines, after the arc at index 5)
        Assert.Equal(6, candidates[1].StartIndex);
        Assert.Equal(9, candidates[1].EndIndex);
    }

    [Fact]
    public void Apply_SingleCandidate_ReplacesLinesWithArc()
    {
        // 20 lines approximating a semicircle
        var arc = new Arc(new Vector(0, 0), 10, 0, System.Math.PI, false);
        var points = arc.ToPoints(20);
        var shape = new Shape();
        for (var i = 0; i < points.Count - 1; i++)
            shape.Entities.Add(new Line(points[i], points[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1 };
        var candidates = simplifier.Analyze(shape);
        var result = simplifier.Apply(shape, candidates);

        Assert.Single(result.Entities);
        Assert.IsType<Arc>(result.Entities[0]);
    }

    [Fact]
    public void Apply_OnlySelectedCandidates_LeavesUnselectedAsLines()
    {
        // Two runs of lines with an arc between them
        var shape = new Shape();
        var arc1 = new Arc(new Vector(0, 0), 10, 0, System.Math.PI / 2, false);
        var pts1 = arc1.ToPoints(5);
        for (var i = 0; i < pts1.Count - 1; i++)
            shape.Entities.Add(new Line(pts1[i], pts1[i + 1]));

        shape.Entities.Add(new Arc(new Vector(20, 0), 5, 0, System.Math.PI, false));

        var arc2 = new Arc(new Vector(30, 0), 8, 0, System.Math.PI / 3, false);
        var pts2 = arc2.ToPoints(4);
        for (var i = 0; i < pts2.Count - 1; i++)
            shape.Entities.Add(new Line(pts2[i], pts2[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.5, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        // Deselect the first candidate
        candidates[0].IsSelected = false;

        var result = simplifier.Apply(shape, candidates);

        // First run (5 lines) stays as lines + middle arc + second run replaced by arc
        // 5 original lines + 1 original arc + 1 fitted arc = 7 entities
        Assert.Equal(7, result.Entities.Count);
        // First 5 should be lines
        for (var i = 0; i < 5; i++)
            Assert.IsType<Line>(result.Entities[i]);
        // Index 5 is the original arc
        Assert.IsType<Arc>(result.Entities[5]);
        // Index 6 is the fitted arc replacing the second run
        Assert.IsType<Arc>(result.Entities[6]);
    }
}
