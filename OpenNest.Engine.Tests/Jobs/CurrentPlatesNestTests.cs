using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class CurrentPlatesNestTests
{
    /// <summary>Places one part per offered sheet it fits, at the work area's corner, until demand runs out.</summary>
    private sealed class OnePerSheetEngine : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var part = job.Parts[0];
            var plates = new List<NestJobPlateResult>();
            var placed = 0;
            foreach (var stock in job.Plates)
            {
                // The test part is 10 x 20, unrotated.
                if (!stock.Fits(10, 20))
                    continue;
                var sheets = stock.Quantity ?? job.Options.MaxPlates ?? 100;
                for (var i = 0; i < sheets && placed < part.Quantity
                    && plates.Count < (job.Options.MaxPlates ?? 100); i++, placed++)
                {
                    var area = stock.WorkArea;
                    plates.Add(new NestJobPlateResult(plates.Count, stock,
                        new[] { new NestJobPlacement(part.Id, placed, area.X, area.Y, 0) }));
                }
            }
            var done = placed == part.Quantity;
            return new NestJobResult(
                done ? NestJobStatus.Complete : NestJobStatus.Incomplete,
                done ? NestJobStopReason.Completed : NestJobStopReason.StockExhausted,
                plates,
                new[] { new PartFulfillment(part.Id, part.Quantity, placed, part.Quantity - placed) },
                job.Plates.Select(s => new StockUsage(s.Id, plates.Count(p => p.Stock.Id == s.Id), s.Quantity)).ToList());
        }
    }

    private static Task<NestPipelineResult> Solve(NestPipelineRequest request) =>
        Task.FromResult(NestPipeline.Run(new OnePerSheetEngine(), "stub", request));

    private static Task<LeftoverFillResult> Fill(Func<IProgress<NestProgress>, LeftoverFillResult> plan) =>
        Task.FromResult(plan(null!));

    /// <summary>A nest without a plate manager yet: a live manager collapses a second empty
    /// plate at the tail, so tests add their plates first.</summary>
    private static (Nest Nest, Drawing Drawing) NewNest()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.PlateDefaults.PartSpacing = 0.25;
        return (nest, drawing);
    }

    private static Plate EmptyPlate(Nest nest, double width, double length)
    {
        var plate = nest.CreatePlate();
        plate.Size = new Size(width, length);
        plate.PartSpacing = 0.25;
        return plate;
    }

    [Fact]
    public void LayoutCheck_HoldsOccupantsFixedButReportsNewPartsAgainstThem()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var plate = new Plate(new Size(48, 96)) { PartSpacing = 0.25 };
        // Two occupants that already overlap each other: not this proposal's problem.
        var occupantA = new Part(drawing, new Vector(0, 0));
        var occupantB = new Part(drawing, new Vector(5, 0));
        var clear = new Part(drawing, new Vector(40, 0));
        var tooClose = new Part(drawing, new Vector(15.1, 0));
        var requirements = new Dictionary<Drawing, (string, int)>(ReferenceEqualityComparer.Instance)
        {
            [drawing] = ("part", 2),
        };
        var fixedParts = new HashSet<Part>(new[] { occupantA, occupantB }, ReferenceEqualityComparer.Instance);

        Assert.Empty(NestLayoutCheck.Validate(
            new List<(Plate, List<Part>)> { (plate, new List<Part> { occupantA, occupantB, clear }) },
            requirements, fixedParts));
        Assert.NotEmpty(NestLayoutCheck.Validate(
            new List<(Plate, List<Part>)> { (plate, new List<Part> { occupantA, occupantB, tooClose }) },
            requirements, fixedParts));
        // Without fixed parts every pair is checked, as before.
        Assert.NotEmpty(NestLayoutCheck.Validate(
            new List<(Plate, List<Part>)> { (plate, new List<Part> { occupantA, occupantB, clear }) },
            requirements));
    }

    public static TheoryData<string> Strategies()
    {
        var data = new TheoryData<string>();
        foreach (var strategy in PlateFillService.BuiltInStrategies)
            data.Add(strategy);
        return data;
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void LeftoverFill_PlacesAroundExistingPartsWithoutChangingThePlate(string strategy)
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var plate = new Plate(new Size(48, 96)) { PartSpacing = 0.25 };
        var occupant = new Part(drawing, new Vector(0, 0));
        plate.Parts.Add(occupant);

        var result = PlateLeftoverFill.Run(new[] { plate },
            new[] { new NestItem { Drawing = drawing, Quantity = 5 } }, strategy, null, CancellationToken.None);

        var fill = Assert.Single(result.Plates);
        Assert.Equal(5, fill.Parts.Count);
        Assert.Empty(result.Violations);
        Assert.Same(occupant, Assert.Single(plate.Parts));
        var requirements = new Dictionary<Drawing, (string, int)>(ReferenceEqualityComparer.Instance)
        {
            [drawing] = ("part", 5),
        };
        Assert.Empty(NestLayoutCheck.Validate(
            new List<(Plate, List<Part>)> { (plate, fill.Parts.Append(occupant).ToList()) }, requirements,
            new HashSet<Part>(new[] { occupant }, ReferenceEqualityComparer.Instance)));
    }

    [Fact]
    public void LeftoverFill_ARepeatedPlateUsesEveryCopysParts()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var plate = new Plate(new Size(48, 96)) { PartSpacing = 0.25, Quantity = 3 };
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));

        var result = PlateLeftoverFill.Run(new[] { plate },
            new[] { new NestItem { Drawing = drawing, Quantity = 7 } }, PlateFillService.DefaultStrategy, null,
            CancellationToken.None);

        // Seven copies over three plates: two per plate, six in all.
        Assert.Equal(2, Assert.Single(result.Plates).Parts.Count);
        Assert.Equal(6, result.Placed()[drawing]);
    }

    [Fact]
    public void ApplyToStockPlates_UsesTheExactPlatesAndRefusesOneThatChanged()
    {
        var (nest, drawing) = NewNest();
        var first = EmptyPlate(nest, 30, 40);
        var second = EmptyPlate(nest, 48, 96);
        var map = new Dictionary<string, Plate> { ["a"] = first, ["b"] = second };
        var result = NestPipeline.Run(new OnePerSheetEngine(), "stub", new NestPipelineRequest("stub",
            new[] { new NestItem { Drawing = drawing, Quantity = 2 } },
            new[]
            {
                OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.FromPlate("a", first, 1),
                OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.FromPlate("b", second, 1),
            }));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));

        second.Parts.Add(new Part(drawing));
        Assert.Throws<InvalidOperationException>(() => NestPipelineCommit.ApplyToStockPlates(result, map));
        Assert.Empty(first.Parts);

        second.Parts.Clear();
        var applied = NestPipelineCommit.ApplyToStockPlates(result, map);
        Assert.Equal(new[] { first, second }, applied);
        Assert.Equal(new Size(30, 40), first.Size);
        Assert.Single(first.Parts);
        Assert.Single(second.Parts);
    }

    [Fact]
    public async Task CurrentPlates_FillsLeftoverAndEmptyPlatesAndNeverCreatesAPlate()
    {
        var (nest, drawing) = NewNest();
        var occupied = EmptyPlate(nest, 48, 96);
        occupied.Parts.Add(new Part(drawing, new Vector(0, 0)));
        var configured = EmptyPlate(nest, 30, 40);
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        var plates = nest.Plates.Count;

        var proposal = await CurrentPlatesNest.RunAsync(nest, manager, NestPlateSource.Current,
            new[] { new NestItem { Drawing = drawing, Quantity = 100 } }, "stub",
            PlateFillService.DefaultStrategy, null, null, Fill, Solve);

        Assert.True(proposal.IsValid, string.Join("; ", proposal.Violations));
        Assert.NotEmpty(Assert.Single(proposal.Leftover.Plates).Parts);
        Assert.Same(configured, Assert.Single(proposal.PlateByStockId.Values));
        Assert.Null(proposal.New);
        Assert.NotEmpty(proposal.Unplaced);
        Assert.Equal(occupied.Parts.Count, proposal.Sheets[0].Occupants.Count);
        Assert.Equal(1, proposal.Sheets[0].ExistingPlateNumber);

        var applied = CurrentPlatesNest.Commit(proposal, nest, manager, false, CancellationToken.None, out var newPlates);

        Assert.Empty(newPlates);
        Assert.Equal(new[] { occupied, configured }, applied);
        Assert.Single(configured.Parts);
        Assert.True(occupied.Parts.Count > 1);
        // The only plate added is the empty trailing sheet the nest keeps ready after the
        // configured plate (the old tail) received parts; nothing was nested on it.
        Assert.Equal(plates + 1, nest.Plates.Count);
        Assert.Empty(nest.Plates[^1].Parts);
    }

    [Fact]
    public async Task Both_NestsTheRemainderOnNewSheetsAfterCurrentPlates()
    {
        var (nest, drawing) = NewNest();
        var configured = EmptyPlate(nest, 30, 40);
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        IReadOnlyList<NestItem>? askedNew = null;

        var proposal = await CurrentPlatesNest.RunAsync(nest, manager, NestPlateSource.Both,
            new[] { new NestItem { Drawing = drawing, Quantity = 3 } }, "stub",
            PlateFillService.DefaultStrategy, null,
            remaining =>
            {
                askedNew = remaining;
                return new NestPipelineRequest("stub", remaining.ToList(),
                    new[] { new NestPlateStock("new", new Size(48, 96), null, 0.25, new Spacing(), 1) });
            },
            Fill, Solve);

        Assert.True(proposal.IsValid, string.Join("; ", proposal.Violations));
        Assert.Equal(2, Assert.Single(askedNew!).Quantity);
        Assert.Empty(proposal.Unplaced);
        // The configured plate, then the two identical new sheets as one layout cut twice.
        Assert.Equal(2, proposal.Sheets.Count);
        Assert.Equal(2, proposal.Sheets[1].Quantity);

        CurrentPlatesNest.Commit(proposal, nest, manager, false, CancellationToken.None, out var newPlates);

        Assert.Single(configured.Parts);
        var fresh = Assert.Single(newPlates);
        Assert.NotSame(configured, fresh);
        Assert.Equal(2, fresh.Quantity);
        Assert.Equal(3, drawing.Quantity.Nested);
    }

    [Fact]
    public async Task Both_NeverLaysNewSheetsOnAnEmptyPlateTheUserSetUp()
    {
        var (nest, drawing) = NewNest();
        nest.Plates.Add(new Plate(new Size(48, 96)) { PartSpacing = 0.25 });
        nest.Plates[0].Parts.Add(new Part(drawing, new Vector(0, 0)));
        // Too small for the part, so the current-plate stage leaves it empty.
        var remnant = EmptyPlate(nest, 5, 5);
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();

        var proposal = await CurrentPlatesNest.RunAsync(nest, manager, NestPlateSource.Both,
            new[] { new NestItem { Drawing = drawing, Quantity = 40 } }, "stub",
            PlateFillService.DefaultStrategy, null,
            remaining => new NestPipelineRequest("stub", remaining.ToList(),
                new[] { new NestPlateStock("new", new Size(48, 96), null, 0.25, new Spacing(), 1) }),
            Fill, Solve);
        Assert.True(proposal.IsValid, string.Join("; ", proposal.Violations));
        Assert.NotNull(proposal.New);

        CurrentPlatesNest.Commit(proposal, nest, manager, false, CancellationToken.None, out var newPlates);

        Assert.NotEmpty(newPlates);
        Assert.DoesNotContain(remnant, newPlates);
        Assert.Empty(remnant.Parts);
        Assert.Equal(new Size(5, 5), remnant.Size);
    }

    [Fact]
    public void EmptyPlates_SkipsTheUntouchedTrailingSheet()
    {
        var (nest, _) = NewNest();
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        Assert.Empty(CurrentPlatesNest.EmptyPlates(nest, manager));

        nest.Plates[^1].Size = new Size(60, 120);
        Assert.Same(nest.Plates[^1], Assert.Single(CurrentPlatesNest.EmptyPlates(nest, manager)));
    }
}
