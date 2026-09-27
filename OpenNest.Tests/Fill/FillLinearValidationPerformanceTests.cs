using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

[Collection(nameof(FillCacheCollection))]
[Trait("Category", "FillPerformance")]
public class FillLinearValidationPerformanceTests
{
    private readonly ITestOutputHelper output;

    public FillLinearValidationPerformanceTests(ITestOutputHelper output) => this.output = output;

    [SkippableFact]
    public void FillLinearValidation_ReportsStripeAndControls()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        // Self-contained, baseline-API-only harness: copy this file byte-for-byte into
        // the before tree. Never time a frozen oracle or an overlap-only helper here.
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        foreach (var point in new[] { new Vector(10, 0), new Vector(10, 3), new Vector(4, 3),
            new Vector(4, 8), new Vector(0, 8), new Vector(0, 0) })
            program.Codes.Add(new LinearMove(point));
        var drawing = new Drawing("concave", program);
        var profile = new ShapeProfile(ConvertProgram.ToGeometry(program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());
        Assert.True(profile.Perimeter.IsClosed());
        Assert.Empty(profile.Cutouts);
        Assert.Equal(50.0, drawing.Area);
        Assert.True(profile.Perimeter.Area() > 0);
        var first = Part.CreateAtOrigin(drawing, 0);
        var second = Part.CreateAtOrigin(drawing, System.Math.PI);
        second.Offset(new Vector(10.5, 0));
        var pair = FillHelpers.BuildRotatedPattern(new List<Part> { first, second }, 0.37);
        var single = FillHelpers.BuildRotatedPattern(new List<Part> { first }, 0.37);
        var patterns = new[] { pair, pair, pair, pair, single };
        var names = new[] { "horizontal-stripe", "vertical-stripe", "full-grid", "partial-only", "single-seed-stripe" };
        var counts = new[] { 8, 19, 36, 29, 8 };
        var directions = new[] { NestDirection.Horizontal, NestDirection.Vertical, NestDirection.Horizontal,
            NestDirection.Vertical, NestDirection.Horizontal };
        var areas = new[]
        {
            new Box(0, 0, 96, pair.BoundingBox.Width),
            new Box(0, 0, pair.BoundingBox.Length, 96),
            new Box(0, 0, 96, 48),
            new Box(0, 0, 1.8 * pair.BoundingBox.Length, 96),
            new Box(0, 0, 96, single.BoundingBox.Width),
        };
        var fills = areas.Select((area, mode) =>
        {
            var filler = new FillLinear(area, 0.5);
            return new Func<List<Part>>(() => filler.Fill(patterns[mode], directions[mode]));
        }).ToArray();
        var expected = fills.Select(fill => fill()).ToArray();
        for (var mode = 0; mode < fills.Length; mode++)
        {
            Assert.Equal(counts[mode], expected[mode].Count);
            AssertValid(expected[mode], areas[mode]);
        }
        var warmupCalls = 100;
        var callsPerBatch = 200;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine("linear-validation: closed concave (0,0)-(10,0)-(10,3)-(4,3)-(4,8)-(0,8)-(0,0); "
            + "pair at 0/PI, second offset=(10.5,0), BuildRotatedPattern=0.37; spacing=0.5; origin=(0,0). "
            + "Stripe primary span=96, perpendicular span=exact pattern bbox; full-grid=(96,48); "
            + "vertical partial-only perpendicular span=1.8*pair.Length; single rotated seed H stripe. "
            + "Counts in mode order=8,19,36,29,8; full-grid row=8+28; partial-only row=19+10.");
        output.WriteLine($"warmup=2 x {warmupCalls} calls/mode; measured={repetitions} x {callsPerBatch} calls/mode. "
            + "Mode order alternates forward/reverse in warmup and measurement. Real synchronous production Fill only; "
            + "setup, correctness assertions and output excluded. Geometry, tiling, overlap checks, GC, delegate/loop, "
            + "count accumulation and last-result assignment included identically in every mode. "
            + "Allocations=GC.GetAllocatedBytesForCurrentThread, not RSS. Warm JIT/drawing, no forced GC. "
            + "Baseline harness only: no reduced-work assertion, timing gate or whole-job speedup claim.");

        for (var batch = 0; batch < 2; batch++)
            RunBatch("warmup", batch, warmupCalls);
        for (var batch = 0; batch < repetitions; batch++)
            RunBatch("measured", batch, callsPerBatch);

        void RunBatch(string phase, int batch, int calls)
        {
            // Capture all modes first; assertions/output cannot enter any timed window.
            var samples = new Sample[fills.Length];
            for (var slot = 0; slot < fills.Length; slot++)
            {
                var mode = batch % 2 == 0 ? slot : fills.Length - 1 - slot;
                samples[mode] = Measure(fills[mode], calls);
            }
            for (var mode = 0; mode < fills.Length; mode++)
            {
                var sample = samples[mode];
                Assert.Equal((long)calls * counts[mode], sample.PartCount);
                AssertSameLayout(expected[mode], sample.LastResult);
                AssertValid(sample.LastResult, areas[mode]);
                output.WriteLine(FormattableString.Invariant(
                    $"linear-validation phase={phase} mode={names[mode]} batch={batch + 1} order={(batch % 2 == 0 ? "forward" : "reverse")} calls={calls}: ticks={sample.ElapsedTicks}; ms={sample.ElapsedTicks * 1000.0 / Stopwatch.Frequency:R}; bytes={sample.AllocatedBytes}; parts={sample.PartCount}; last={sample.LastResult.Count}."));
            }
        }
    }

    private static Sample Measure(Func<List<Part>> fill, int calls)
    {
        var count = 0L;
        var last = new List<Part>();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            last = fill();
            count += last.Count;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Sample(elapsed, allocated, count, last);
    }

    private static void AssertValid(List<Part> parts, Box area)
    {
        Assert.All(parts, p =>
        {
            Assert.True(p.BaseDrawing.Area > 0);
            Assert.True(p.Left >= area.Left - OpenNest.Math.Tolerance.Epsilon
                && p.Right <= area.Right + OpenNest.Math.Tolerance.Epsilon
                && p.Bottom >= area.Bottom - OpenNest.Math.Tolerance.Epsilon
                && p.Top <= area.Top + OpenNest.Math.Tolerance.Epsilon);
        });
        Assert.False(FillHelpers.HasOverlappingParts(parts));
    }

    private static void AssertSameLayout(List<Part> expected, List<Part> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i].BaseDrawing, actual[i].BaseDrawing);
            Assert.Same(expected[i].Program, actual[i].Program);
            Assert.Equal(PartBits(expected[i]), PartBits(actual[i]));
        }
    }

    private static long[] PartBits(Part part) => new[] { part.Location.X, part.Location.Y, part.Rotation,
        part.BoundingBox.X, part.BoundingBox.Y, part.BoundingBox.Length, part.BoundingBox.Width }
        .Select(BitConverter.DoubleToInt64Bits).ToArray();

    private readonly record struct Sample(long ElapsedTicks, long AllocatedBytes, long PartCount, List<Part> LastResult);
}
