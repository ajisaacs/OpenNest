using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// <see cref="NestJobOptions.SearchUntilCancelled"/>: Irregular keeps decoding seeded variants
/// after its normal search, reporting each better plan, until the token is cancelled.
/// </summary>
public class IrregularSearchUntilCancelledTests
{
    private static NestJob MixedJob(bool searchUntilCancelled) =>
        Job(
            new[] { Part("disc", Disc(3), 10), Part("ell", LShape(12, 8, 4), 10), Part("tri", Triangle(9, 7), 8) },
            new[] { Stock("sheet", 40, 60, spacing: 0.5) },
            new NestJobOptions(searchUntilCancelled: searchUntilCancelled)
        );

    private sealed class Recorder(Action<NestJobProgress>? also = null) : IProgress<NestJobProgress>
    {
        public List<NestJobResult> Plans { get; } = new();

        public void Report(NestJobProgress value)
        {
            if (value.Stage == NestJobStage.PlanImproved)
                Plans.Add(value.Plan!);
            also?.Invoke(value);
        }
    }

    private static List<(string, int, double, double, double)> Placements(NestJobResult result) =>
        result.Plates.SelectMany(p => p.Placements.Select(q => (q.PartId, q.InstanceIndex, q.X, q.Y, q.Rotation)))
            .ToList();

    [Fact]
    public void WithoutACancellableTokenTheSearchEndsAsUsual()
    {
        var plain = new IrregularNestingEngine().Solve(MixedJob(false));
        // A listener alone does not start the extended search: it ends only by cancellation.
        var flagged = new IrregularNestingEngine().Solve(MixedJob(true), new Recorder());

        Assert.Equal(Placements(plain), Placements(flagged));
    }

    [Fact]
    public void KeepsSearchingAfterTheNormalSearchUntilCancelled()
    {
        var plain = new Recorder();
        var plainResult = new IrregularNestingEngine().Solve(MixedJob(false), plain);
        var normalSheets = 0;
        new IrregularNestingEngine { SheetPacked = () => normalSheets++ }.Solve(MixedJob(false));

        var extendedSheets = 0;
        var extendedTailBudgets = new List<long>();
        using var cancel = new CancellationTokenSource();
        var engine = new IrregularNestingEngine
        {
            // The normal search packs exactly normalSheets sheets; later ones are extended.
            TailReplanning = budget =>
            {
                if (extendedSheets > normalSheets)
                    extendedTailBudgets.Add(budget);
            },
            SheetPacked = () =>
            {
                // Let the extended search pack several more whole plans, then stop it.
                if (++extendedSheets >= normalSheets * 12)
                    cancel.Cancel();
            },
        };
        var extended = new Recorder();

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Solve(MixedJob(true), extended, cancel.Token));

