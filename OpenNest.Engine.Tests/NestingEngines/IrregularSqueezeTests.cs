using System.Threading;
using OpenNest.Clamps;
using OpenNest.CutOffs;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularSqueezeTests
{
    private static OrderedSqueeze Squeeze(IReadOnlyList<global::OpenNest.Engine.NestingEngines.Irregular.PartType> types, NestPlateStock stock, WorkCounter? counter = null) =>
        new(types, new NoFitCache(stock.PartSpacing), stock,
            stock.ClampZones.Count == 0 ? null : new ClampObstacles(stock), counter ?? new WorkCounter(),
            type => type.Orientations);

    /// <summary>One copy of each type, side by side along X in type order: a layout to shorten.</summary>
    private static List<Placed> InARow(IReadOnlyList<global::OpenNest.Engine.NestingEngines.Irregular.PartType> types, NestPlateStock stock, params int[] order)
    {
        var placed = new List<Placed>();
        var x = stock.WorkArea.Left;
        foreach (var index in order)
        {
            var pose = types[index].Orientations[0];
            placed.Add(new Placed(pose, x - pose.MinX, stock.WorkArea.Bottom - pose.MinY));
            x += pose.Width + stock.PartSpacing;
        }
        return placed;
    }

    private static NestJobResult Result(NestJob job, IReadOnlyList<global::OpenNest.Engine.NestingEngines.Irregular.PartType> types, SheetFill fill)
    {
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(fill.Stock, fill.Parts.Select(p => (types[p.Orientation.TypeIndex].Part.Id, p.X, p.Y, p.Orientation.Rotation)));
        return builder.Build(NestJobStopReason.Completed);
    }

    [Fact]
    public void PartsBehindALongOneStackOnIt()
    {
        // Two squares in front of a bar reach 59; the squares fit above the bar, which is 40 long.
        var stock = Stock("sheet", 20, 60, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("bar", Rectangle(40, 8), 1, RotationPolicy.Fixed(0)),
            Part("a", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
            Part("b", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
        }, new[] { stock });
        var types = PartCatalog.Build(job);
        var current = InARow(types, stock, 1, 2, 0);
        Assert.True(current.Max(p => p.Right) > 58);

        var fill = Squeeze(types, stock).Fill(current, PackAxis.X, CancellationToken.None);

        Assert.NotNull(fill);
        Assert.Equal(3, fill!.Parts.Count);
        Assert.True(fill.Parts.Max(p => p.Right) < 41, $"reaches {fill.Parts.Max(p => p.Right)}");
        LayoutAssert.Valid(job, Result(job, types, fill));
    }

    [Fact]
    public void SqueezesAcrossTheSheetToo()
    {
        var stock = Stock("sheet", 60, 20, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("bar", Rectangle(8, 40), 1, RotationPolicy.Fixed(0)),
            Part("a", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
            Part("b", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
        }, new[] { stock });
        var types = PartCatalog.Build(job);
        var bar = types[0].Orientations[0];
        var square = types[1].Orientations[0];
        var other = types[2].Orientations[0];
        var work = stock.WorkArea;
        // The squares below the bar, stacked along Y.
        var current = new List<Placed>
        {
            new(square, work.Left - square.MinX, work.Bottom - square.MinY),
            new(other, work.Left - other.MinX, work.Bottom + 9.25 - other.MinY),
            new(bar, work.Left - bar.MinX, work.Bottom + 18.5 - bar.MinY),
        };

        var fill = Squeeze(types, stock).Fill(current, PackAxis.Y, CancellationToken.None);

        Assert.NotNull(fill);
        Assert.True(fill!.Parts.Max(p => p.Top) < 41, $"reaches {fill.Parts.Max(p => p.Top)}");
        LayoutAssert.Valid(job, Result(job, types, fill));
    }

    [Fact]
    public void ASheetThatCannotBeShorterIsKept()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("a", Rectangle(50, 9), 1, RotationPolicy.Fixed(0)),
            Part("b", Rectangle(50, 9), 1, RotationPolicy.Fixed(0)),
        }, new[] { stock });
        var types = PartCatalog.Build(job);
        var a = types[0].Orientations[0];
        var b = types[1].Orientations[0];
        var work = stock.WorkArea;
        var current = new List<Placed>
        {
            new(a, work.Left - a.MinX, work.Bottom - a.MinY),
            new(b, work.Left - b.MinX, work.Bottom + 9.25 - b.MinY),
        };

        Assert.Null(Squeeze(types, stock).Fill(current, PackAxis.X, CancellationToken.None));
    }

    [Fact]
    public void OnePartOrTooManyAreLeftAlone()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("box", Rectangle(1, 1), OrderedSqueeze.MaxParts + 1, RotationPolicy.Fixed(0)) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var pose = types[0].Orientations[0];
        var one = new List<Placed> { new(pose, 30, 5) };
        var many = Enumerable.Range(0, OrderedSqueeze.MaxParts + 1).Select(i => new Placed(pose, i * 1.5, 5)).ToList();

        Assert.Null(Squeeze(types, stock).Fill(one, PackAxis.X, CancellationToken.None));
        Assert.Null(Squeeze(types, stock).Fill(many, PackAxis.X, CancellationToken.None));
    }

    [Fact]
    public void PartsKeepClearOfClamps()
    {
        var clamp = new ClampZone("clamp", new Box(20, 0, 2, 1));
        var stock = Stock("sheet", 20, 60, spacing: 0.25, edge: new Spacing(0.25, 0.25), clampZones: new[] { clamp });
        var job = Job(new[]
        {
            Part("bar", Rectangle(40, 8), 1, RotationPolicy.Fixed(0)),
            Part("a", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
            Part("b", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
        }, new[] { stock });
        var types = PartCatalog.Build(job);
        // Clear of the clamp: the row starts above it.
        var current = InARow(types, stock, 1, 2, 0).Select(p => p with { Y = p.Y + 2 }).ToList();

        var fill = Squeeze(types, stock).Fill(current, PackAxis.X, CancellationToken.None);

        Assert.NotNull(fill);
        LayoutAssert.Valid(job, Result(job, types, fill!));
    }

    [Fact]
    public void WorkIsCountedAndBounded()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("bar", Rectangle(40, 8), 1, RotationPolicy.Fixed(0)),
            Part("a", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
            Part("b", Rectangle(9, 9), 1, RotationPolicy.Fixed(0)),
        }, new[] { stock });
        var types = PartCatalog.Build(job);
        var counter = new WorkCounter();

        Squeeze(types, stock, counter).Fill(InARow(types, stock, 1, 2, 0), PackAxis.X, CancellationToken.None);

        Assert.InRange(counter.Value, 1, OrderedSqueeze.MaxWork + OrderedSqueeze.TryWork);
    }

    [Fact]
    public void FewerPartsGetLessWorkPerStrip()
    {
        Assert.Equal(OrderedSqueeze.TryWork, OrderedSqueeze.TryBudget(11));
        Assert.Equal(OrderedSqueeze.TryWork, OrderedSqueeze.TryBudget(OrderedSqueeze.MaxParts));
        Assert.InRange(OrderedSqueeze.TryBudget(8), OrderedSqueeze.TryBudget(5) + 1, OrderedSqueeze.TryWork - 1);
        Assert.True(OrderedSqueeze.TryBudget(2) >= 1);
    }

    [Fact]
    public void TheSameSheetSqueezesTheSameWay()
    {
        var stock = Stock("sheet", 30, 80, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        NestJobResult Solve()
        {
            var job = Job(new[]
            {
                Part("bar", Rectangle(45, 9), 2, RotationPolicy.Automatic),
                Part("disc", Disc(6), 2, RotationPolicy.Automatic),
                Part("ell", LShape(12, 10, 4), 2, RotationPolicy.Automatic),
                Part("tri", Triangle(10, 7), 2, RotationPolicy.Automatic),
            }, new[] { stock });
            var result = new IrregularNestingEngine().Solve(job);
            LayoutAssert.Valid(job, result);
            return result;
        }

        var first = Solve();
        var second = Solve();

        Assert.Equal(first.Plates.Single().Placements, second.Plates.Single().Placements);
    }

    [Fact]
    public void TheEngineEndsNoLongerThanWithoutTheSqueeze()
    {
        var stock = Stock("sheet", 30, 80, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("bar", Rectangle(45, 9), 2, RotationPolicy.Automatic),
            Part("disc", Disc(6), 2, RotationPolicy.Automatic),
            Part("ell", LShape(12, 10, 4), 2, RotationPolicy.Automatic),
            Part("tri", Triangle(10, 7), 2, RotationPolicy.Automatic),
        }, new[] { stock });

        var plain = new IrregularNestingEngine { SqueezeLastSheet = false }.Solve(job);
        var squeezed = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, squeezed);
        Assert.Equal(NestJobStatus.Complete, squeezed.Status);
        Assert.Single(squeezed.Plates);
        Assert.True(NestJobCost.CompactnessOffcut(job, squeezed) > NestJobCost.CompactnessOffcut(job, plain),
            $"{NestJobCost.CompactnessOffcut(job, squeezed)} against {NestJobCost.CompactnessOffcut(job, plain)}");
    }

    [Fact]
    public void LockedPartsAreNotTurned()
    {
        var stock = Stock("sheet", 30, 80, spacing: 0.25, edge: new Spacing(0.25, 0.25));
        var job = Job(new[]
        {
            Part("bar", Rectangle(45, 9), 2, RotationPolicy.Fixed(0)),
            Part("ell", LShape(12, 10, 4), 2, RotationPolicy.Fixed(0)),
            Part("tri", Triangle(10, 7), 3, RotationPolicy.Fixed(0)),
        }, new[] { stock });

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.All(result.Plates.SelectMany(p => p.Placements), p => Assert.Equal(0, p.Rotation));
    }

    [Theory]
    [InlineData(null, 48.0, 96.0, "X")]
    [InlineData(null, 96.0, 48.0, "X,Y")]
    [InlineData(CutOffAxis.Vertical, 96.0, 48.0, "X")]
    [InlineData(CutOffAxis.Horizontal, 48.0, 96.0, "Y")]
    public void TheSqueezeFollowsTheOffcutTheJobCounts(CutOffAxis? cut, double width, double length, string axes)
    {
        var stock = Stock("sheet", width, length);
        var job = Job(new[] { Part("box", Rectangle(1, 1), 1) }, new[] { stock },
            new NestJobOptions("Lattice", offcutCut: cut));

        Assert.Equal(axes, string.Join(",", IrregularNestingEngine.SqueezeAxes(job, stock)));
    }
}
