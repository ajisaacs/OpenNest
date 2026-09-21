using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Names are never identity: two distinct drawings that share a display name must keep
/// independent quantities, and two requirements that share one source drawing must not
/// cross-count each other's placements through the legacy engine paths.
/// </summary>
public class NestJobIdentityTests
{
    [Fact]
    public void DistinctDrawingsWithSameNameKeepIndependentQuantitiesInRealEngine()
    {
        var a = new Drawing("identical", TestDrawingFactory.Rectangle(40, 40));
        var b = new Drawing("identical", TestDrawingFactory.Rectangle(40, 40));
        var job = new NestJob(
            new[]
            {
                DrawingJobMapper.FromDrawing("a", a, 2),
                DrawingJobMapper.FromDrawing("b", b, 2),
            },
            new[] { new NestPlateStock("s", new Size(90, 90), 1) }
        );

        var result = new NestJobRunner(LegacyPlateNesterAdapter.Create).Solve(job);

        // Every placed part maps to a known requirement ID; no part is invented or cross-counted.
        Assert.True(result.Plates.SelectMany(p => p.Placements).All(p => p.PartId is "a" or "b"));
        var counts = result
            .Plates.SelectMany(p => p.Placements)
            .GroupBy(p => p.PartId)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, placed) in counts)
            Assert.True(placed <= 2, $"Requirement {id} placed {placed} > requested 2");

        // Fulfillment conservation for both IDs.
        foreach (var f in result.Fulfillment)
            Assert.Equal(f.Requested, f.Placed + f.Unplaced);
    }

    [Fact]
    public void TwoRequirementsOnSameSourceDrawingKeepIndependentQuantities()
    {
        var source = new Drawing("shared", TestDrawingFactory.Rectangle(30, 30));
        var job = new NestJob(
            new[]
            {
                DrawingJobMapper.FromDrawing("first", source, 2),
                DrawingJobMapper.FromDrawing("second", source, 2),
            },
            new[] { new NestPlateStock("s", new Size(90, 90), 1) }
        );

        var result = new NestJobRunner(LegacyPlateNesterAdapter.Create).Solve(job);

        Assert.Equal(new[] { "first", "second" }, result.Fulfillment.Select(f => f.PartId));
        foreach (var f in result.Fulfillment)
            Assert.Equal(f.Requested, f.Placed + f.Unplaced);

        // Output drawings are distinct even though the input is the same Drawing instance.
        var output = NestResultMaterializer.Materialize(job, result);
        Assert.NotSame(output.DrawingsByPartId["first"], output.DrawingsByPartId["second"]);
        // Caller source is untouched.
        Assert.Equal(0, source.Quantity.Nested);
    }

    [Fact]
    public void EngineDeductionCountsByDrawingReferenceNotName()
    {
        // Plate 90x40 fits exactly two 40x40 parts. The engine fills item A with both and
        // starves item B. Name-based deduction would then zero BOTH items (the two placed
        // parts carry the shared name, so each item counts 2 as "its own"). Reference-based
        // deduction leaves B at 2.
        var a = new Drawing("dup", TestDrawingFactory.Rectangle(40, 40));
        var b = new Drawing("dup", TestDrawingFactory.Rectangle(40, 40));
        var plate = new Plate(new Size(90, 40));
        var items = new List<NestItem>
        {
            new() { Drawing = a, Quantity = 2 },
            new() { Drawing = b, Quantity = 2 },
        };
        // Place exactly 2 parts from item A and none from item B, then run the base-class
        // deduction. Deterministic regardless of any fill heuristic.
        var placed = new StarvingProbe(plate).Nest(items, null, default);

        var aPlaced = placed.Count(p => ReferenceEquals(p.BaseDrawing, a));
        var bPlaced = placed.Count(p => ReferenceEquals(p.BaseDrawing, b));
        Assert.Equal(2, placed.Count);
        Assert.Equal(2, aPlaced);
        Assert.Equal(0, bPlaced);

        // Invariant: remaining = requested - own placements. Under name-based counting,
        // both items would read 0 here because the two placed parts match the shared name.
        Assert.Equal(0, items[0].Quantity);
        Assert.Equal(2, items[1].Quantity);
    }

    [Fact]
    public void SameNameSinglesAreBothReturnedByPackPhase()
    {
        var a = new Drawing("samesingle", TestDrawingFactory.Rectangle(30, 30));
        var b = new Drawing("samesingle", TestDrawingFactory.Rectangle(30, 30));
        var plate = new Plate(new Size(100, 100));
        var items = new List<NestItem>
        {
            new() { Drawing = a, Quantity = 1 },
            new() { Drawing = b, Quantity = 1 },
        };
        var placed = new BaseNestEngineProbe(plate).Nest(items, null, default);
        Assert.Equal(2, placed.Count);
        Assert.Equal(new[] { 0, 0 }, new[] { items[0].Quantity, items[1].Quantity });
    }

    private sealed class BaseNestEngineProbe(Plate plate) : NestEngineBase(plate)
    {
        public override string Name => "probe";
        public override string Description => "probe";

        public override List<Part> Fill(
            NestItem item,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        ) => new DefaultNestEngine(Plate).Fill(item, workArea, progress, token);

        public override List<Part> Fill(
            List<Part> groupParts,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        ) => new DefaultNestEngine(Plate).Fill(groupParts, workArea, progress, token);

        public override List<Part> PackArea(
            Box box,
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        ) => new DefaultNestEngine(Plate).PackArea(box, items, progress, token);
    }

    /// <summary>Places exactly 2 parts from the first multi-quantity item and none from the
    /// rest, forcing the base-class deduction to run on an asymmetric placement result.</summary>
    private sealed class StarvingProbe(Plate plate) : NestEngineBase(plate)
    {
        private int _first = -1;
        public override string Name => "starving";
        public override string Description => "starves all but the first fill item";

        public override List<Part> Fill(
            NestItem item,
            Box workArea,
            IProgress<NestProgress> progress,
            CancellationToken token
        )
        {
            if (_first < 0)
                _first = 1;
            if (_first++ != 1)
                return new List<Part>();
            var parts = new List<Part>();
            var x = 0.0;
            for (var i = 0; i < 2; i++)
            {
                var p = new Part(item.Drawing);
                p.Offset(new Vector(x, 0));
                x += item.Drawing.Program.BoundingBox().Width + Plate.PartSpacing;
                parts.Add(p);
            }
            return parts;
        }
    }
}
