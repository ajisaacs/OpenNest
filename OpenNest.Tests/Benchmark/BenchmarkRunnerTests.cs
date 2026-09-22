using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.IO;

namespace OpenNest.Tests.Benchmark;

public sealed class BenchmarkRunnerTests : IDisposable
{
    private static readonly string SourceDxf = Path.Combine(
        "Bending",
        "TestData",
        "4526 A14 PT11.dxf"
    );

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "opennest-runner-" + Guid.NewGuid().ToString("N")
    );

    public BenchmarkRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(SourceDxf, Path.Combine(_dir, "part.dxf"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private List<BenchmarkJob> LoadJob()
    {
        var manifest = Path.Combine(_dir, "job.json");
        File.WriteAllText(
            manifest,
            """{ "sheetSizes": ["48x96"], "parts": [ { "dxf": "part.dxf", "quantity": 2 } ] }"""
        );
        return JobLoader.Load(manifest);
    }

    private string WriteBaselineNest(
        string name,
        double salvageRate = 0.5,
        bool outsideWorkArea = false,
        int plateQuantity = 1,
        int requiredQuantity = 1,
        int partCount = 1,
        double partSpacing = 0,
        double partRotation = 0
    )
    {
        var path = Path.Combine(_dir, name + ".nest");
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(2, 0);
        program.LineTo(2, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        var drawing = new Drawing("part", program);
        if (partRotation != 0)
        {
            drawing.Constraints.StepAngle = System.Math.PI / 2;
            drawing.Constraints.StartAngle = 0;
            drawing.Constraints.EndAngle = 0;
        }
        drawing.Quantity.Required = requiredQuantity;
        var nest = new Nest(name) { SalvageRate = salvageRate };
        nest.Drawings.Add(drawing);
        var plate = new Plate(10, 10) { Quantity = plateQuantity, PartSpacing = partSpacing };
        for (var index = 0; index < partCount; index++)
        {
            var part = Part.CreateAtOrigin(drawing);
            if (partRotation != 0)
                part.Rotate(partRotation);
            part.Location =
                outsideWorkArea ? new OpenNest.Geometry.Vector(9, 0)
                : partRotation == 0 ? new OpenNest.Geometry.Vector(index * 2, 0)
                : new OpenNest.Geometry.Vector(2 + index * 2, 2);
            plate.Parts.Add(part);
        }
        nest.Plates.Add(plate);
        new NestWriter(nest).Write(path);
        return path;
    }

    private static List<NestingEngineInfo> FakeEngines(
        ConcurrencyProbe probe,
        params int[] delaysMs
    ) =>
        delaysMs
            .Select(
                (delay, i) =>
                    new NestingEngineInfo(
                        $"Engine{i}",
                        "test double",
                        () => new SleepingEngine(probe, delay)
                    )
            )
            .ToList();

    [Fact]
    public void NestJobUsesSalvageRateStoredInSourceNest()
    {
        var job = Assert.Single(JobLoader.Load(WriteBaselineNest("salvage", salvageRate: 0.5)));

        var nestJob = job.BuildNestJob(maxPlates: 1);

        Assert.Equal(0.5, nestJob.Options.SalvageRate);
        Assert.Equal(0, nestJob.Options.MinimumSalvageDimension);
    }

    [Fact]
    public void Run_ScoresTheOriginalBaselineLayoutAlongsideEngines()
    {
        var results = BenchmarkRunner.Run(
            JobLoader.Load(WriteBaselineNest("baseline")),
            FakeEngines(new ConcurrencyProbe(), 0),
            maxParallelism: 1
        );

        var baseline = Assert.Single(results.Where(result => result.EngineName == "Baseline"));
        Assert.True(baseline.Valid);
        Assert.Equal(1, baseline.PartsPlaced);
        Assert.Equal(1, baseline.PartsRequested);
        Assert.Equal(4, baseline.PlacedArea);
        Assert.Equal(100, baseline.PlateArea);
        Assert.Equal(1, baseline.PlatesUsed);
        Assert.Equal(0.04, baseline.Utilization, 6);
        Assert.Equal(100, baseline.NetSheetArea);
        Assert.Equal(100, baseline.Cost);
    }

    [Fact]
    public void Run_BaselineUsesSourceSalvageRateUnlessCliOverridesIt()
    {
        var jobs = JobLoader.Load(WriteBaselineNest("baseline-salvage", salvageRate: 0.5));
        var sourceRate = BenchmarkRunner.Run(
            jobs,
            FakeEngines(new ConcurrencyProbe(), 0),
            minimumSalvageDimension: 5,
            maxParallelism: 1
        );
        var overriddenRate = BenchmarkRunner.Run(
            jobs,
            FakeEngines(new ConcurrencyProbe(), 0),
            salvageRate: 0,
            minimumSalvageDimension: 5,
            maxParallelism: 1
        );

        Assert.Equal(
            60,
            Assert.Single(sourceRate.Where(result => result.EngineName == "Baseline")).NetSheetArea
        );
        Assert.Equal(
            100,
            Assert
                .Single(overriddenRate.Where(result => result.EngineName == "Baseline"))
                .NetSheetArea
        );
    }

    [Fact]
    public void Run_RejectsBaselineLayoutOutsideRotationConstraint()
    {
        var results = BenchmarkRunner.Run(
            JobLoader.Load(
                WriteBaselineNest("invalid-baseline-rotation", partRotation: System.Math.PI)
            ),
            FakeEngines(new ConcurrencyProbe(), 0),
            maxParallelism: 1
        );

        var baseline = Assert.Single(results.Where(result => result.EngineName == "Baseline"));
        Assert.False(baseline.Valid);
        Assert.Contains(
            baseline.Violations,
            violation => violation.Contains("outside its rotation constraint")
        );
    }

    [Fact]
    public void Run_RejectsAnInvalidBaselineLayout()
    {
        var results = BenchmarkRunner.Run(
            JobLoader.Load(WriteBaselineNest("invalid-baseline", outsideWorkArea: true)),
            FakeEngines(new ConcurrencyProbe(), 0),
            maxParallelism: 1
        );

        var baseline = Assert.Single(results.Where(result => result.EngineName == "Baseline"));
        Assert.False(baseline.Valid);
        Assert.Equal(0, baseline.Utilization);
        Assert.Contains(
            baseline.Violations,
            violation => violation.Contains("outside the work area")
        );
    }

    [Fact]
    public void Run_DoesNotScoreZeroQuantityBaselinePlate()
    {
        var results = BenchmarkRunner.Run(
            JobLoader.Load(WriteBaselineNest("zero-quantity", plateQuantity: 0)),
            FakeEngines(new ConcurrencyProbe(), 0),
            maxParallelism: 1
        );

        Assert.DoesNotContain(results, result => result.EngineName == "Baseline");
    }

    [Fact]
    public void Run_AppliesSpacingOverrideToBaseline()
    {
        var results = BenchmarkRunner.Run(
            JobLoader.Load(
                WriteBaselineNest("spacing-override", requiredQuantity: 2, partCount: 2),
                partSpacingOverride: 0.25
            ),
            FakeEngines(new ConcurrencyProbe(), 0),
            maxParallelism: 1
        );

        var baseline = Assert.Single(results.Where(result => result.EngineName == "Baseline"));
        Assert.False(baseline.Valid);
        Assert.Contains(
            baseline.Violations,
            violation => violation.Contains("closer than the required spacing")
        );
    }

    [Fact]
    public void Run_NeverExceedsMaxParallelism_ButReachesIt()
    {
        var probe = new ConcurrencyProbe();

        var results = BenchmarkRunner.Run(
            LoadJob(),
            FakeEngines(probe, 200, 200, 200, 200, 200, 200),
            maxParallelism: 2
        );

        Assert.Equal(6, results.Count);
        Assert.Equal(2, probe.Max);
    }

    [Fact]
    public void Run_WithMaxParallelismOne_RunsOneSolveAtATime()
    {
        var probe = new ConcurrencyProbe();

        BenchmarkRunner.Run(LoadJob(), FakeEngines(probe, 50, 50, 50, 50), maxParallelism: 1);

        Assert.Equal(1, probe.Max);
    }

    [Fact]
    public void Run_KeepsResultsInJobThenEngineOrder_RegardlessOfCompletionOrder()
    {
        var probe = new ConcurrencyProbe();

        // The first engine finishes last, so completion order differs from input order.
        var results = BenchmarkRunner.Run(
            LoadJob(),
            FakeEngines(probe, 300, 10, 10, 10),
            maxParallelism: 3
        );

        Assert.Equal(
            new[] { "Engine0", "Engine1", "Engine2", "Engine3" },
            results.Select(r => r.EngineName)
        );
    }

    [Fact]
    public void Run_WithOutputDirectory_WritesManifestJobsWithoutNeedingASourceNest()
    {
        var probe = new ConcurrencyProbe();
        var output = Path.Combine(_dir, "out");

        var results = BenchmarkRunner.Run(
            LoadJob(),
            FakeEngines(probe, 0),
            outputDirectory: output
        );

        var result = Assert.Single(results);
        Assert.Null(result.Error);
        Assert.True(result.Valid);
        Assert.True(File.Exists(Path.Combine(output, "job-Engine0.nest")));
        Assert.True(File.Exists(Path.Combine(output, "job-Engine0.json")));
    }

    private sealed class ConcurrencyProbe
    {
        private int _current;
        private int _max;

        public int Max => Volatile.Read(ref _max);

        public void Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while ((seen = Volatile.Read(ref _max)) < now)
            {
                if (Interlocked.CompareExchange(ref _max, now, seen) == seen)
                    break;
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    /// <summary>Places nothing after sleeping, recording how many solves overlap.</summary>
    private sealed class SleepingEngine(ConcurrencyProbe probe, int delayMs) : INestingEngine
    {
        public NestJobResult Solve(
            NestJob job,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        )
        {
            probe.Enter();
            try
            {
                Thread.Sleep(delayMs);
                return new NestJobResult(
                    NestJobStatus.Incomplete,
                    NestJobStopReason.NoPlacementFound,
                    Array.Empty<NestJobPlateResult>(),
                    job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, 0, p.Quantity)),
                    job.Plates.Select(s => new StockUsage(s.Id, 0, null))
                );
            }
            finally
            {
                probe.Exit();
            }
        }
    }
}
