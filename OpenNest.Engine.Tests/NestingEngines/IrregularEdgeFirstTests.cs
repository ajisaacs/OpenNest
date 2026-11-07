using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularEdgeFirstTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> NoPairs =
        new Dictionary<int, IReadOnlyList<PairPose>>();

    private static EdgeFirstFill Packer(IReadOnlyList<global::OpenNest.Engine.NestingEngines.Irregular.PartType> types, NestPlateStock stock)
    {
        var nfps = new NoFitCache(stock.PartSpacing);
        var counter = new WorkCounter();
        var next = 1_000_000;
        return new EdgeFirstFill(types, nfps, stock, null, counter,
            () => new FrontierPacker(types, nfps, NoPairs, stock, PackAxis.X, 1, counter),
            (type, angle) => PartCatalog.CreateOrientation(type, next++, angle));
    }

    private static NestJobResult Result(NestJob job, IReadOnlyList<global::OpenNest.Engine.NestingEngines.Irregular.PartType> types, SheetFill fill)
    {
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(fill.Stock, fill.Parts.Select(p => (types[p.Orientation.TypeIndex].Part.Id, p.X, p.Y, p.Orientation.Rotation)));
        return builder.Build(NestJobStopReason.Completed);
    }

    [Fact]
    public void ContactsTurnAPartThatNoRightAngleFits()
    {
        // 61 long on a 60-long sheet: only a turn off the axes fits it, and the catalog has none.
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("bar", Rectangle(61, 2), 1, RotationPolicy.Automatic) }, new[] { stock });
        var types = PartCatalog.Build(job);
        Assert.DoesNotContain(types[0].Orientations, o => o.Width <= 60 && o.Height <= 20);

        var fill = Packer(types, stock).Fill(new[] { 1 }, CancellationToken.None);

        Assert.NotNull(fill);
        var rotation = Assert.Single(fill!.Parts).Orientation.Rotation;
        Assert.NotEqual(0, System.Math.IEEERemainder(rotation, System.Math.PI / 2), 3);
        LayoutAssert.Valid(job, Result(job, types, fill));
    }

    [Fact]
    public void ALockedRotationIsNeverTurned()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("bar", Rectangle(61, 2), 1, RotationPolicy.Fixed(0)) }, new[] { stock });
        var types = PartCatalog.Build(job);

        Assert.Null(Packer(types, stock).Fill(new[] { 1 }, CancellationToken.None));
    }

    [Fact]
    public void EdgePartsTakeOppositeCornersAndTheKeyFillsBetween()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[]
        {
            Part("a", Rectangle(20, 8), 1, RotationPolicy.Automatic),
            Part("b", Rectangle(20, 8), 1, RotationPolicy.Automatic),
            Part("key", Rectangle(19, 19), 1, RotationPolicy.Automatic),
        }, new[] { stock });
        var types = PartCatalog.Build(job);

        var fill = Packer(types, stock).Fill(new[] { 1, 1, 1 }, CancellationToken.None);

        Assert.NotNull(fill);
        Assert.Equal(3, fill!.Parts.Count);
        LayoutAssert.Valid(job, Result(job, types, fill));
    }

    [Fact]
    public void ReductionMergesTwoSheetsThatFitOne()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("box", Rectangle(10, 10), 2, RotationPolicy.Automatic) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var pose = types[0].Orientations[0];
        var sheet = new SheetFill(stock, new[] { new Placed(pose, -pose.MinX, -pose.MinY) }, types[0].Area);

        var result = new IrregularNestingEngine().ReduceSheetsForTesting(job, new[] { sheet, sheet });

        Assert.Single(result.Plates);
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void ReductionKeepsAPlanItCannotShorten()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("box", Rectangle(50, 15), 2, RotationPolicy.Automatic) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var pose = types[0].Orientations[0];
        var sheet = new SheetFill(stock, new[] { new Placed(pose, -pose.MinX, -pose.MinY) }, types[0].Area);

        var result = new IrregularNestingEngine().ReduceSheetsForTesting(job, new[] { sheet, sheet });

        Assert.Equal(2, result.Plates.Count);
        LayoutAssert.Valid(job, result);
    }
}
