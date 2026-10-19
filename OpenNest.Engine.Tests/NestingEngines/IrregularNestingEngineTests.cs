using OpenNest.Engine.NestingEngines.Irregular;
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

public class IrregularNestingEngineTests
{
    [Fact]
    public void RectanglesFitOnOneSheetWithSpacing()
    {
        var job = Job(new[] { Part("rect", Rectangle(10, 5), 12) }, new[] { Stock("sheet", 48, 96, spacing: 0.25) });

        var result = new IrregularNestingEngine().Solve(job);

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
                Part("slot", Obround(10, 3), 6),
            },
            new[] { Stock("sheet", 40, 60, spacing: 0.5, edge: new Spacing(0.5, 0.5, 0.5, 0.5), quadrant: quadrant) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void ZeroSpacingStillKeepsPartsApartForValidation()
    {
        var job = Job(new[] { Part("disc", Disc(2), 30), Part("rect", Rectangle(7, 3), 20) }, new[] { Stock("sheet", 30, 40) });

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void LargeAndSmallConcavePartsShareASheet()
    {
        // End-to-end companion to NoFitCacheTests' containment cases (the precise regression guard).
        var job = Job(
            new[] { Part("small", LShape(3, 3, 1), 6), Part("big", Rectangle(20, 20), 2) },
            new[] { Stock("sheet", 25, 45, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void PicksTheCheaperSheetWhenItHoldsEverything()
    {
        var job = Job(
            new[] { Part("square", Rectangle(10, 10), 4) },
            new[] { Stock("big", 60, 120, spacing: 0.25), Stock("small", 25, 25, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("small", Assert.Single(result.Plates).StockId);
    }

    [Fact]
    public void SpillsOntoAdditionalSheets()
    {
        var job = Job(new[] { Part("rect", Rectangle(20, 10), 25) }, new[] { Stock("sheet", 30, 50, spacing: 0.5) });

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.True(result.Plates.Count > 1);
        Assert.Equal(25, result.Plates.Sum(p => p.Placements.Count));
        var indices = result.Plates.SelectMany(p => p.Placements).Select(p => p.InstanceIndex).OrderBy(i => i);
        Assert.Equal(Enumerable.Range(0, 25), indices);
    }

    [Fact]
    public void RespectsFixedAndBoundedRotationPolicies()
    {
        var fixedPolicy = RotationPolicy.Fixed(0);
        var sweep = RotationPolicy.BoundedSweep(0, System.Math.PI / 2, System.Math.PI / 4);
        var job = Job(
            new[]
            {
                Part("fixed", LShape(10, 6, 3), 8, fixedPolicy),
                Part("swept", Triangle(8, 5), 8, sweep),
            },
            new[] { Stock("sheet", 40, 60, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        foreach (var placement in result.Plates.SelectMany(p => p.Placements))
        {
            var policy = placement.PartId == "fixed" ? fixedPolicy : sweep;
            Assert.True(policy.Allows(placement.Rotation), $"{placement.PartId} at {placement.Rotation}");
        }
    }

    [Fact]
    public void OversizedPartIsReportedUnplacedWithoutBlockingOthers()
    {
        var job = Job(
            new[] { Part("huge", Rectangle(100, 100), 1), Part("small", Rectangle(5, 5), 3) },
            new[] { Stock("sheet", 20, 20) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
        Assert.Equal(1, result.Fulfillment.Single(f => f.PartId == "huge").Unplaced);
        Assert.Equal(3, result.Fulfillment.Single(f => f.PartId == "small").Placed);
    }

    [Fact]
    public void StopsWhenFiniteStockRunsOut()
    {
        var job = Job(new[] { Part("rect", Rectangle(9, 9), 20) }, new[] { Stock("sheet", 20, 20, quantity: 2) });

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStopReason.StockExhausted, result.StopReason);
        Assert.Equal(2, result.Plates.Count);
        var usage = Assert.Single(result.StockUsage);
        Assert.Equal(2, usage.Used);
        Assert.Equal(0, usage.Remaining);
    }

    [Fact]
    public void HonorsMaxPlates()
    {
        var job = Job(
            new[] { Part("rect", Rectangle(9, 9), 20) },
            new[] { Stock("sheet", 20, 20) },
            new NestJobOptions(maxPlates: 1)
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Single(result.Plates);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
    }

    [Fact]
    public void IsDeterministic()
    {
        NestJob Build() =>
            Job(
                new[] { Part("disc", Disc(2.5), 12), Part("ell", LShape(9, 7, 3), 12), Part("tri", Triangle(7, 7), 12) },
                new[] { Stock("a", 30, 45, spacing: 0.3), Stock("b", 40, 40, spacing: 0.3) }
            );

        var first = new IrregularNestingEngine().Solve(Build());
        var second = new IrregularNestingEngine().Solve(Build());

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }

    [Fact]
    public void EtchMarksAreLeftOutOfNestingGeometry()
    {
        // A bend tick starts on material and ends 1.0 into a side notch, outside the part but
        // inside its bounding box (the PEP case that crashed nesting before 1b5e1b1). As
        // material it is open geometry leaving the part; as a mark it must be ignored.
        var etched = Polyline((0, 0), (10, 0), (10, 4), (8, 4), (8, 6), (10, 6), (10, 10), (0, 10));
        etched.Codes.Add(new RapidMove(7.5, 5));
        etched.Codes.Add(new LinearMove(9, 5) { Layer = LayerType.Scribe });
        var job = Job(new[] { Part("part", etched, 2, RotationPolicy.Fixed(0)) }, new[] { Stock("sheet", 10.4, 20.6, spacing: 0.2) });

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
    }

    [Fact]
    public void HasPublicParameterlessConstructorForPluginDiscovery()
    {
        var engine = Activator.CreateInstance(typeof(IrregularNestingEngine));
        Assert.IsAssignableFrom<INestingEngine>(engine);
    }

}

public sealed class IrregularContractTests : EngineContractTests<IrregularNestingEngine> { }
