using OpenNest.CutOffs;
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
    private static NestJob MixedJob(bool searchUntilCancelled, int ells = 10) =>
        Job(
            new[] { Part("disc", Disc(3), 10), Part("ell", LShape(12, 8, 4), ells), Part("tri", Triangle(9, 7), 8) },
            new[] { Stock("sheet", 40, 60, spacing: 0.5) },
            new NestJobOptions(searchUntilCancelled: searchUntilCancelled)
        );

    private sealed class Recorder(Action<NestJobProgress>? also = null) : IProgress<NestJobProgress>
    {
        public List<NestJobResult> Plans { get; } = new();

        /// <summary>Every plan reported, improved or alternative, in order.</summary>
        public List<NestJobResult> All { get; } = new();

        public void Report(NestJobProgress value)
        {
            if (value.Stage == NestJobStage.PlanImproved)
                Plans.Add(value.Plan!);
            if (value.Stage is NestJobStage.PlanImproved or NestJobStage.PlanAlternative)
                All.Add(value.Plan!);
            also?.Invoke(value);
        }
    }

    /// <summary>Whether <paramref name="expected"/> appears in <paramref name="reported"/> in order.</summary>
    private static bool InOrder(IEnumerable<NestJobResult> expected, IEnumerable<NestJobResult> reported)
    {
        using var next = reported.GetEnumerator();
        foreach (var plan in expected)
        {
            var found = false;
            while (!found && next.MoveNext())
                found = Placements(next.Current).SequenceEqual(Placements(plan));
            if (!found)
                return false;
        }
        return true;
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
    public void ASolveThatCanEndUnlimitedReportsNoQuickPlan()
    {
        // The quick plan belongs to a solve that ends only at its limit: without the option, or
        // without a token that can cancel it, the plans reported are the usual ones.
        var plain = new Recorder();
        var plainResult = new IrregularNestingEngine().Solve(MixedJob(false), plain);
        var quick = new IrregularNestingEngine().QuickPlanForTesting(MixedJob(false));
        Assert.DoesNotContain(plain.All, p => Placements(p).SequenceEqual(Placements(quick)));

        using var cancel = new CancellationTokenSource();
        var cancellable = new Recorder();
        var flagged = new Recorder();
        var cancellableResult = new IrregularNestingEngine().Solve(MixedJob(false), cancellable, cancel.Token);
        var flaggedResult = new IrregularNestingEngine().Solve(MixedJob(true), flagged);

        foreach (var (recorder, result) in new[] { (cancellable, cancellableResult), (flagged, flaggedResult) })
        {
            Assert.Equal(Placements(plainResult), Placements(result));
            Assert.Equal(plain.All.Select(Placements), recorder.All.Select(Placements));
        }
    }

    [Fact]
    public void ATimeLimitedSolveReportsAQuickPlanFirst()
    {
        // The first variant without Lattice blocks, reported before the normal search finishes its
        // first sheet: stopped there, the solve has the quick plan, a valid layout, to return.
        var job = MixedJob(true);
        var quickSheets = 0;
        var quick = new IrregularNestingEngine { SheetPacked = () => quickSheets++ }.QuickPlanForTesting(job);
        using var cancel = new CancellationTokenSource();
        var sheets = 0;
        var engine = new IrregularNestingEngine
        {
            SheetPacked = () =>
            {
                if (++sheets > quickSheets)
                    cancel.Cancel();
            },
        };
        var recorder = new Recorder();

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Solve(job, recorder, cancel.Token));

        var plan = Assert.Single(recorder.Plans);
        Assert.Equal(Placements(quick), Placements(plan));
        Assert.NotEmpty(Placements(plan));
        LayoutAssert.Valid(MixedJob(false), plan);
    }

    [Fact]
    public void KeepsSearchingAfterTheNormalSearchUntilCancelled()
    {
        const int Ells = 12;
        var plain = new Recorder();
        // Grouped, or with ten ells (whose blocks Fill nests as herringbone pairs), this job's
        // normal search already finds a plan the seeds do not beat within the sheets allowed
        // here; this test is of the seeded search, so it runs ungrouped with twelve.
        var plainResult = new IrregularNestingEngine { GroupLikeParts = false }.Solve(MixedJob(false, Ells), plain);
        var normalSheets = 0;
        new IrregularNestingEngine { GroupLikeParts = false, SheetPacked = () => normalSheets++ }.Solve(MixedJob(false, Ells));
        // A time-limited solve packs its quick plan before the normal search.
        var quickSheets = 0;
        var quick = new IrregularNestingEngine { GroupLikeParts = false, SheetPacked = () => quickSheets++ }
            .QuickPlanForTesting(MixedJob(false, Ells));

        var extendedSheets = 0;
        var extendedTailBudgets = new List<long>();
        using var cancel = new CancellationTokenSource();
        var engine = new IrregularNestingEngine
        {
            GroupLikeParts = false,
            // The quick plan and the normal search pack exactly quickSheets + normalSheets sheets;
            // later ones are extended.
            TailReplanning = budget =>
            {
                if (extendedSheets > quickSheets + normalSheets)
                    extendedTailBudgets.Add(budget);
            },
            SheetPacked = () =>
            {
                // Let the extended search pack several more whole plans, then stop it.
                if (++extendedSheets >= quickSheets + normalSheets * 12)
                    cancel.Cancel();
            },
        };
        var extended = new Recorder();

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Solve(MixedJob(true, Ells), extended, cancel.Token));

        Assert.True(extendedSheets >= quickSheets + normalSheets * 12);
        // The quick plan comes first. The normal search then runs unchanged, reporting its plans as
        // before; the extended search improves on it and re-plans the tails of its plans.
        Assert.Equal(Placements(quick), Placements(extended.Plans[0]));
        Assert.True(InOrder(plain.Plans, extended.Plans.Skip(1)));
        Assert.Contains(extended.Plans.Skip(1),
            p => !plain.Plans.Any(q => Placements(q).SequenceEqual(Placements(p))));
        // Each improvement gets a tail re-plan with its own fresh budget.
        Assert.NotEmpty(extendedTailBudgets);
        Assert.All(extendedTailBudgets, b => Assert.Equal(2 * new IrregularNestingEngine().WorkBudget, b));
        var job = MixedJob(false, Ells);
        var last = CandidateScore.Of(job, extended.Plans[^1], 0);
        Assert.False(CandidateScore.Of(job, plainResult, 0).IsBetterThan(last));
        foreach (var plan in extended.Plans)
            LayoutAssert.Valid(job, plan);
        // After the quick plan, each reported plan beats the one before it: no seed reports a
        // worse plan. The deadline wrapper ranks the quick plan against them.
        for (var i = 2; i < extended.Plans.Count; i++)
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
        var options = new NestJobOptions("Fill", maxPlates: 3, salvageRate: 0.25,
            minimumSalvageDimension: 1.5, offcutCut: CutOffAxis.Vertical);
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
        Assert.Equal(CutOffAxis.Vertical, seen.Options.OffcutCut);
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
        // with the Rectangles and Circles results already reported. A limit shorter than the
        // normal search correctly returns an earlier, worse plan, so the limit is scaled from
        // the measured unlimited solve: seconds on a loaded CI runner, not a fixed constant.
        var job = MixedJob(false);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var normal = new DefaultNestingEngine().Solve(job);
        var limit = TimeSpan.FromTicks(
            System.Math.Max(TimeSpan.FromSeconds(5).Ticks, 3 * watch.Elapsed.Ticks));

        var limited = new DeadlineNestingEngine(new DefaultNestingEngine(), limit).Solve(job);

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
