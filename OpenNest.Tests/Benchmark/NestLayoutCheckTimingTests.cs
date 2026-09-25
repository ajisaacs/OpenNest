using System.Diagnostics;
using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using Xunit.Abstractions;

namespace OpenNest.Tests.Benchmark;

public class NestLayoutCheckTimingTests
{
    private readonly ITestOutputHelper output;

    public NestLayoutCheckTimingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ManyPartLayoutMatchesFrozenValidatorAndReportsTiming()
    {
        const int count = 100;
        var program = new Program();
        program.MoveTo(1, 0);
        program.ArcTo(-1, 0, 0, 0, RotationType.CCW);
        program.ArcTo(1, 0, 0, 0, RotationType.CCW);
        var part = new NestJobPart("disc", PartGeometrySnapshot.FromProgram(program), count);
        var stock = new NestPlateStock("sheet", new Size(30, 30), partSpacing: 0.01);
        var job = new NestJob(new[] { part }, new[] { stock });
        var builder = new NestJobResultBuilder(job);
        // Dense bounding-box candidates exercise the pair gate; both overlapping and
        // disjoint diagonal neighbours occur. Violations are deliberate and compared.
        builder.AddSheet(stock, Enumerable.Range(0, count)
            .Select(i => (part.Id, 2 + (i % 10) * 1.5, 2 + (i / 10) * 1.5, 0.0)));
        var result = builder.Build(NestJobStopReason.Completed);
        var materialized = NestResultMaterializer.Materialize(job, result);
        var requirements = job.Parts.ToDictionary(p => materialized.DrawingsByPartId[p.Id], p => (p.Id, p.Quantity));
        var runs = materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList();
        var expected = LegacyNestValidator.Validate(runs, requirements).Violations;
        Assert.NotEmpty(expected);
        Assert.Equal(expected, NestValidator.Validate(runs, requirements).Violations);
        Assert.Equal(expected, NestLayoutCheck.Violations(job, result));

        var legacyTimes = new double[5];
        var currentTimes = new double[5];
        for (var i = 0; i < legacyTimes.Length; i++)
        {
            // Alternate order after warmup to reduce systematic timing bias.
            if (i % 2 == 0)
            {
                legacyTimes[i] = Measure(() => Assert.Equal(expected, LegacyNestValidator.Validate(runs, requirements).Violations));
                currentTimes[i] = Measure(() => Assert.Equal(expected, NestValidator.Validate(runs, requirements).Violations));
            }
            else
            {
                currentTimes[i] = Measure(() => Assert.Equal(expected, NestValidator.Validate(runs, requirements).Violations));
                legacyTimes[i] = Measure(() => Assert.Equal(expected, LegacyNestValidator.Validate(runs, requirements).Violations));
            }
        }
        Array.Sort(legacyTimes);
        Array.Sort(currentTimes);
        output.WriteLine($"100 discs; {expected.Count} identical ordered violations; "
            + $"median of 5: frozen={legacyTimes[2]:F3}ms current={currentTimes[2]:F3}ms; "
            + $"ratio={legacyTimes[2] / currentTimes[2]:F2}x. Includes outline preparation; excludes materialization.");
    }

    private static double Measure(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed.TotalMilliseconds;
    }
}
