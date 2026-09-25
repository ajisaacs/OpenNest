using OpenNest.Benchmark;
using OpenNest.Engine.Jobs;

namespace OpenNest.Tests.Benchmark;

public sealed class JobProgressLogTests
{
    private static NestJobProgress Evaluating(int plateIndex = 0) =>
        new(NestJobStage.EvaluatingCandidate, "stock", plateIndex, 0, 0);

    [Fact]
    public void Report_ThrottlesEvaluatingButAlwaysWritesCommits()
    {
        var writer = new StringWriter();
        var now = TimeSpan.Zero;
        var log = new JobProgressLog(writer, "job/engine", TimeSpan.FromSeconds(2), () => now);

        log.Report(Evaluating());
        log.Report(Evaluating());
        log.Report(new NestJobProgress(NestJobStage.PlateCommitted, "stock", 0, 1, 5));
        now = TimeSpan.FromSeconds(3);
        log.Report(Evaluating(1));

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "[job/engine] evaluating plate 1 on stock stock (0 plate(s), 0 parts committed)",
                "[job/engine] committed plate 1 on stock stock (5 parts placed)",
                "[job/engine] evaluating plate 2 on stock stock (0 plate(s), 0 parts committed)",
            ],
            lines
        );
    }

    [Fact]
    public void Report_UnknownPlateIndexFallsBackToNextCommittedPlate()
    {
        var writer = new StringWriter();
        var log = new JobProgressLog(writer, "j/e");

        log.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", -1, 2, 9));

        Assert.Contains("evaluating plate 3 on stock s", writer.ToString());
    }
}
