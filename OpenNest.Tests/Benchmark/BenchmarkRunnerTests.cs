using OpenNest.Benchmark;

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
