using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class PlateFillerContractTests
{
    [Fact]
    public void NestProgressReporter_Report_ClonesPartsAndPreservesReportFields()
    {
        var source = new Part(
            new Drawing("part", TestDrawingFactory.Rectangle(10, 20)),
            new Vector(3, 5)
        );
        source.Rotate(0.5);
        var progress = new CapturingProgress();
        var workArea = new Box(1, 2, 30, 40);

        NestProgressReporter.Report(
            progress,
            new ProgressReport
            {
                Phase = NestPhase.Pairs,
                PlateNumber = 3,
                Parts = new List<Part> { source },
                WorkArea = workArea,
                Description = "candidate preview",
                IsOverallBest = true,
            }
        );

        var reported = Assert.Single(progress.Reports);
        Assert.Equal(NestPhase.Pairs, reported.Phase);
        Assert.Equal(3, reported.PlateNumber);
        Assert.Equal("candidate preview", reported.Description);
        Assert.True(reported.IsOverallBest);
        Assert.Equal(workArea.X, reported.ActiveWorkArea.X);
        Assert.Equal(workArea.Y, reported.ActiveWorkArea.Y);
        Assert.Equal(workArea.Width, reported.ActiveWorkArea.Width);
        Assert.Equal(workArea.Length, reported.ActiveWorkArea.Length);

        var preview = Assert.Single(reported.BestParts);
        Assert.NotSame(source, preview);
        Assert.Same(source.BaseDrawing, preview.BaseDrawing);
        Assert.Equal(source.Location.X, preview.Location.X);
        Assert.Equal(source.Location.Y, preview.Location.Y);
        Assert.Equal(source.Rotation, preview.Rotation);
    }

    [Fact]
    public void NestProgressReporter_Report_DoesNotForwardMissingProgressOrParts()
    {
        var progress = new CapturingProgress();
        var part = new Part(new Drawing("part", TestDrawingFactory.Rectangle()));

        NestProgressReporter.Report(
            null,
            new ProgressReport { Parts = new List<Part> { part } }
        );
        NestProgressReporter.Report(progress, new ProgressReport { Parts = null });
        NestProgressReporter.Report(progress, new ProgressReport { Parts = new List<Part>() });

        Assert.Empty(progress.Reports);
    }

    [Fact]
    public void PlateFillOrchestrator_Nest_UsesThresholdIdentityAndCallerDelegates()
    {
        var plate = new Plate(new Size(100, 100));
        var fillDrawing = new Drawing("duplicate", TestDrawingFactory.Rectangle(10, 10));
        var packDrawing = new Drawing("duplicate", TestDrawingFactory.Rectangle(10, 10));
        var items = new List<NestItem>
        {
            new() { Drawing = fillDrawing, Quantity = 10 },
            new() { Drawing = packDrawing, Quantity = 1 },
        };
        var calls = new List<string>();
        var progress = new CapturingProgress();
        var token = CancellationToken.None;

        var parts = PlateFillOrchestrator.Nest(
            plate,
            items,
            new DefaultFillComparer(),
            (item, workArea, receivedProgress, receivedToken) =>
            {
                calls.Add("fill");
                Assert.Same(progress, receivedProgress);
                Assert.Equal(token, receivedToken);
                Assert.Same(fillDrawing, item.Drawing);
                var placed = new List<Part>();
                for (var i = 0; i < item.Quantity; i++)
                {
                    var part = new Part(item.Drawing);
                    part.Offset(new Vector(i * 10, 0));
                    placed.Add(part);
                }
                return placed;
            },
            (workArea, packItems, receivedProgress, receivedToken) =>
            {
                calls.Add("pack");
                Assert.Same(progress, receivedProgress);
                Assert.Equal(token, receivedToken);
                var packItem = Assert.Single(packItems);
                Assert.Same(packDrawing, packItem.Drawing);
                return new List<Part> { new(packItem.Drawing, new Vector(0, 20)) };
            },
            progress,
            token
        );

        Assert.Equal(new[] { "fill", "pack" }, calls);
        Assert.Equal(11, parts.Count);
        Assert.Equal(10, parts.Count(part => ReferenceEquals(part.BaseDrawing, fillDrawing)));
        Assert.Single(parts.Where(part => ReferenceEquals(part.BaseDrawing, packDrawing)));
        Assert.Equal(0, items[0].Quantity);
        Assert.Equal(0, items[1].Quantity);
    }

    [Fact]
    public void PlateFillOrchestrator_Nest_DoesNotInvokeDelegatesAfterCancellation()
    {
        var plate = new Plate(new Size(100, 100));
        var item = new NestItem
        {
            Drawing = new Drawing("part", TestDrawingFactory.Rectangle(10, 10)),
            Quantity = 10,
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var parts = PlateFillOrchestrator.Nest(
            plate,
            new List<NestItem> { item },
            new DefaultFillComparer(),
            (_, _, _, _) => throw new Xunit.Sdk.XunitException("Fill must not run after cancellation"),
            (_, _, _, _) => throw new Xunit.Sdk.XunitException("Pack must not run after cancellation"),
            null,
            cancellation.Token
        );

        Assert.Empty(parts);
        Assert.Equal(10, item.Quantity);
    }

    private sealed class CapturingProgress : IProgress<NestProgress>
    {
        public List<NestProgress> Reports { get; } = new();

        public void Report(NestProgress value)
        {
            Reports.Add(value);
        }
    }
}
