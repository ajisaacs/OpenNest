using OpenNest.Engine.NestingEngines.Rectangles;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Starter acceptance tests. Every layout is checked by the shared NestLayoutCheck the benchmark
/// scores with, so a passing test means the benchmark will accept the layout. They fail until
/// Solve() is implemented; add engine-specific tests alongside them.
/// </summary>
public class RectanglesNestingEngineTests
{
    [Fact]
    public void HasPublicParameterlessConstructorForPluginDiscovery()
    {
        var engine = Activator.CreateInstance(typeof(RectanglesNestingEngine));
        Assert.IsAssignableFrom<INestingEngine>(engine);
    }

    [Fact]
    public void RectanglesFitOnOneSheetWithSpacing()
    {
        var job = Job(new[] { Part("rect", Rectangle(10, 5), 12) }, new[] { Stock("sheet", 48, 96, spacing: 0.25) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Assert.Equal(12, result.Plates[0].Placements.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MixedArcAndConcavePartsAreValidInEveryQuadrant(int quadrant)
    {
        var job = Job(
            new[]
            {
                Part("disc", Disc(3), 10),
                Part("ell", LShape(12, 8, 4), 10),
                Part("tri", Triangle(9, 6), 10),
            },
            new[] { Stock("sheet", 40, 60, spacing: 0.5, edge: new Spacing(0.5, 0.5, 0.5, 0.5), quadrant: quadrant) }
        );

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void OverflowSpillsOntoAdditionalSheets()
    {
        var job = Job(new[] { Part("square", Rectangle(10, 10), 30) }, new[] { Stock("sheet", 25, 45, spacing: 0.25) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.True(result.Plates.Count > 1);
    }

    [Fact]
    public void PartTooBigForAnySheetIsReportedUnplaced()
    {
        var job = Job(
            new[] { Part("huge", Rectangle(50, 50), 1), Part("small", Rectangle(5, 5), 4) },
            new[] { Stock("sheet", 20, 20, spacing: 0.25) }
        );

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        var huge = Assert.Single(result.Fulfillment, f => f.PartId == "huge");
        Assert.Equal(1, huge.Unplaced);
    }

    [Fact]
    public void ExactFitGridPacksAtExactlyThePartSpacing()
    {
        // 4 x 3 boxes of 10 x 5 at 0.5 spacing need exactly 41.5 x 16.
        var job = Job(new[] { Part("r", Rectangle(10, 5), 12) },
            new[] { Stock("s", 16, 41.5, spacing: 0.5, quantity: 1) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(12, Assert.Single(result.Plates).Placements.Count);
    }

    [Fact]
    public void ArcExtremePartsStayClearAtTheSpacing()
    {
        // Discs and obrounds have arcs, not vertices, at their box edges: the validator's
        // circumscribed flattening would read box-touching copies as closer than the spacing.
        var job = Job(new[] { Part("disc", Disc(2), 20), Part("ob", Obround(8, 3), 12) },
            new[] { Stock("s", 30, 40, spacing: 0.25) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void MixedSizesFillOneSheetThatShelfPackingWouldSplit()
    {
        // Area check: 2*(24x20) + 4*(12x10) + 8*(6x5) = 960 + 480 + 240 = 1680 of 48 x 40 = 1920.
        // A maximal-rectangles packing fits all of it on one sheet with zero spacing.
        var job = Job(new[]
            {
                Rectangle("big", 24, 20, 2, RotationPolicy.Automatic),
                Rectangle("mid", 12, 10, 4, RotationPolicy.Automatic),
                Rectangle("small", 6, 5, 8, RotationPolicy.Automatic),
            },
            new[] { Stock("s", 40, 48) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
    }

    [Fact]
    public void RotatedInputIsNestedAtItsMinimumBoundingRectangle()
    {
        // A 10 x 4 rectangle drawn at 30 degrees: only its squared-up box fits 4 per 20.5 x 8.5 sheet.
        var c = System.Math.Cos(System.Math.PI / 6);
        var s = System.Math.Sin(System.Math.PI / 6);
        (double, double) R(double x, double y) => (x * c - y * s + 5, x * s + y * c + 5);
        var tilted = Polyline(R(0, 0), R(10, 0), R(10, 4), R(0, 4));
        var job = Job(new[] { Part("tilted", tilted, 4, RotationPolicy.Automatic) },
            new[] { Stock("s", 8.5, 20.5, spacing: 0.5, quantity: 1) });

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }
}

public sealed class RectanglesContractTests : EngineContractTests<RectanglesNestingEngine> { }
