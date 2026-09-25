using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

[Collection(nameof(FillCacheCollection))]
[Trait("Category", "FillPerformance")]
public class FillPerformanceTests
{
    private readonly ITestOutputHelper output;

    public FillPerformanceTests(ITestOutputHelper output) => this.output = output;

    [SkippableFact]
    public void DefaultComparer_ReportsUnequalCountsAndEqualCountControl()
    {
        // Xunit.SkippableFact 1.4.13 calls its skip-unless API IfNot.
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        var workArea = new Box(0, 0, 256, 256);
        var drawing = new RectangleShape { Length = 2, Width = 1 }.GetDrawing();
        var larger = MakeGrid(drawing, 2048, 4);
        var smaller = MakeGrid(drawing, 2047, 3);
        var equalCountCompact = MakeGrid(drawing, 2048, 3);
        AssertValidRectangles(larger, workArea);
        AssertValidRectangles(smaller, workArea);
        AssertValidRectangles(equalCountCompact, workArea);
        Assert.True(FillScore.Compute(smaller, workArea).Density > FillScore.Compute(larger, workArea).Density);

#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine("Synthetic 2x1 rectangles, 64 columns, work area=(0,0,256,256). "
            + "Larger: 2048 parts, pitch=4; smaller: 2047 parts, pitch=3; equal-count compact: 2048 parts, pitch=3.");
        output.WriteLine("Each batch alternates argument order (half forward, half reverse). "
            + "Actual/reference batch order alternates between repetitions. "
            + "Construction, validation, assertions and output excluded; delegate/loop/result-consumption overhead included. "
            + "Allocations use GC.GetAllocatedBytesForCurrentThread around synchronous calls only. "
            + "These are local microbenchmarks, not timing gates or whole-job speedup estimates.");

        // A large fixed batch also makes the optimized, constant-time path measurable.
        // Keep these inputs and iteration counts identical for before/after measurements.
        ReportCase("unequal-counts", larger, smaller, workArea, 500_000);
        ReportCase("equal-count-control", equalCountCompact, larger, workArea, 10_000);
    }

    private void ReportCase(string name, List<Part> candidate, List<Part> current, Box workArea, int callsPerBatch)
    {
        var comparer = new DefaultFillComparer();
        var actual = new Func<List<Part>, List<Part>, Box, bool>(comparer.IsBetter);
        var reference = new Func<List<Part>, List<Part>, Box, bool>((a, b, area) =>
            FillScore.Compute(a, area) > FillScore.Compute(b, area));
        var expectedForward = reference(candidate, current, workArea);
        var expectedReverse = reference(current, candidate, workArea);
        Assert.Equal(expectedForward, actual(candidate, current, workArea));
        Assert.Equal(expectedReverse, actual(current, candidate, workArea));
        var expectedTrueCount = callsPerBatch / 2 * ((expectedForward ? 1 : 0) + (expectedReverse ? 1 : 0));

        var warmupCallsPerBatch = 50_000;
        var repetitions = 7;
        // Interleaved warmup allows JIT/tiering and cached drawing/bounds access to settle.
        for (var i = 0; i < 2; i++)
        {
            Measure(actual, candidate, current, workArea, warmupCallsPerBatch);
            Measure(reference, candidate, current, workArea, warmupCallsPerBatch);
        }

        output.WriteLine($"{name}: warmup=2 batches x {warmupCallsPerBatch} calls per implementation; "
            + $"measured={repetitions} batches x {callsPerBatch} calls per implementation; "
            + $"expected true results/batch={expectedTrueCount}.");
        var actualSamples = new Sample[repetitions];
        var referenceSamples = new Sample[repetitions];
        for (var i = 0; i < repetitions; i++)
        {
            if (i % 2 == 0)
            {
                actualSamples[i] = Measure(actual, candidate, current, workArea, callsPerBatch);
                referenceSamples[i] = Measure(reference, candidate, current, workArea, callsPerBatch);
            }
            else
            {
                referenceSamples[i] = Measure(reference, candidate, current, workArea, callsPerBatch);
                actualSamples[i] = Measure(actual, candidate, current, workArea, callsPerBatch);
            }
            // Consume measured results and check correctness outside the timed region.
            Assert.Equal(expectedTrueCount, actualSamples[i].TrueCount);
            Assert.Equal(expectedTrueCount, referenceSamples[i].TrueCount);
            output.WriteLine(FormattableString.Invariant(
                $"{name} batch {i + 1}: actual={actualSamples[i].Milliseconds:F6} ms, {actualSamples[i].AllocatedBytes} B; reference={referenceSamples[i].Milliseconds:F6} ms, {referenceSamples[i].AllocatedBytes} B."));
        }
        ReportSummary(name, "actual", actualSamples, callsPerBatch);
        ReportSummary(name, "reference", referenceSamples, callsPerBatch);
    }

    private void ReportSummary(string name, string implementation, Sample[] samples, int callsPerBatch)
    {
        var times = samples.Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
        var bytes = samples.Select(s => s.AllocatedBytes).OrderBy(b => b).ToArray();
        var median = samples.Length / 2;
        output.WriteLine(FormattableString.Invariant(
            $"{name} {implementation}: batch ms min/median/max={times[0]:F6}/{times[median]:F6}/{times[^1]:F6}; ns/call min/median/max={times[0] * 1_000_000 / callsPerBatch:F3}/{times[median] * 1_000_000 / callsPerBatch:F3}/{times[^1] * 1_000_000 / callsPerBatch:F3}; batch bytes min/median/max={bytes[0]}/{bytes[median]}/{bytes[^1]}; B/call min/median/max={(double)bytes[0] / callsPerBatch:F6}/{(double)bytes[median] / callsPerBatch:F6}/{(double)bytes[^1] / callsPerBatch:F6}."));
    }

    private static Sample Measure(Func<List<Part>, List<Part>, Box, bool> compare,
        List<Part> candidate, List<Part> current, Box workArea, int calls)
    {
        var trueCount = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            var forward = i % 2 == 0;
            if (compare(forward ? candidate : current, forward ? current : candidate, workArea))
                trueCount++;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Sample(elapsed * 1000.0 / Stopwatch.Frequency, allocated, trueCount);
    }

    private static List<Part> MakeGrid(Drawing drawing, int count, double pitch)
    {
        var parts = new List<Part>(count);
        for (var i = 0; i < count; i++)
            parts.Add(new Part(drawing, new Vector(i % 64 * pitch, i / 64 * pitch)));
        return parts;
    }

    private static void AssertValidRectangles(List<Part> parts, Box workArea)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            Assert.Equal(2.0, part.BaseDrawing.Area);
            Assert.Equal(2.0, part.BoundingBox.Length);
            Assert.Equal(1.0, part.BoundingBox.Width);
            Assert.True(workArea.Contains(part.BoundingBox));
            foreach (var value in new[] { part.Left, part.Right, part.Bottom, part.Top, part.Rotation })
                Assert.True(double.IsFinite(value));
            for (var j = 0; j < i; j++)
                Assert.False(part.BoundingBox.Intersects(parts[j].BoundingBox));
        }
    }

    private readonly record struct Sample(double Milliseconds, long AllocatedBytes, int TrueCount);
}
