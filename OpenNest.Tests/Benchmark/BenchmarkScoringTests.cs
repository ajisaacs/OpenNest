using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Benchmark;

public class BenchmarkScoringTests
{
    [Theory]
    [InlineData(0, 43, 43, 4080)]
    [InlineData(0.5, 43, 43, 4080)]
    [InlineData(0, 43, 0, 4080)]
    [InlineData(0.5, 43, 0, 4080)]
    [InlineData(0, 86, 21, 4104)]
    [InlineData(0.5, 86, 21, 4104)]
    [InlineData(0, 0, 21, 4104)]
    [InlineData(0.5, 0, 21, 4104)]
    public void SharedCostEqualsBenchmarkRunForEveryEdgeAndUnplacedParts(
        double rate, double x, double y, double salvage)
    {
        var benchmarkJob = Job(0.5, (Rect(10, 5), 3));
        NestJob? capturedJob = null;
        NestJobResult? capturedResult = null;
        var engine = Engine("Shared cost parity", job =>
        {
            capturedJob = job;
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(job.Plates[0], new[] { (job.Parts[0].Id, x, y, 0.0) });
            capturedResult = builder.Build(NestJobStopReason.NoPlacementFound);
            return capturedResult.Plates;
        });

        var scored = Assert.Single(BenchmarkRunner.Run(new List<BenchmarkJob> { benchmarkJob }, new[] { engine },
            salvageRate: rate, minimumSalvageDimension: 10));

        Assert.True(scored.Valid, string.Join("; ", scored.Violations));
        Assert.NotNull(capturedJob);
        Assert.NotNull(capturedResult);
        Assert.Equal(2, scored.PartsUnplaced);
        Assert.Equal(4608 - rate * salvage, scored.NetSheetArea);
        Assert.Equal(scored.NetSheetArea,
            capturedResult.Plates.Sum(sheet => NestJobCost.NetSheetArea(capturedJob, sheet)));
        Assert.Equal(benchmarkJob.UnplacedPartPenalty, NestJobCost.UnplacedPartPenalty(capturedJob));
        Assert.Equal(scored.NetSheetArea + 2 * 4608, scored.Cost);
        Assert.Equal(scored.Cost, NestJobCost.Evaluate(capturedJob, capturedResult));
    }

    // ── ranking ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_CompleteRunBeatsIncompleteRunWithHigherUtilization()
    {
        var cherryPicked = Result(placed: 150, requested: 219, placedArea: 90, plateArea: 100);
        var complete = Result(placed: 219, requested: 219, placedArea: 120, plateArea: 143);

        Assert.True(cherryPicked.Utilization > complete.Utilization);
        Assert.True(Report.Compare(complete, cherryPicked) < 0);
    }

    [Fact]
    public void Compare_AmongIncompleteRuns_PlacingMorePartsWinsEvenOnMoreSheet()
    {
        var more = Result(placed: 9, requested: 10, placedArea: 90, plateArea: 200);
        var fewer = Result(placed: 5, requested: 10, placedArea: 50, plateArea: 100);

        Assert.True(Report.Compare(more, fewer) < 0);
    }

    [Fact]
    public void Compare_CompleteRuns_SalvageCreditBreaksEqualSheetUsage()
    {
        var cleanRemnant = Result(10, 10, placedArea: 60, plateArea: 100, netSheetArea: 80);
        var scattered = Result(10, 10, placedArea: 60, plateArea: 100, netSheetArea: 100);

        Assert.True(Report.Compare(cleanRemnant, scattered) < 0);
    }

    [Fact]
    public void Cost_InvalidRunPaysPenaltyOnEveryRequestedPart()
    {
        var invalid = new JobResult
        {
            Valid = false,
            PartsPlaced = 10,
            PartsRequested = 10,
            UnplacedPartPenalty = 100,
            Violations = { "overlap" },
        };

        Assert.Equal(1000, invalid.Cost);
    }

    // ── validation through the runner ────────────────────────────────────────

    [Fact]
    public void Run_RotationOutsideConstraint_IsInvalid()
    {
        var part = Rect(10, 5);
        LockRotation(part);
        var job = Job(0.5, (part, 1));

        var rotated = RunSingle(job, j => Sheet(j, (j.Parts[0].Id, 20, 20, Angle.HalfPI)));
        var upright = RunSingle(job, j => Sheet(j, (j.Parts[0].Id, 20, 20, 0)));

        Assert.False(rotated.Valid);
        Assert.Contains(rotated.Violations, v => v.Contains("rotation constraint"));
        Assert.True(upright.Valid, string.Join("; ", upright.Violations));
    }

