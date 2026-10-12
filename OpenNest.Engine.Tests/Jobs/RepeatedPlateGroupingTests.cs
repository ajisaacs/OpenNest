using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class RepeatedPlateGroupingTests
{
    private static NestItem Item(string name = "same-name", int quantity = 10) => new()
    {
        Drawing = new Drawing(name, TestDrawingFactory.Rectangle(2, 2)),
        Quantity = quantity,
    };

    private static NestPlateStock Stock(string id = "sheet") => new(id, new Size(20, 30), 10, 0.25);

    private sealed class Stub(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => solve(job);
    }

    private static NestJobResult Raw(IEnumerable<NestJobPlateResult> sheets) =>
        new(NestJobStatus.Complete, NestJobStopReason.Completed, sheets, [], []);

    private static NestJobPlateResult Sheet(NestPlateStock stock, int index, params NestJobPlacement[] poses) =>
        new(index, stock, poses);

    private static NestJobPlacement Pose(string id = "part-0", int instance = 0,
        double x = 1, double y = 1, double rotation = 0) => new(id, instance, x, y, rotation);

    [Fact]
    public void NonAdjacentOrderedDuplicatesKeepFirstOccurrenceAndIgnoreInstanceIndices()
    {
        var item = Item();
        var input = new NestPipelineRequest("stub", [item], [Stock()]);
        NestJobResult? raw = null;
        var result = NestPipeline.Run(new Stub(job => raw = Raw([
            Sheet(job.Plates[0], 7, Pose(instance: 0), Pose(instance: 1, x: 6)),
            Sheet(job.Plates[0], 8, Pose(instance: 2, x: 3)),
            Sheet(job.Plates[0], 9, Pose(instance: 3), Pose(instance: 4, x: 6))])), "stub", input);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Same(raw, result.Raw);
        Assert.Equal(new[] { 7, 8 }, result.Plates.Select(p => p.PlateIndex));
        Assert.Equal(new[] { 2, 1 }, result.Plates.Select(p => p.Quantity));
        Assert.Equal(new[] { 1d, 6d }, result.Plates[0].Parts.Select(p => p.Location.X));
        Assert.All(result.Plates.SelectMany(p => p.Parts), p => Assert.Same(item.Drawing, p.BaseDrawing));
        Assert.Equal(5, result.Plates.Sum(p => p.Parts.Count * p.Quantity));
        Assert.Equal(0, item.Drawing.Quantity.Nested);
        Assert.Empty(NestPipelineDrawingFreshness.BoundChanges(result.Job, result.Raw,
            result.Plates, result.DrawingsByPartId, consolidate: true));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("rotation")]
    [InlineData("negative-zero")]
    public void ScalarBitDifferenceDoesNotGroup(string field)
    {
        var second = field switch
        {
            "x" => Pose(instance: 1, x: double.BitIncrement(1)),
            "y" => Pose(instance: 1, y: double.BitIncrement(1)),
            "rotation" => Pose(instance: 1, rotation: double.BitIncrement(0)),
            _ => Pose(instance: 1, rotation: -0.0),
        };
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, second)])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock()]));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Plates.Count);
        Assert.All(result.Plates, p => Assert.Equal(1, p.Quantity));
    }

    [Fact]
    public void ReorderedPlacementsAndDistinctSameNameRequirementsStaySeparate()
    {
        var a = Item();
        var b = Item();
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose(), Pose("part-1", x: 6)),
            Sheet(job.Plates[0], 1, Pose("part-1", 1, x: 6), Pose(instance: 1)),
            Sheet(job.Plates[0], 2, Pose(instance: 2)),
            Sheet(job.Plates[0], 3, Pose("part-1", 2))])),
            "stub", new NestPipelineRequest("stub", [a, b], [Stock()]));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(4, result.Plates.Count);
        Assert.Same(a.Drawing, result.Plates[2].Parts[0].BaseDrawing);
        Assert.Same(b.Drawing, result.Plates[3].Parts[0].BaseDrawing);
        Assert.All(result.Plates, p => Assert.Equal(1, p.Quantity));
    }

    [Fact]
    public void SameSizeDifferentStockIdentityDoesNotGroup()
    {
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[1], 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock("a"), Stock("b")]));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Plates.Count);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("length")]
    [InlineData("spacing")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("quadrant")]
    [InlineData("cost")]
    [InlineData("quantity")]
    public void EveryStockSettingIsPartOfExactIdentity(string field)
    {
        var size = new Size(field == "width" ? double.BitIncrement(20) : 20,
            field == "length" ? double.BitIncrement(30) : 30);
        var spacing = new Spacing
        {
            Left = field == "left" ? double.Epsilon : 0,
            Right = field == "right" ? double.Epsilon : 0,
            Top = field == "top" ? double.Epsilon : 0,
            Bottom = field == "bottom" ? double.Epsilon : 0,
        };
        var second = new NestPlateStock("sheet", size, field == "quantity" ? 11 : 10,
            field == "spacing" ? double.BitIncrement(0.25) : 0.25,
            spacing, field == "quadrant" ? 2 : 1, field == "cost" ? 1 : null);
        var groups = RepeatedPlateGrouping.Create([
            Sheet(Stock(), 0, Pose()), Sheet(second, 1, Pose(instance: 1))], true);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void ToleranceEquivalentValidStockSettingsStillDoNotGroup()
    {
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()),
            Sheet(new NestPlateStock("sheet", job.Plates[0].Size, 10,
                double.BitIncrement(0.25)), 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock()]));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Plates.Count);
    }

    [Fact]
    public void EmptySheetsNeverGroup()
    {
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0), Sheet(job.Plates[0], 1)])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock()]));
        Assert.True(result.IsValid);
        Assert.Equal(2, result.Plates.Count);
        Assert.All(result.Plates, p => Assert.Equal(1, p.Quantity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantityWeightedCostAndSalvageMatchExpandedPhysicalSheets(bool priced)
    {
        var item = Item(quantity: 3);
        var stock = new NestPlateStock("sheet", new Size(20, 30), 4, 0.25,
            new Spacing(), 1, cost: priced ? 7 : null);
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, Pose(instance: 1)),
            Sheet(job.Plates[0], 2, Pose(instance: 2, x: 15))])), "stub",
            new NestPipelineRequest("stub", [item], [stock],
                new NestJobOptions(salvageRate: 0.5, minimumSalvageDimension: 1)));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { 2, 1 }, result.Plates.Select(p => p.Quantity));
        var summary = NestCostSummary.FromAccepted(result);
        var gross = result.Raw.Plates.Sum(p => NestJobCost.GrossSheetCost(p.Stock));
        var net = NestJobCost.Evaluate(result.Job, result.Raw);
        Assert.Equal(gross, summary.GrossTotal, 10);
        Assert.Equal(net, summary.NetScore, 10);
        Assert.Equal(gross - net, summary.SalvageCredit, 10);
        Assert.True(summary.SalvageCredit > 0);
        Assert.Equal(3, Assert.Single(summary.Stock).Used);
        Assert.Equal(gross, Assert.Single(summary.Stock).GrossSubtotal, 10);
    }

    [Fact]
    public void InvalidDuplicatesRemainPhysicalEvenWithExplicitCommitConsent()
    {
        var item = Item();
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose(), Pose(instance: 1)),
            Sheet(job.Plates[0], 1, Pose(instance: 2), Pose(instance: 3))])),
            "stub", new NestPipelineRequest("stub", [item], [Stock()]));
        Assert.False(result.IsValid);
        Assert.True(result.CanKeep);
        Assert.Equal(2, result.Plates.Count);
        Assert.All(result.Plates, p => Assert.Equal(1, p.Quantity));
        var nest = new Nest();
        nest.Drawings.Add(item.Drawing);
        using var manager = new PlateManager(nest);
        var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager, allowInvalid: true);
        Assert.Equal(2, applied.Count);
        Assert.Equal(4, item.Drawing.Quantity.Nested);
    }

    [Theory]
    [InlineData("ghost", 1)]
    [InlineData(null, 1)]
    [InlineData("part-0", double.NaN)]
    public void MalformedDuplicatesRemainUnkeepable(string? id, double x)
    {
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose(id!, x: x)),
            Sheet(job.Plates[0], 1, Pose(id!, 1, x: x))])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock()]));
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Equal(2, result.Raw.Plates.Count);
    }

    [Fact]
    public void ProposalKeepsThreeFieldConstructorAndDeconstructionAndRequiresPositiveQuantity()
    {
        var stock = Stock();
        var proposed = new ProposedPlate(4, stock, []);
        var (index, actualStock, parts) = proposed;
        Assert.Equal(4, index);
        Assert.Same(stock, actualStock);
        Assert.Empty(parts);
        Assert.Equal(1, proposed.Quantity);
        Assert.Equal(2, (proposed with { Quantity = 2 }).Quantity);
        Assert.Throws<ArgumentOutOfRangeException>(() => proposed with { Quantity = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => proposed with { Quantity = -1 });
    }

    [Theory]
    [InlineData("program")]
    [InlineData("pose")]
    [InlineData("drawing")]
    public void GroupedProposalFreshnessStillRefusesMutationBeforeAttachment(string field)
    {
        var item = Item(quantity: 2);
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [item], [Stock()]));
        var proposed = Assert.Single(result.Plates);
        Assert.Equal(2, proposed.Quantity);
        if (field == "program") ((Motion)proposed.Parts[0].Program.Codes[1]).EndPoint = new Vector(10, 10);
        else if (field == "pose") proposed.Parts[0].Location = new Vector(double.BitIncrement(1), 1);
        else item.Drawing.Program = TestDrawingFactory.Rectangle(10, 10);
        var nest = new Nest();
        nest.Drawings.Add(item.Drawing);
        using var manager = new PlateManager(nest);
        Assert.Throws<InvalidOperationException>(() => NestPipelineCommit.ApplyToEmptyPlates(result, manager, true));
        Assert.Empty(nest.Plates);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("stock")]
    [InlineData("index")]
    public void RawToGroupedMatchChecksQuantityStockAndFirstPhysicalIndex(string field)
    {
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [Item()], [Stock()]));
        var proposed = Assert.Single(result.Plates);
        var changed = field switch
        {
            "quantity" => proposed with { Quantity = 1 },
            "stock" => proposed with { Stock = Stock("other") },
            _ => proposed with { PlateIndex = 1 },
        };
        Assert.NotEmpty(NestPipelineDrawingFreshness.BoundChanges(result.Job, result.Raw,
            [changed], result.DrawingsByPartId, true));
    }

    private sealed class TransientMove(double x, double y) : LinearMove(x, y)
    {
        public override ICode Clone()
        {
            var before = EndPoint;
            EndPoint = new Vector(100, 100);
            try { return base.Clone(); }
            finally { EndPoint = before; }
        }
    }

    [Fact]
    public void GroupedBindingTransientCloneEditRemainsUnkeepable()
    {
        var item = Item();
        item.Drawing.Program.Codes[^1] = new TransientMove(0, 0);
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [item], [Stock()]));
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("Bound part"));
    }

    [Fact]
    public void CancelledGroupingAndCommitLeaveInputsUntouched()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => RepeatedPlateGrouping.Create([
            Sheet(Stock(), 0, Pose()), Sheet(Stock(), 1, Pose(instance: 1))], true, source.Token));
        var item = Item();
        var result = NestPipeline.Run(new Stub(job => Raw([
            Sheet(job.Plates[0], 0, Pose()), Sheet(job.Plates[0], 1, Pose(instance: 1))])),
            "stub", new NestPipelineRequest("stub", [item], [Stock()]));
        var nest = new Nest();
        using var manager = new PlateManager(nest);
        Assert.Throws<OperationCanceledException>(() => NestPipelineCommit.ApplyToEmptyPlates(result, manager,
            token: source.Token));
        Assert.Empty(nest.Plates);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }
}
