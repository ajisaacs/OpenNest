using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
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

    [SkippableFact]
    public void GroupPattern_ReportsDefaultAndCustomComparer()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        var group = new List<Part>
        {
            new(new RectangleShape { Length = 2, Width = 1 }.GetDrawing(), new Vector(11, 13)),
            new(new RectangleShape { Length = 1, Width = 2 }.GetDrawing(), new Vector(13.5, 14.5)),
        };
        var workArea = new Box(3, 5, 5, 9);
        var engine = new FillLinear(workArea, 0.25);
        // One angle avoids cross-worker ConcurrentBag tie-order ambiguity, while still
        // exercising the real Parallel.ForEach, both fills, bag and selection path.
        var angles = new List<double> { 0 };
        var customComparer = new FewerPartsComparer();
        var pattern = FillHelpers.BuildRotatedPattern(group, 0);
        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);
        Assert.Equal(8, h.Count);
        Assert.Equal(7, v.Count);
        Assert.True(FillScore.Compute(h, workArea) > FillScore.Compute(v, workArea));
        Assert.True(customComparer.IsBetter(v, h, workArea));
        AssertGroupLayout(h, FillHelpers.FillPattern(engine, group, angles, workArea), workArea);
        AssertGroupLayout(v, FillHelpers.FillPattern(engine, group, angles, workArea, customComparer), workArea);
        var defaultFill = new Func<List<Part>>(() => FillHelpers.FillPattern(engine, group, angles, workArea));
        var customFill = new Func<List<Part>>(() => FillHelpers.FillPattern(engine, group, angles, workArea, customComparer));
        var warmupCalls = 2_000;
        var callsPerBatch = 20_000;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine("group-pattern: synthetic 2x1 at (11,13) and 1x2 at (13.5,14.5); "
            + "work area=(3,5,5,9); spacing=0.25; angle=0 radians; horizontal=8, vertical=7 parts. "
            + "Default scoring selects horizontal; custom fewer-parts comparer selects vertical.");
        output.WriteLine($"group-pattern: warmup=2 batches x {warmupCalls} calls per mode; "
            + $"measured={repetitions} batches x {callsPerBatch} calls per mode; "
            + "default/custom batch order alternates, including warmup. "
            + "Actual production FillPattern only; no reference/approximation inside timing. "
            + "Setup, correctness/layout checks and output excluded; fill geometry, scheduling, "
            + "result construction, selection, GC, delegate/loop and count consumption included. "
            + "Allocation measurement omitted: fills use parallel workers, so current-thread bytes would be incomplete. "
            + "Not a timing gate or a whole-job benchmark.");

        for (var i = 0; i < 2; i++)
        {
            MeasureGroupPattern(i % 2 == 0 ? defaultFill : customFill, warmupCalls);
            MeasureGroupPattern(i % 2 == 0 ? customFill : defaultFill, warmupCalls);
        }

        var defaultSamples = new GroupPatternSample[repetitions];
        var customSamples = new GroupPatternSample[repetitions];
        for (var i = 0; i < repetitions; i++)
        {
            if (i % 2 == 0)
            {
                defaultSamples[i] = MeasureGroupPattern(defaultFill, callsPerBatch);
                customSamples[i] = MeasureGroupPattern(customFill, callsPerBatch);
            }
            else
            {
                customSamples[i] = MeasureGroupPattern(customFill, callsPerBatch);
                defaultSamples[i] = MeasureGroupPattern(defaultFill, callsPerBatch);
            }
            Assert.Equal((long)callsPerBatch * h.Count, defaultSamples[i].PartCount);
            Assert.Equal((long)callsPerBatch * v.Count, customSamples[i].PartCount);
            AssertGroupLayout(h, defaultSamples[i].LastResult, workArea);
            AssertGroupLayout(v, customSamples[i].LastResult, workArea);
            output.WriteLine(FormattableString.Invariant(
                $"group-pattern batch {i + 1}: default={defaultSamples[i].Milliseconds:F6} ms; custom={customSamples[i].Milliseconds:F6} ms; default parts={defaultSamples[i].PartCount}; custom parts={customSamples[i].PartCount}."));
        }
        ReportGroupSummary("default", defaultSamples, callsPerBatch);
        ReportGroupSummary("custom", customSamples, callsPerBatch);
    }

    [SkippableFact]
    public void RotatedPattern_ReportsBoundsConstruction()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");
        var drawing = FillExtentsTests.MakeFixture("arc");
        var group = Enumerable.Range(0, 32).Select(i =>
            new Part(drawing, new Vector(11.25 + i % 8 * 12, 13.5 + i / 8 * 10))).ToList();
        var angles = new[] { 0.0, 0.37 };
        var builds = angles.Select(angle => new Func<List<Part>>(() =>
            FillHelpers.BuildRotatedPattern(group, angle).Parts)).ToArray();
        var expected = angles.Select(angle =>
            OpenNest.Tests.Strategies.FillHelpersTests.PreChangeRotatedPattern(group, angle).Parts).ToArray();
        var warmupCalls = 1_000;
        var callsPerBatch = 5_000;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine($"rotated-pattern: 32 native-arc parts on an 8-column 12x10 grid from (11.25,13.5); "
            + $"angle=0 or 0.37; warmup=2 x {warmupCalls}; measured={repetitions} x {callsPerBatch}; "
            + "angle batch order alternates. Real synchronous production construction only; setup, reference, "
            + "assertions/output excluded; clone/rotation/aggregate bounds, GC and count consumption included. "
            + "Warm drawing/JIT; no forced GC/cache reset. Current-thread allocations, not RSS or a whole-job benchmark.");
        for (var mode = 0; mode < builds.Length; mode++)
            FillExtentsTests.AssertSameLayout(expected[mode], builds[mode]());
        for (var batch = 0; batch < 2; batch++)
            for (var slot = 0; slot < builds.Length; slot++)
                MeasureExtents(builds[(slot + batch) % builds.Length], warmupCalls);
        var samples = angles.Select(_ => new ExtentsSample[repetitions]).ToArray();
        for (var batch = 0; batch < repetitions; batch++)
        {
            for (var slot = 0; slot < builds.Length; slot++)
            {
                var mode = (slot + batch) % builds.Length;
                samples[mode][batch] = MeasureExtents(builds[mode], callsPerBatch);
            }
            for (var mode = 0; mode < builds.Length; mode++)
            {
                var sample = samples[mode][batch];
                Assert.Equal((long)callsPerBatch * group.Count, sample.PartCount);
                FillExtentsTests.AssertSameLayout(expected[mode], sample.LastResult);
                output.WriteLine(FormattableString.Invariant(
                    $"rotated-pattern angle={angles[mode]} batch={batch + 1}: ms={sample.Milliseconds:F6}; bytes={sample.AllocatedBytes}; parts={sample.PartCount}."));
            }
        }
        for (var mode = 0; mode < builds.Length; mode++)
        {
            var times = samples[mode].Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
            var bytes = samples[mode].Select(s => s.AllocatedBytes).OrderBy(b => b).ToArray();
            output.WriteLine(FormattableString.Invariant(
                $"rotated-pattern angle={angles[mode]}: batch ms min/median/max={times[0]:F6}/{times[repetitions / 2]:F6}/{times[^1]:F6}; us/call min/median/max={times[0] * 1000 / callsPerBatch:F3}/{times[repetitions / 2] * 1000 / callsPerBatch:F3}/{times[^1] * 1000 / callsPerBatch:F3}; batch bytes min/median/max={bytes[0]}/{bytes[repetitions / 2]}/{bytes[^1]}; B/call={(double)bytes[repetitions / 2] / callsPerBatch:F3}."));
        }
    }

    [SkippableFact]
    public void Extents_ReportsRepeatedColumnRebuilds()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        var area = new Box(3, 5, 45, 27);
        var drawing = FillExtentsTests.MakeFixture("triangle");
        var spacings = new[] { 0.0, 0.5 };
        var fills = spacings.Select(spacing =>
        {
            var filler = new FillExtents(area, spacing);
            return new Func<List<Part>>(() => filler.Fill(drawing));
        }).ToArray();
        var expected = spacings.Select(spacing => new LegacyFillExtents(area, spacing).Fill(drawing)).ToArray();
        for (var i = 0; i < fills.Length; i++)
        {
            Assert.Equal(24, expected[i].Count);
            FillExtentsTests.AssertSameLayout(expected[i], fills[i]());
            FillExtentsTests.AssertValidLayout(expected[i], area);
        }
        var warmupCalls = 50;
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
        output.WriteLine("extents: synthetic closed right triangle (0,0)-(10,0)-(0,8)-(0,0); "
            + "area=(3,5,45,27); angle=0; spacing=0 or 0.5; 24 parts/fill. "
            + "The matching Debug work test proves 2 BuildColumn calls/fill (initial + adjustment). "
            + "Real synchronous production Fill, no reflection/reference inside timing. "
            + "Setup, assertions and output excluded; geometry, tiling, adjustment, overlap fallback, GC, "
            + "delegate/loop/count consumption included. Allocations=GC.GetAllocatedBytesForCurrentThread "
            + "around synchronous calls, not RSS. Warm source drawing/JIT, no forced GC or cache reset. "
            + "Not a timing gate, isolated BuildColumn latency, or whole-job benchmark.");
        output.WriteLine($"extents: warmup=2 batches x {warmupCalls} calls per spacing; "
            + $"measured={repetitions} batches x {callsPerBatch} calls per spacing; "
            + "spacing order alternates in warmup and measurement.");

        for (var batch = 0; batch < 2; batch++)
            for (var slot = 0; slot < fills.Length; slot++)
                MeasureExtents(fills[(slot + batch) % fills.Length], warmupCalls);

        var samples = spacings.Select(_ => new ExtentsSample[repetitions]).ToArray();
        for (var batch = 0; batch < repetitions; batch++)
        {
            for (var slot = 0; slot < fills.Length; slot++)
            {
                var mode = (slot + batch) % fills.Length;
                samples[mode][batch] = MeasureExtents(fills[mode], callsPerBatch);
            }
            for (var mode = 0; mode < fills.Length; mode++)
            {
                var sample = samples[mode][batch];
                Assert.Equal((long)callsPerBatch * expected[mode].Count, sample.PartCount);
                FillExtentsTests.AssertSameLayout(expected[mode], sample.LastResult);
                FillExtentsTests.AssertValidLayout(sample.LastResult, area);
                output.WriteLine(FormattableString.Invariant(
                    $"extents spacing={spacings[mode]} batch={batch + 1}: ms={sample.Milliseconds:F6}; bytes={sample.AllocatedBytes}; parts={sample.PartCount}."));
            }
        }
        for (var mode = 0; mode < fills.Length; mode++)
        {
            var times = samples[mode].Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
            var bytes = samples[mode].Select(s => s.AllocatedBytes).OrderBy(b => b).ToArray();
            var median = repetitions / 2;
            output.WriteLine(FormattableString.Invariant(
                $"extents spacing={spacings[mode]}: batch ms min/median/max={times[0]:F6}/{times[median]:F6}/{times[^1]:F6}; us/call min/median/max={times[0] * 1000 / callsPerBatch:F3}/{times[median] * 1000 / callsPerBatch:F3}/{times[^1] * 1000 / callsPerBatch:F3}; batch bytes min/median/max={bytes[0]}/{bytes[median]}/{bytes[^1]}; B/call min/median/max={(double)bytes[0] / callsPerBatch:F3}/{(double)bytes[median] / callsPerBatch:F3}/{(double)bytes[^1] / callsPerBatch:F3}."));
        }
    }

    [SkippableFact]
    public void FeatureExtraction_ReportsFullAndScalarOnly()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        var drawing = new RingShape { OuterDiameter = 20, InnerDiameter = 8 }.GetDrawing();
        var full = new Func<OpenNest.Engine.ML.PartFeatures>(() => OpenNest.Engine.ML.FeatureExtractor.Extract(drawing));
        var scalar = new Func<OpenNest.Engine.ML.PartFeatures>(() => OpenNest.Engine.ML.FeatureExtractor.Extract(drawing, includeBitmask: false));
        var fullBaseline = full();
        var scalarBaseline = scalar();
        Assert.NotNull(fullBaseline.Bitmask);
        Assert.Null(scalarBaseline.Bitmask);
        var expectedOnes = fullBaseline.Bitmask.Count(cell => cell == 1);
        // Perimeter-only rasterization of the circle silhouette leaves corners clear but center set.
        Assert.InRange(expectedOnes, 1, BitmaskCells - 1);
        var warmupCalls = 200;
        var callsPerBatch = 1_000;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine("feature-extraction: synthetic ring OD=20 ID=8 (perimeter + one circular cutout); "
            + $"full=default overload (32x32 bitmask) vs scalar-only=includeBitmask:false; warmup=2 x {warmupCalls}; "
            + $"measured={repetitions} x {callsPerBatch}; mode batch order alternates. "
            + "Real synchronous production extraction only; setup/assertions/output excluded; canonical copy, "
            + "geometry conversion, hull, bitmask scan (full mode only), GC and result consumption included. "
            + "The per-call bitmap-count consumption also runs inside the window and allocates only in full mode. "
            + "Current-thread allocations, not RSS or a whole-job benchmark.");
        for (var batch = 0; batch < 2; batch++)
        {
            MeasureFeature(batch % 2 == 0 ? full : scalar, warmupCalls);
            MeasureFeature(batch % 2 == 0 ? scalar : full, warmupCalls);
        }
        var samples = new (FeatureSample Full, FeatureSample Scalar)[repetitions];
        for (var batch = 0; batch < repetitions; batch++)
        {
            if (batch % 2 == 0)
            {
                samples[batch].Full = MeasureFeature(full, callsPerBatch);
                samples[batch].Scalar = MeasureFeature(scalar, callsPerBatch);
            }
            else
            {
                samples[batch].Scalar = MeasureFeature(scalar, callsPerBatch);
                samples[batch].Full = MeasureFeature(full, callsPerBatch);
            }
            var fullSample = samples[batch].Full;
            var scalarSample = samples[batch].Scalar;
            Assert.Equal((long)expectedOnes * callsPerBatch, fullSample.BitmaskOnes);
            Assert.Equal(0, scalarSample.BitmaskOnes);
            Assert.Equal(scalarBaseline.Area, scalarSample.Area);
            Assert.Equal(scalarBaseline.Area, fullSample.Area);
            output.WriteLine(FormattableString.Invariant(
                $"feature-extraction batch={batch + 1}: full ms={samples[batch].Full.Milliseconds:F6} bytes={samples[batch].Full.AllocatedBytes}."));
            output.WriteLine(FormattableString.Invariant(
                $"feature-extraction batch={batch + 1}: scalar ms={samples[batch].Scalar.Milliseconds:F6} bytes={samples[batch].Scalar.AllocatedBytes}."));
        }
        ReportFeatureSummary("full", samples.Select(s => s.Full).ToArray(), callsPerBatch);
        ReportFeatureSummary("scalar-only", samples.Select(s => s.Scalar).ToArray(), callsPerBatch);
    }

    [SkippableFact]
    public void IrregularAngles_ReportsWarmNoModelPath()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");

        // Never move/delete a user's model to obtain a no-model measurement.
        var modelPath = Path.Combine(
            Path.GetDirectoryName(typeof(OpenNest.Engine.ML.AnglePredictor).Assembly.Location)!,
            "Models", "angle_predictor.onnx");
        Skip.If(File.Exists(modelPath), "No-model measurement requires an output directory without an angle model.");
        Assert.Null(OpenNest.Engine.ML.AnglePredictor.PredictAngles(new OpenNest.Engine.ML.PartFeatures(), 80, 120));

        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        foreach (var point in new[] { new Vector(20, 0), new Vector(20, 6), new Vector(8, 6),
            new Vector(8, 14), new Vector(0, 14), new Vector(0, 0) })
            program.Codes.Add(new OpenNest.CNC.LinearMove(point));
        var item = new NestItem { Drawing = new Drawing("performance-L", program) };
        var workArea = new Box(3, 5, 120, 80);
        var classification = new ClassificationResult { Type = PartType.Irregular, PrimaryAngle = 0.13 };
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var build = new Func<List<double>>(() => builder.Build(item, classification, workArea));
        // Independent pre-4b fallback expression; exact ordered equality, not only count.
        var expected = new List<double> { classification.PrimaryAngle, classification.PrimaryAngle + OpenNest.Math.Angle.HalfPI };
        for (var angle = 0.0; angle < System.Math.PI; angle += OpenNest.Math.Angle.ToRadians(5))
        {
            if (!expected.Any(existing => OpenNest.Math.Tolerance.IsEqualTo(existing, angle)))
                expected.Add(angle);
        }
        Assert.Equal(expected, build());
        var warmupCalls = 20_000;
        var callsPerBatch = 20_000;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine("no-model angles: concave L (0,0)-(20,0)-(20,6)-(8,6)-(8,14)-(0,14), "
            + "primary=0.13 rad, workArea=(3,5,120,80), ForceFullSweep=true; public production builder, no delegates replaced. "
            + $"Initialization completed outside timing; warmup=2 x {warmupCalls}, measured={repetitions} x {callsPerBatch}. "
            + "Synchronous current-thread allocations; construction/assertions/output excluded, loop/result consumption included. "
            + "Warm missing-model branch only, not ONNX inference, cold-start latency, or whole-job speedup.");
        for (var batch = 0; batch < 2; batch++)
            MeasureAngles(build, warmupCalls);
        var samples = new AngleSample[repetitions];
        for (var batch = 0; batch < repetitions; batch++)
        {
            var sample = samples[batch] = MeasureAngles(build, callsPerBatch);
            Assert.Equal((long)expected.Count * callsPerBatch, sample.AngleCount);
            Assert.Equal(expected, sample.LastResult);
            output.WriteLine(FormattableString.Invariant(
                $"no-model-angles batch={batch + 1}: ms={sample.Milliseconds:F6} bytes={sample.AllocatedBytes}."));
        }
        var times = samples.Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
        var bytes = samples.Select(s => s.AllocatedBytes).OrderBy(b => b).ToArray();
        var median = repetitions / 2;
        output.WriteLine(FormattableString.Invariant(
            $"no-model-angles: batch ms min/median/max={times[0]:F6}/{times[median]:F6}/{times[^1]:F6}; us/call min/median/max={times[0] * 1000 / callsPerBatch:F3}/{times[median] * 1000 / callsPerBatch:F3}/{times[^1] * 1000 / callsPerBatch:F3}; B/call min/median/max={(double)bytes[0] / callsPerBatch:F3}/{(double)bytes[median] / callsPerBatch:F3}/{(double)bytes[^1] / callsPerBatch:F3}."));
    }

    private static AngleSample MeasureAngles(Func<List<double>> build, int calls)
    {
        var count = 0L;
        var last = new List<double>();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            last = build();
            count += last.Count;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new AngleSample(elapsed * 1000.0 / Stopwatch.Frequency, allocated, count, last);
    }

    private readonly record struct AngleSample(double Milliseconds, long AllocatedBytes,
        long AngleCount, List<double> LastResult);

    private const int BitmaskCells = 32 * 32;

    private static FeatureSample MeasureFeature(Func<OpenNest.Engine.ML.PartFeatures> extract, int calls)
    {
        var ones = 0L;
        var lastArea = 0.0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            var features = extract();
            ones += features.Bitmask?.Count(cell => cell == 1) ?? 0;
            lastArea = features.Area;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new FeatureSample(elapsed * 1000.0 / Stopwatch.Frequency, allocated, ones, lastArea);
    }

    private void ReportFeatureSummary(string mode, FeatureSample[] samples, int callsPerBatch)
    {
        var times = samples.Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
        var bytes = samples.Select(s => s.AllocatedBytes).OrderBy(b => b).ToArray();
        var median = samples.Length / 2;
        output.WriteLine(FormattableString.Invariant(
            $"feature-extraction {mode}: batch ms min/median/max={times[0]:F6}/{times[median]:F6}/{times[^1]:F6}; us/call min/median/max={times[0] * 1000 / callsPerBatch:F3}/{times[median] * 1000 / callsPerBatch:F3}/{times[^1] * 1000 / callsPerBatch:F3}; batch bytes min/median/max={bytes[0]}/{bytes[median]}/{bytes[^1]}; B/call min/median/max={(double)bytes[0] / callsPerBatch:F3}/{(double)bytes[median] / callsPerBatch:F3}/{(double)bytes[^1] / callsPerBatch:F3}."));
    }

    private readonly record struct FeatureSample(double Milliseconds, long AllocatedBytes, long BitmaskOnes, double Area);

    private static ExtentsSample MeasureExtents(Func<List<Part>> fill, int calls)
    {
        var partCount = 0L;
        var last = new List<Part>();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            last = fill();
            partCount += last.Count;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new ExtentsSample(elapsed * 1000.0 / Stopwatch.Frequency, allocated, partCount, last);
    }

    private readonly record struct ExtentsSample(double Milliseconds, long AllocatedBytes,
        long PartCount, List<Part> LastResult);

    private static GroupPatternSample MeasureGroupPattern(Func<List<Part>> fill, int calls)
    {
        var partCount = 0L;
        var last = new List<Part>();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
        {
            last = fill();
            partCount += last.Count;
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        return new GroupPatternSample(elapsed * 1000.0 / Stopwatch.Frequency, partCount, last);
    }

    private void ReportGroupSummary(string mode, GroupPatternSample[] samples, int callsPerBatch)
    {
        var times = samples.Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
        var median = samples.Length / 2;
        output.WriteLine(FormattableString.Invariant(
            $"group-pattern {mode}: batch ms min/median/max={times[0]:F6}/{times[median]:F6}/{times[^1]:F6}; us/call min/median/max={times[0] * 1000 / callsPerBatch:F3}/{times[median] * 1000 / callsPerBatch:F3}/{times[^1] * 1000 / callsPerBatch:F3}."));
    }

    private static void AssertGroupLayout(List<Part> expected, List<Part> actual, Box workArea)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            var part = actual[i];
            Assert.Same(expected[i].BaseDrawing, part.BaseDrawing);
            Assert.Equal(expected[i].Location, part.Location);
            Assert.Equal(expected[i].Rotation, part.Rotation);
            Assert.Equal(2.0, part.BaseDrawing.Area);
            Assert.True(workArea.Contains(part.BoundingBox));
            foreach (var value in new[] { part.Left, part.Right, part.Bottom, part.Top, part.Rotation })
                Assert.True(double.IsFinite(value));
            for (var j = 0; j < i; j++)
                Assert.False(part.BoundingBox.Intersects(actual[j].BoundingBox));
        }
    }

    private sealed class FewerPartsComparer : IFillComparer
    {
        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea) =>
            candidate.Count < current.Count;
    }

    private readonly record struct GroupPatternSample(double Milliseconds, long PartCount, List<Part> LastResult);

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
