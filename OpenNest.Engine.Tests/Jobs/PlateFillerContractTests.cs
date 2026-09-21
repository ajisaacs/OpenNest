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

    private sealed class CapturingProgress : IProgress<NestProgress>
    {
        public List<NestProgress> Reports { get; } = new();

        public void Report(NestProgress value)
        {
            Reports.Add(value);
        }
    }
}