    [Fact]
    public void Run_StockWithLoosenedSpacing_IsInvalid()
    {
        var job = Job(0.5, (Rect(10, 5), 1));

        var result = RunSingle(
            job,
            j =>
            {
                var real = j.Plates[0];
                var forged = new NestPlateStock(real.Id, real.Size, null, 0, real.EdgeSpacing, real.Quadrant);
                return new[]
                {
                    new NestJobPlateResult(0, forged, new[] { new NestJobPlacement(j.Parts[0].Id, 0, 1, 1, 0) }),
                };
            }
        );

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Contains("does not match any stock"));
    }

    [Fact]
    public void Run_PartInsideAnotherPartsCutout_IsValidWhenItClearsTheEdge()
    {
        var job = Job(0.5, (Frame(), 1), (Rect(4, 4), 1));

        var clear = RunSingle(
            job,
            j => Sheet(j, (j.Parts[0].Id, 10, 10, 0), (j.Parts[1].Id, 18, 18, 0))
        );
        var tooClose = RunSingle(
            job,
            j => Sheet(j, (j.Parts[0].Id, 10, 10, 0), (j.Parts[1].Id, 15.2, 18, 0))
        );

        Assert.True(clear.Valid, string.Join("; ", clear.Violations));
        Assert.True(clear.FullyPlaced);
        Assert.False(tooClose.Valid);
        Assert.Contains(tooClose.Violations, v => v.Contains("closer than the required spacing"));
    }

    [Fact]
    public void Run_OverlappingNeighbours_AreStillCaughtBySweep()
    {
        var job = Job(0.5, (Rect(10, 5), 3));

        var result = RunSingle(
            job,
            j => Sheet(
                j,
                (j.Parts[0].Id, 60, 1, 0),
                (j.Parts[0].Id, 1, 1, 0),
                (j.Parts[0].Id, 10.2, 1, 0)
            )
        );

        Assert.False(result.Valid);
        Assert.Single(result.Violations);
    }

    [Fact]
    public void Run_WithSalvageCredit_CompactLayoutOutscoresScatteredLayout()
    {
        var job = Job(0.5, (Rect(10, 5), 2));
        var engines = new List<NestingEngineInfo>
        {
            Engine("Compact", j => Sheet(j, (j.Parts[0].Id, 0, 0, 0), (j.Parts[0].Id, 0, 10, 0))),
            Engine("Scattered", j => Sheet(j, (j.Parts[0].Id, 0, 0, 0), (j.Parts[0].Id, 86, 43, 0))),
        };

        var results = BenchmarkRunner.Run(
            new List<BenchmarkJob> { job },
            engines,
            salvageRate: 0.5,
            minimumSalvageDimension: 10
        );
        var compact = results.Single(r => r.EngineName == "Compact");
        var scattered = results.Single(r => r.EngineName == "Scattered");

        Assert.True(compact.FullyPlaced && scattered.FullyPlaced);
        Assert.Equal(compact.PlateArea, scattered.PlateArea);
        Assert.True(compact.NetSheetArea < scattered.NetSheetArea);
        Assert.True(Report.Compare(compact, scattered) < 0);
    }

    [Fact]
    public void TryParseSheetSize_UsesInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            Assert.True(JobLoader.TryParseSheetSize("48.5x96", out var size));
            Assert.Equal(48.5, size.Width);
            Assert.Equal(96, size.Length);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JobResult Result(
        int placed,
        int requested,
        double placedArea,
        double plateArea,
        double? netSheetArea = null
    ) =>
        new()
        {
            Valid = true,
            PartsPlaced = placed,
            PartsRequested = requested,
            PlacedArea = placedArea,
            PlateArea = plateArea,
            NetSheetArea = netSheetArea ?? plateArea,
            UnplacedPartPenalty = 100,
            PlatesUsed = 1,
        };

    private static Drawing Rect(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    /// <summary>20x20 square with a 10x10 cutout centred in it.</summary>
    private static Drawing Frame()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(5, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 15)));
        pgm.Codes.Add(new LinearMove(new Vector(15, 15)));
        pgm.Codes.Add(new LinearMove(new Vector(15, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 5)));
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("frame", pgm);
    }

    private static void LockRotation(Drawing drawing)
    {
        drawing.Constraints ??= new NestConstraints();
        drawing.Constraints.StepAngle = Angle.TwoPI;
        drawing.Constraints.StartAngle = 0;
        drawing.Constraints.EndAngle = 0;
    }

    private static BenchmarkJob Job(double spacing, params (Drawing Drawing, int Quantity)[] parts) =>
        new()
        {
            SourceFile = "test.manifest.json",
            CandidateSizes = new List<Size> { new(48, 96) },
            EdgeSpacing = new Spacing(0, 0),
            PartSpacing = spacing,
            Quadrant = 1,
            Requests = parts
                .Select(p => new DrawingRequest { Drawing = p.Drawing, Quantity = p.Quantity })
                .ToList(),
        };

    private static NestJobPlateResult[] Sheet(
        NestJob job,
        params (string PartId, double X, double Y, double Rotation)[] placements
    )
    {
        var counts = new Dictionary<string, int>();
        return new[]
        {
            new NestJobPlateResult(
                0,
                job.Plates[0],
                placements.Select(p =>
                {
                    var index = counts.GetValueOrDefault(p.PartId);
                    counts[p.PartId] = index + 1;
                    return new NestJobPlacement(p.PartId, index, p.X, p.Y, p.Rotation);
                })
            ),
        };
    }

    private static JobResult RunSingle(
        BenchmarkJob job,
        Func<NestJob, IEnumerable<NestJobPlateResult>> layout
    ) => Assert.Single(BenchmarkRunner.Run(new List<BenchmarkJob> { job }, new[] { Engine("Scripted", layout) }));

    private static NestingEngineInfo Engine(
        string name,
        Func<NestJob, IEnumerable<NestJobPlateResult>> layout
    ) => new(name, "test double", () => new ScriptedEngine(layout));

    /// <summary>Returns a fixed layout without going through NestJobRunner, the way a
    /// hand-written engine could, so only the benchmark's own validator judges it.</summary>
    private sealed class ScriptedEngine(Func<NestJob, IEnumerable<NestJobPlateResult>> layout)
        : INestingEngine
    {
        public NestJobResult Solve(
            NestJob job,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) =>
            new(
                NestJobStatus.Complete,
                NestJobStopReason.Completed,
                layout(job),
                Array.Empty<PartFulfillment>(),
                Array.Empty<StockUsage>()
            );
    }
}
