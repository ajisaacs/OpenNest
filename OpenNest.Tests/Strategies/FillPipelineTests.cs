using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Tests.Strategies;

public class FillPipelineTests
{
    [Fact]
    public void Pipeline_PopulatesPhaseResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        filler.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(
            filler.PhaseResults.Count >= FillStrategyRegistry.Strategies.Count,
            $"Expected phase results from all active strategies, got {filler.PhaseResults.Count}"
        );
    }

    [Fact]
    public void Pipeline_SetsWinnerPhase()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0);
        Assert.True(
            filler.WinnerPhase == NestPhase.Pairs
                || filler.WinnerPhase == NestPhase.Linear
                || filler.WinnerPhase == NestPhase.RectBestFit
                || filler.WinnerPhase == NestPhase.Extents
                || filler.WinnerPhase == NestPhase.Custom
        );
    }

    [Fact]
    public void Pipeline_RespectsCancellation()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        // Pre-canceled token should return empty or partial results without throwing
        var parts = filler.Fill(item, plate.WorkArea(), null, cts.Token);

        // Should not throw — graceful degradation
        Assert.NotNull(parts);
    }

    [Fact]
    public void Pipeline_ReportsBoxLayoutBeforeBestFitPairs()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var reports = new List<NestProgress>();

        filler.Fill(item, plate.WorkArea(), new SyncProgress(reports.Add), CancellationToken.None);

        var first = reports.First();
        Assert.True(first.IsOverallBest);
        Assert.Equal(NestPhase.RectBestFit, first.Phase);
        Assert.NotEmpty(first.BestParts);
    }

    [Fact]
    public void Pipeline_ReportsStatusWhileComputingBestFitPairs()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var reports = new List<NestProgress>();

        filler.Fill(item, plate.WorkArea(), new SyncProgress(reports.Add), CancellationToken.None);

        var status = Assert.Single(reports, p => p.Description?.StartsWith("Computing best-fit pairs") == true);
        Assert.Equal(NestPhase.Pairs, status.Phase);
        Assert.False(status.IsOverallBest);
        Assert.Equal(reports.First().BestParts.Count, status.BestParts.Count);
    }

    [Fact]
    public void Pipeline_AcceptedBeforeBestFitPairs_ReturnsBoxLayout()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        using var cts = new CancellationTokenSource();
        NestProgress first = null;

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            new SyncProgress(p =>
            {
                first ??= p;
                cts.Cancel();
            }),
            cts.Token
        );

        Assert.Equal(NestPhase.RectBestFit, filler.WinnerPhase);
        Assert.Equal(first.BestParts.Count, parts.Count);
    }

    [Fact]
    public void Pipeline_ShownLayoutNeverRegressesBelowBoxLayout()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var shown = new List<int>();

        filler.Fill(
            item,
            plate.WorkArea(),
            new SyncProgress(p =>
            {
                if (p.IsOverallBest)
                    shown.Add(p.BestParts.Count);
            }),
            CancellationToken.None
        );

        for (var i = 1; i < shown.Count; i++)
            Assert.True(shown[i] >= shown[0], $"Report {i} showed {shown[i]} < {shown[0]} parts");
    }

    private sealed class SyncProgress(Action<NestProgress> report) : IProgress<NestProgress>
    {
        public void Report(NestProgress value) => report(value);
    }
}