        Assert.True(extendedSheets >= normalSheets * 12);
        // The normal search runs first, unchanged; the extended search improves on it and
        // re-plans the tails of its plans.
        Assert.True(extended.Plans.Count > plain.Plans.Count);
        // Each improvement gets a tail re-plan with its own fresh budget.
        Assert.NotEmpty(extendedTailBudgets);
        Assert.All(extendedTailBudgets, b => Assert.Equal(2 * new IrregularNestingEngine().WorkBudget, b));
        for (var i = 0; i < plain.Plans.Count; i++)
            Assert.Equal(Placements(plain.Plans[i]), Placements(extended.Plans[i]));
        var job = MixedJob(false);
        var last = CandidateScore.Of(job, extended.Plans[^1], 0);
        Assert.False(CandidateScore.Of(job, plainResult, 0).IsBetterThan(last));
        foreach (var plan in extended.Plans)
            LayoutAssert.Valid(job, plan);
        // Each reported plan beats the one before it: no seed reports a worse plan.
        for (var i = 1; i < extended.Plans.Count; i++)
            Assert.True(
                CandidateScore.Of(job, extended.Plans[i], 0)
                    .IsBetterThan(CandidateScore.Of(job, extended.Plans[i - 1], 0)),
                $"plan {i} does not improve on plan {i - 1}");
    }

    [Fact]
    public void SeedWeightsChangeTheLayout()
    {
        // The weights must reach the placement rule: some seed decodes differently.
        var job = MixedJob(false);
        var engine = new IrregularNestingEngine();
        var unweighted = Placements(engine.PlanForTesting(job, 0, null));
        Assert.Equal(unweighted, Placements(engine.PlanForTesting(job, 0, new[] { 1.0, 1.0, 1.0 })));
        Assert.Contains(
            Enumerable.Range(1, 8),
            seed => !Placements(engine.PlanForTesting(job, 0, IrregularNestingEngine.SeedWeights(seed, 3)))
                .SequenceEqual(unweighted));
    }

    [Fact]
    public void AJobWithNothingToPlaceDoesNotWaitForTheLimit()
    {
        // Oversize parts only: every decode places nothing, so more searching cannot help.
        var job = Job(
            new[] { Part("big", Rectangle(80, 80), 2), Part("bigger", Rectangle(90, 90), 2) },
            new[] { Stock("sheet", 40, 60) }
        );
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = new DeadlineNestingEngine(new IrregularNestingEngine(), TimeSpan.FromSeconds(30)).Solve(job);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.Empty(result.Plates.SelectMany(p => p.Placements));
    }

    [Fact]
    public void SeedWeightsAreFixedPerSeedAndBounded()
    {
        var first = IrregularNestingEngine.SeedWeights(7, 5);
        Assert.Equal(first, IrregularNestingEngine.SeedWeights(7, 5));
        Assert.NotEqual(first, IrregularNestingEngine.SeedWeights(8, 5));
        Assert.All(first, w => Assert.InRange(
            w,
            System.Math.Exp(-IrregularNestingEngine.WeightSpread),
            System.Math.Exp(IrregularNestingEngine.WeightSpread)));
    }

    [Fact]
    public void ATimeLimitAsksTheEngineToSearchUntilCancelled()
    {
        NestJob? seen = null;
        var options = new NestJobOptions("Fill", maxPlates: 3, salvageRate: 0.25, minimumSalvageDimension: 1.5);
        var plain = MixedJob(false);
        var job = new NestJob(plain.Parts, plain.Plates, options);
        var engine = new DeadlineNestingEngine(
            new StubEngine((j, _, _) =>
            {
                seen = j;
                return new IrregularNestingEngine().Solve(job);
            }),
            TimeSpan.FromMinutes(1)
        );

        engine.Solve(job);

        Assert.NotNull(seen);
        Assert.True(seen!.Options.SearchUntilCancelled);
        // Everything else about the job is passed on unchanged, and the caller's job is untouched.
        Assert.Equal(options.PlacementStrategy, seen.Options.PlacementStrategy);
        Assert.Equal(3, seen.Options.MaxPlates);
        Assert.Equal(0.25, seen.Options.SalvageRate);
        Assert.Equal(1.5, seen.Options.MinimumSalvageDimension);
        Assert.Equal(job.Parts, seen.Parts);
        Assert.Equal(job.Plates, seen.Plates);
        Assert.False(job.Options.SearchUntilCancelled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWeightAboveOneLowersThePlacementValue(bool fills)
    {
        // Lower value wins, in gap fills and in advance alike; weight one changes nothing.
        double Value(double weight) => FrontierPacker.PlacementValue(fills, 2.5, 7.0, 1.0, weight);
        Assert.Equal(fills ? -7.0 : 2.5 / 7.0, Value(1.0));
        Assert.True(Value(1.5) < Value(1.0));
        Assert.True(Value(1.0) < Value(1 / 1.5));
    }

    [Fact]
    public void AHeavyWeightPlacesItsTypeFirst()
    {
        // A weight above one favors its type; swapping the weights swaps the first type placed
        // and moves the favored type's parts earlier in placement order.
        var job = MixedJob(false);
        var engine = new IrregularNestingEngine();
        double MeanOrder(NestJobResult r, string id)
        {
            var order = r.Plates.SelectMany(p => p.Placements).Select((q, i) => (q.PartId, i))
                .Where(x => x.PartId == id).Select(x => (double)x.i).ToList();
            return order.Average();
        }
        foreach (var (favored, weights) in new[]
                 {
                     ("disc", new[] { 50.0, 1, 1 }), ("ell", new[] { 1, 50.0, 1 }), ("tri", new[] { 1, 1, 50.0 }),
                 })
        {
            var result = engine.PlanForTesting(job, 0, weights);
            var light = engine.PlanForTesting(job, 0, weights.Select(w => 1 / w).ToArray());
            Assert.Equal(favored, result.Plates[0].Placements[0].PartId);
            Assert.True(MeanOrder(result, favored) < MeanOrder(light, favored), favored);
        }
    }

    [Fact]
    public void DefaultUnderATimeLimitReturnsAValidPlanNoWorseThanUnlimited()
    {
        // Irregular runs last under Default and searches until the limit; its plans compete
        // with the Rectangles and Circles results already reported. The limit leaves a wide
        // margin over the unlimited solve (well under a second here) so a slow machine passes.
        var job = MixedJob(false);
        var normal = new DefaultNestingEngine().Solve(job);

        var limited = new DeadlineNestingEngine(new DefaultNestingEngine(), TimeSpan.FromSeconds(5))
            .Solve(job);

        LayoutAssert.Valid(job, limited);
        Assert.False(CandidateScore.Of(job, normal, 0).IsBetterThan(CandidateScore.Of(job, limited, 0)));
    }

    private sealed class StubEngine(
        Func<NestJob, IProgress<NestJobProgress>?, CancellationToken, NestJobResult> solve
    ) : INestingEngine
    {
        public NestJobResult Solve(
            NestJob job,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) => solve(job, progress, token);
    }
}
