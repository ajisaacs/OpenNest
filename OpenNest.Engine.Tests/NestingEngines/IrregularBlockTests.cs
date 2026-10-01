using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularBlockTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> NoPairs =
        new Dictionary<int, IReadOnlyList<PairPose>>();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SmallDemandNeverPreparesAFill(int quantity)
    {
        var job = Job(new[] { Part("ell", LShape(9, 7, 3), quantity, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 40, 60, spacing: 0.25) });
        var types = PartCatalog.Build(job);
        using var catalog = new BlockCatalog(0.25, types, NoPairs);
        Assert.Empty(catalog.Get(types[0], quantity, new Box(0, 0, 60, 40), CancellationToken.None));
        Assert.Equal(0, catalog.PreparationCount);
    }

    [Fact]
    public void FillProposalIsTrimmedAndCertifiedWithoutChangingSingleRotations()
    {
        var job = Job(new[] { Part("ell", LShape(9, 7, 3), 7, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 40, 60, spacing: 0.25) });
        var types = PartCatalog.Build(job);
        var original = types[0].Orientations.ToArray();
        using var catalog = new BlockCatalog(0.25, types, NoPairs);
        var block = catalog.Get(types[0], 7, new Box(0, 0, 60, 40), CancellationToken.None);
        Assert.Equal(7, block.Count);
        Assert.Equal(original, types[0].Orientations);
        var geometry = JobPartGeometry.Read(job.Parts[0].Geometry);
        for (var i = 0; i < block.Count; i++)
            for (var j = i + 1; j < block.Count; j++)
                Assert.True(NestLayoutCheck.Clears(geometry,
                    new NestJobPlacement("ell", i, block[i].X, block[i].Y, block[i].Orientation.Rotation), geometry,
                    new NestJobPlacement("ell", j, block[j].X, block[j].Y, block[j].Orientation.Rotation), 0.25));
        Assert.Same(block, catalog.Get(types[0], 7, new Box(0, 0, 60, 40), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2.1)]
    public void InvalidInternalSpacingRejectsTheWholeProposal(double offset)
    {
        var program = Shapes.Rectangle(2, 2);
        var job = Job(new[] { Part("box", program, 3, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 20, 20, spacing: 0.25) });
        var type = PartCatalog.Build(job)[0];
        var drawing = new Drawing("box", program);
        var members = Enumerable.Range(0, 3).Select(i => new OpenNest.Part(drawing)
        { Location = new Vector(i * offset, 0) }).ToArray();
        var orientations = type.Orientations.ToList();
        Assert.Empty(BlockCatalog.Resolve(type, members, orientations, 0.25));
        Assert.Equal(type.Orientations, orientations);
    }

    [Fact]
    public void PhysicalFreeRectanglesExcludeOccupiedMaterialAndClearance()
    {
        var job = Job(new[] { Rectangle("box", 4, 10, 1) }, new[] { Stock("sheet", 10, 20) });
        var type = PartCatalog.Build(job)[0];
        var part = new Placed(type.Orientations[0], 8, 0);
        var rectangles = BlockCatalog.Rectangles(new Box(0, 0, 20, 10), new[] { part }, 0.25);
        Assert.NotEmpty(rectangles);
        Assert.All(rectangles, box => Assert.True(box.Right <= 7.75 || box.Left >= 12.25));
    }

    [Fact]
    public void RepeatedEllBlockCompetesWithPairsOnAnOpenSheet()
    {
        var stock = Stock("sheet", 30, 40, spacing: 0.25);
        var job = Job(new[] { Part("ell", LShape(9, 7, 3), 44, RotationPolicy.Automatic) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var pairs = PairCatalog.Build(types, 0.25, 40, 30, CancellationToken.None);
        using var blocks = new BlockCatalog(0.25, types, pairs);
        var before = new FrontierPacker(types, new NoFitCache(0.25), pairs, stock,
            PackAxis.X, 1, new WorkCounter()).Fill(new[] { 44 }, CancellationToken.None);
        var after = new FrontierPacker(types, new NoFitCache(0.25), pairs, stock,
            PackAxis.X, 1, new WorkCounter(), blocks).Fill(new[] { 44 }, CancellationToken.None);
        Assert.True(after.Parts.Count > before.Parts.Count,
            $"Before {before.Parts.Count}; after {after.Parts.Count}");
        var result = new NestJobResultBuilder(job);
        result.AddSheet(stock, after.Parts.Select(p => ("ell", p.X, p.Y, p.Orientation.Rotation)));
        LayoutAssert.Valid(job, result.Build(NestJobStopReason.StockExhausted));
    }

    [Fact]
    public void UndersizedRectangleKeepsSinglesFallback()
    {
        var stock = Stock("sheet", 3, 3, spacing: 0.25);
        var job = Job(new[] { Rectangle("box", 2, 2, 3) }, new[] { stock });
        var types = PartCatalog.Build(job);
        using var blocks = new BlockCatalog(0.25, types, NoPairs);
        Assert.Empty(blocks.Get(types[0], 3, new Box(0, 0, 3, 3), CancellationToken.None));
        Assert.Equal(0, blocks.PreparationCount);
        var fill = new FrontierPacker(types, new NoFitCache(0.25), NoPairs, stock,
            PackAxis.X, 1, new WorkCounter(), blocks).Fill(new[] { 3 }, CancellationToken.None);
        Assert.Single(fill.Parts);
    }

    [Fact]
    public void ForbiddenRotationsAreRejectedBeforeAddingGroupOrientations()
    {
        var program = LShape(9, 7, 3);
        var job = Job(new[] { Part("ell", program, 3, RotationPolicy.Fixed(0)) },
            new[] { Stock("sheet", 40, 60, spacing: 0.25) });
        var type = PartCatalog.Build(job)[0];
        var drawing = new Drawing("ell", program);
        var members = Enumerable.Range(0, 3).Select(i =>
        {
            var part = OpenNest.Part.CreateAtOrigin(drawing, System.Math.PI / 2);
            part.Offset(new Vector(i * 12, 0));
            return part;
        }).ToArray();
        var orientations = type.Orientations.ToList();
        Assert.Empty(BlockCatalog.Resolve(type, members, orientations, 0.25));
        Assert.Equal(type.Orientations, orientations);
    }

    [Fact]
    public void CancellationDuringCertificationPropagates()
    {
        var program = Shapes.Rectangle(2, 2);
        var job = Job(new[] { Part("box", program, 3, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 20, 20, spacing: 0.25) });
        var type = PartCatalog.Build(job)[0];
        var drawing = new Drawing("box", program);
        using var cancellation = new CancellationTokenSource();
        var members = new CancellingMembers(Enumerable.Range(0, 3)
            .Select(i => new OpenNest.Part(drawing) { Location = new Vector(i * 3, 0) }).ToArray(), cancellation);
        Assert.Throws<OperationCanceledException>(() => BlockCatalog.Resolve(type, members,
            type.Orientations.ToList(), 0.25, cancellation.Token));
    }

    private sealed class CancellingMembers(OpenNest.Part[] parts, CancellationTokenSource cancellation)
        : IReadOnlyList<OpenNest.Part>
    {
        public int Count => parts.Length;
        public OpenNest.Part this[int index] => parts[index];
        public IEnumerator<OpenNest.Part> GetEnumerator()
        {
            foreach (var part in parts)
                yield return part;
            cancellation.Cancel();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void CancellationIsNotConvertedIntoAnEmptyProposal()
    {
        var job = Job(new[] { Rectangle("box", 2, 2, 3) }, new[] { Stock("sheet", 10, 20) });
        var types = PartCatalog.Build(job);
        using var catalog = new BlockCatalog(0.25, types, NoPairs);
        Assert.Throws<OperationCanceledException>(() =>
            catalog.Get(types[0], 3, new Box(0, 0, 20, 10), new CancellationToken(true)));
    }
}
