using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Opus55.Tests;

public class Opus55NestingEngineTests
{
    [Fact]
    public void RectanglesFitOnOneSheetWithSpacing()
    {
        var job = Job(new[] { Part("rect", Rectangle(10, 5), 12) }, new[] { Stock("sheet", 48, 96, spacing: 0.25) });

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void ZeroSpacingStillKeepsPartsApartForValidation()
    {
        var job = Job(new[] { Part("disc", Disc(2), 30), Part("rect", Rectangle(7, 3), 20) }, new[] { Stock("sheet", 30, 40) });

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void PicksTheCheaperSheetWhenItHoldsEverything()
    {
        var job = Job(
            new[] { Part("square", Rectangle(10, 10), 4) },
            new[] { Stock("big", 60, 120, spacing: 0.25), Stock("small", 25, 25, spacing: 0.25) }
        );

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("small", Assert.Single(result.Plates).StockId);
    }

    [Fact]
    public void SpillsOntoAdditionalSheets()
    {
        var job = Job(new[] { Part("rect", Rectangle(20, 10), 25) }, new[] { Stock("sheet", 30, 50, spacing: 0.5) });

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
        Assert.Equal(1, result.Fulfillment.Single(f => f.PartId == "huge").Unplaced);
        Assert.Equal(3, result.Fulfillment.Single(f => f.PartId == "small").Placed);
    }

    [Fact]
    public void StopsWhenFiniteStockRunsOut()
    {
        var job = Job(new[] { Part("rect", Rectangle(9, 9), 20) }, new[] { Stock("sheet", 20, 20, quantity: 2) });

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var result = new Opus55NestingEngine().Solve(job);

        AssertValid(job, result);
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

        var first = new Opus55NestingEngine().Solve(Build());
        var second = new Opus55NestingEngine().Solve(Build());

        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void HasPublicParameterlessConstructorForPluginDiscovery()
    {
        var engine = Activator.CreateInstance(typeof(Opus55NestingEngine));
        Assert.IsAssignableFrom<INestingEngine>(engine);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static string Describe(NestJobResult result) =>
        string.Join(
            "|",
            result.Plates.Select(p =>
                p.StockId + ":" + string.Join(",", p.Placements.Select(x => $"{x.PartId}#{x.InstanceIndex}@{x.X:R},{x.Y:R},{x.Rotation:R}"))
            )
        );

    private static void AssertValid(NestJob job, NestJobResult result)
    {
        var materialized = NestResultMaterializer.Materialize(job, result);
        var runs = materialized.Nest.Plates.Select(plate => (Plate: plate, Parts: plate.Parts.ToList())).ToList();
        var requirements = job.Parts.ToDictionary<NestJobPart, Drawing, (string Name, int Quantity)>(
            p => materialized.DrawingsByPartId[p.Id],
            p => (p.Id, p.Quantity),
            ReferenceEqualityComparer.Instance
        );
        var validation = NestValidator.Validate(runs, requirements);
        NestValidator.ValidateAgainstJob(job, result, job.Parts.ToDictionary(p => p.Id, p => p.Id), validation);
        Assert.True(validation.Valid, string.Join(Environment.NewLine, validation.Violations));

        foreach (var f in result.Fulfillment)
            Assert.Equal(f.Requested, f.Placed + f.Unplaced);
    }

    private static NestJob Job(NestJobPart[] parts, NestPlateStock[] stock, NestJobOptions? options = null) =>
        new(parts, stock, options);

    private static NestJobPart Part(string id, Program program, int quantity, RotationPolicy? rotation = null) =>
        new(id, PartGeometrySnapshot.FromProgram(program), quantity, 0, rotation);

    /// <param name="width">Y extent.</param>
    /// <param name="length">X extent.</param>
    private static NestPlateStock Stock(
        string id,
        double width,
        double length,
        double spacing = 0,
        Spacing edge = default,
        int quadrant = 1,
        int? quantity = null
    ) => new(id, new Size(width, length), quantity, spacing, edge, quadrant);

    private static Program Polyline(params (double X, double Y)[] points)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1))
            program.Codes.Add(new LinearMove(x, y));
        program.Codes.Add(new LinearMove(points[0].X, points[0].Y));
        return program;
    }

    private static Program Rectangle(double w, double h) => Polyline((0, 0), (w, 0), (w, h), (0, h));

    private static Program Triangle(double w, double h) => Polyline((0, 0), (w, 0), (w * 0.3, h));

    private static Program LShape(double w, double h, double t) => Polyline((0, 0), (w, 0), (w, t), (t, t), (t, h), (0, h));

    private static Program Disc(double r)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(r, 0));
        program.Codes.Add(new ArcMove(-r, 0, 0, 0, RotationType.CCW));
        program.Codes.Add(new ArcMove(r, 0, 0, 0, RotationType.CCW));
        return program;
    }

    /// <summary>Stadium: two semicircular ends joined by straight sides, offset from the origin.</summary>
    private static Program Obround(double length, double width)
    {
        var r = width / 2;
        var program = new Program();
        program.Codes.Add(new RapidMove(1 + r, 1));
        program.Codes.Add(new LinearMove(1 + length - r, 1));
        program.Codes.Add(new ArcMove(1 + length - r, 1 + width, 1 + length - r, 1 + r, RotationType.CCW));
        program.Codes.Add(new LinearMove(1 + r, 1 + width));
        program.Codes.Add(new ArcMove(1 + r, 1, 1 + r, 1 + r, RotationType.CCW));
        return program;
    }
}
