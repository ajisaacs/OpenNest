using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularClampZoneTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> NoPairs =
        new Dictionary<int, IReadOnlyList<PairPose>>();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CurvedPartsClearEveryInflatedClampInEveryQuadrant(int quadrant)
    {
        var stock = Stock("sheet", 10, 20, spacing: 0.1, quadrant: quadrant,
            edge: new Spacing(0.1, 0.25, 0.1, 0.1), clampZones:
            [new("left", new Box(0, 0, 3, 0.625)), new("middle", new Box(7, 0, 1.5, 0.625))]);
        var job = Job([Part("disc", Disc(1), 9, RotationPolicy.Fixed(0))], [stock]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(9, Assert.Single(result.Plates).Placements.Count);
        AssertDiscsClearClamps(result, 1);
        LayoutAssert.Valid(job, result);
        Assert.Contains(result.Plates[0].Placements, p => p.Y - 1 < stock.WorkArea.Bottom + 0.05);
    }

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Rectangles")]
    [InlineData("Default")]
    public void BothLanesAndDefaultAvoidClamps(string engine)
    {
        var job = Job([Part("disc", Disc(1), 2, RotationPolicy.Fixed(0))],
            [Stock("sheet", 5, 12, edge: new Spacing(0.25, 0.25, 0.25, 0.25),
                clampZones: [new("front", new Box(0, 0, 3, 0.625))])]);
        INestingEngine solver = engine switch
        {
            "Irregular" => new IrregularNestingEngine(),
            "Rectangles" => new RectanglesNestingEngine(),
            _ => new DefaultNestingEngine(),
        };
        var result = solver.Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
        AssertDiscsClearClamps(result, 1);
        LayoutAssert.Valid(job, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ClampClearanceUsesBottomEdgeSpacingNotPartSpacing(double partSpacing)
    {
        var stock = Stock("sheet", 4, 5, spacing: partSpacing,
            edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new("rail", new Box(0, 0, 5, 0.625))]);
        var job = Job([Part("disc", Disc(1), 1, RotationPolicy.Fixed(0))], [stock]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var pose = Assert.Single(Assert.Single(result.Plates).Placements);
        Assert.InRange(pose.Y - 1, 0.875, 0.90);
        AssertDiscsClearClamps(result, 1);
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void FullRailBlocksTheOnlyRowInsteadOfReturningAnUnsafePlacement()
    {
        var stock = Stock("sheet", 2.5, 6, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new("rail", new Box(0, 0, 6, 0.625))]);
        var job = Job([Part("disc", Disc(1), 1, RotationPolicy.Fixed(0))], [stock]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Empty(result.Plates);
        Assert.Equal(1, Assert.Single(result.Fulfillment).Unplaced);
        LayoutAssert.Valid(job, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PairOnlyOrientationsAlsoLoseClampBlockedSpace(int axis)
    {
        var stock = Stock("sheet", 10, 10, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new("rail", new Box(0, 0, 10, 1))]);
        var job = Job([Rectangle("bar", 2, 3, 2, RotationPolicy.Automatic)], [stock]);
        var types = PartCatalog.Build(job);
        var extra = PartCatalog.CreateOrientation(types[0], 100, System.Math.PI / 2)!;
        var pair = new PairPose { A = extra, B = extra, Dx = 0, Dy = 3 };
        var pairs = new Dictionary<int, IReadOnlyList<PairPose>> { [0] = new[] { pair } };
        var fill = new FrontierPacker(types, new NoFitCache(0), pairs, stock,
            (PackAxis)axis, 1, new WorkCounter()).Fill([2], default);

        Assert.Equal(2, fill.Parts.Count);
        Assert.All(fill.Parts, p => Assert.True(p.Bottom >= 1.25));
        if (axis == 0)
            Assert.All(fill.Parts, p => Assert.Same(extra, p.Orientation));
        AssertValidFill(job, fill);
    }

    [Fact]
    public void BlockOnlyOrientationsAlsoLoseClampBlockedSpace()
    {
        var stock = Stock("sheet", 20, 12, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new("rail", new Box(0, 0, 12, 1))]);
        var job = Job([Rectangle("bar", 2, 3, 6, RotationPolicy.Automatic)], [stock]);
        var types = PartCatalog.Build(job);
        using var blocks = new BlockCatalog(0.1, types, NoPairs, new WorkCounter(), long.MaxValue);
        var fill = new FrontierPacker(types, new NoFitCache(0.1), NoPairs, stock,
            PackAxis.Y, 1, new WorkCounter(), blocks).Fill([6], default);

        Assert.Equal(6, fill.Parts.Count);
        Assert.True(blocks.PreparationCount > 0);
        Assert.Contains(fill.Parts, p => !types[0].Orientations.Contains(p.Orientation));
        Assert.All(fill.Parts, p => Assert.True(p.Bottom >= 1.25));
        AssertValidFill(job, fill);
    }

    [Fact]
    public void SameSpacingStocksDoNotShareClampGeometry()
    {
        var wall = new ClampZone("rail", new Box(0, 0, 6, 1));
        var job = Job([Rectangle("part", 2, 2, 1)],
            [Stock("blocked", 2.5, 6, clampZones: [wall]), Stock("open", 2.5, 6)]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("open", Assert.Single(result.Plates).StockId);
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void ConcavePartCanUseItsOpenNotchAroundAClamp()
    {
        // The only fitting pose surrounds the rail in an open notch. Blocking the
        // part's bounding box instead of its NFP would incorrectly reject this job.
        var program = Polyline((0, 0), (1, 0), (1, 3), (3, 3),
            (3, 0), (4, 0), (4, 4), (0, 4));
        var stock = Stock("sheet", 4.2, 4, edge: new Spacing(0, 0.1, 0, 0),
            clampZones: [new("notch", new Box(1.5, 0, 1, 2))]);
        var job = Job([Part("notched", program, 1, RotationPolicy.Fixed(0))], [stock]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var pose = Assert.Single(Assert.Single(result.Plates).Placements);
        Assert.Equal(0, pose.X, 8);
        Assert.Equal(0.1, pose.Y, 8);
        // Analytic notch walls versus the rectangle inflated by 0.1.
        Assert.True(pose.X + 1 <= 1.4 && pose.X + 3 >= 2.6 && pose.Y + 3 >= 2.1);
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void DifferentClampedStocksAtTheSameSpacingKeepTheirOwnObstacles()
    {
        var job = Job([Rectangle("part", 2, 2, 2)],
            [Stock("left", 2.5, 6, quantity: 1, clampZones: [new("rail", new Box(0, 0, 3, 1))]),
             Stock("right", 2.5, 6, quantity: 1, clampZones: [new("rail", new Box(3, 0, 3, 1))])]);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, result.Plates.Count);
        foreach (var sheet in result.Plates)
        {
            var pose = Assert.Single(sheet.Placements);
            Assert.True(sheet.StockId == "left" ? pose.X >= 3 : pose.X + 2 <= 3);
        }
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void CancelledClampPackingDoesNotReturnAnEmptySuccess()
    {
        var stock = Stock("sheet", 4, 5, clampZones: [new("rail", new Box(0, 0, 5, 1))]);
        var job = Job([Rectangle("part", 2, 2, 1)], [stock]);
        var packer = new FrontierPacker(PartCatalog.Build(job), new NoFitCache(0), NoPairs,
            stock, PackAxis.X, 1, new WorkCounter());
        Assert.Throws<OperationCanceledException>(() => packer.Fill([1], new CancellationToken(true)));
    }

    [Theory]
    [InlineData(0, "0F90BA3EF6C6392CF1C1E01A6209D55CA88B4F968D6DD0D01850AE15A349FE88")]
    [InlineData(1, "A970D56C5768763CE339999F91ADD7FFB9EC2AF6C1585CC51C4B0869D45D0C53")]
    [InlineData(2, "65ECA7F7163DB0AB7BCA331FBF2A240604B25C44D62CE97CB2015FC78A9631AA")]
    // Full result hashes captured on d71f086 before static-obstacle preparation.
    public void EmptyClampLayoutsKeepBaselineBits(int fixture, string expected)
    {
        var job = fixture switch
        {
            0 => Job([Part("disc", Disc(1), 2, RotationPolicy.Fixed(0))], [Stock("sheet", 5, 12, spacing: 0.25)]),
            1 => Job([Part("ell", LShape(4, 3, 1), 1), Part("tri", Triangle(3, 2), 1)],
                [Stock("sheet", 10, 15, spacing: 0.25)]),
            _ => Job([Rectangle("square", 4, 4, 3)], [Stock("sheet", 5, 5, spacing: 0.25)]),
        };
        var result = new IrregularNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result))));
        Assert.Equal(expected, hash);
    }

    private static void AssertDiscsClearClamps(NestJobResult result, double radius)
    {
        foreach (var sheet in result.Plates)
            foreach (var pose in sheet.Placements)
                foreach (var zone in sheet.Stock.ClampZones)
                {
                    var b = zone.Bounds;
                    var c = sheet.Stock.EdgeSpacing.Bottom;
                    var dx = pose.X - System.Math.Clamp(pose.X, b.Left - c, b.Right + c);
                    var dy = pose.Y - System.Math.Clamp(pose.Y, b.Bottom - c, b.Top + c);
                    Assert.True(dx * dx + dy * dy >= radius * radius,
                        $"Disc at ({pose.X:R}, {pose.Y:R}) enters inflated clamp {zone.Id}");
                }
    }

    private static void AssertValidFill(NestJob job, SheetFill fill)
    {
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(fill.Stock, fill.Parts.Select(p =>
            (job.Parts[p.Orientation.TypeIndex].Id, p.X, p.Y, p.Orientation.Rotation)));
        LayoutAssert.Valid(job, builder.Build(NestJobStopReason.Completed));
    }
}
