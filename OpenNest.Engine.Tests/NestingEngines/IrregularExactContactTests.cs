using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// A part whose material exactly fills the work area in one allowed rotation. The engine must
/// place it: preparation padding for arc flattening may not shrink a line-only part's room.
/// </summary>
public class IrregularExactContactTests
{
    private const double PartLength = 8;
    private const double PartWidth = 3;
    private const double Edge = 0.25;
    private const double Spacing = 0.25;

    /// <summary>Gap shortfall the independent oracle tolerates (float noise only).</summary>
    private const double OracleNoise = 1e-9;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExactContactRotatedRectangleIsPlacedInEveryQuadrant(int quadrant)
    {
        var job = RectangleJob(8.5, 3.5, quadrant, RotationPolicy.Automatic);

        var result = new IrregularNestingEngine().Solve(job);

        AssertPlacedSafely(job, result);
    }

    [Theory]
    [InlineData(8.501, 3.501)]
    [InlineData(8.5, 3.501)]
    [InlineData(8.501, 3.5)]
    [InlineData(8.52, 3.52)] // Generator smoke stock: a looser control, not exact-contact acceptance.
    public void LargerNeighbouringStockStillPlacesThePart(double width, double length)
    {
        var job = RectangleJob(width, length, 1, RotationPolicy.Automatic);

        AssertPlacedSafely(job, new IrregularNestingEngine().Solve(job));
    }

    [Theory]
    [InlineData(8.499, 3.5, 1)]
    [InlineData(8.5, 3.499, 1)]
    [InlineData(8.499, 3.499, 3)]
    public void StockOneThousandthTooSmallLeavesThePartUnplaced(double width, double length, int quadrant)
    {
        var job = RectangleJob(width, length, quadrant, RotationPolicy.Automatic);

        AssertUnplaced(job, new IrregularNestingEngine().Solve(job));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ZeroRotationOnlyCannotFitAndStaysUnplaced(int quadrant)
    {
        var job = RectangleJob(8.5, 3.5, quadrant, RotationPolicy.Fixed(0));

        AssertUnplaced(job, new IrregularNestingEngine().Solve(job));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void HandPoseAtExactContactPassesAndOneThousandthOverEdgeFails(int quadrant)
    {
        var job = RectangleJob(8.5, 3.5, quadrant, RotationPolicy.Automatic);
        var stock = job.Plates[0];
        var work = stock.WorkArea;
        // +90 degrees maps the source rectangle to X in [-3, 0], Y in [0, 8].
        var exact = new NestJobPlacement("rect", 0, work.Left + PartWidth, work.Bottom, System.Math.PI / 2);
        Assert.True(OracleGaps(stock, exact).Min() >= Edge - OracleNoise);
        Assert.Empty(Violations(job, exact));
        Validate(job, exact);

        foreach (var (dx, dy) in new[] { (0.001, 0.0), (-0.001, 0.0), (0.0, 0.001), (0.0, -0.001) })
        {
            var over = exact with { X = exact.X + dx, Y = exact.Y + dy };
            Assert.True(OracleGaps(stock, over).Min() < Edge - 0.0009);
            Assert.NotEmpty(Violations(job, over));
            Assert.Throws<InvalidOperationException>(() => Validate(job, over));
        }
    }

    [Fact]
    public void LineOnlyBoundsAreExactWhileFlatteningToleranceIsKept()
    {
        var job = RectangleJob(8.5, 3.5, 1, RotationPolicy.Automatic);
        var type = PartCatalog.Build(job).Single();
        var rotated = type.Orientations.Single(o => System.Math.Abs(o.Rotation - System.Math.PI / 2) < 1e-9);

        // Chord tolerance still drives tessellation and NFP footprints.
        Assert.Equal(PartCatalog.ChordTolerance, rotated.Tolerance);
        Assert.Equal(-PartWidth, rotated.MinX, 12);
        Assert.Equal(0, rotated.MinY, 12);
        Assert.Equal(0, rotated.MaxX, 12);
        Assert.Equal(PartLength, rotated.MaxY, 12);
        Assert.True(job.Plates[0].Fits(rotated.Width, rotated.Height));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(1.2)]
    public void ArcBoundsStillContainTheTruePerimeterAndTheFlattenedOutline(double angle)
    {
        var job = Job(new[] { Part("obround", Shapes.Obround(9, 4), 1, RotationPolicy.Fixed(angle)) },
            new[] { Stock("sheet", 40, 40, spacing: Spacing) });
        var type = PartCatalog.Build(job).Single();
        var o = type.Orientations.Single();
        Assert.True(o.Tolerance >= PartCatalog.ChordTolerance);

        // Independent oracle: sample the stadium analytically (straight sides plus semicircles).
        const double length = 9, width = 4, r = width / 2;
        var points = new List<(double X, double Y)>();
        for (var i = 0; i <= 2000; i++)
        {
            var t = System.Math.PI * i / 2000;
            points.Add((1 + length - r + r * System.Math.Sin(t), 1 + r - r * System.Math.Cos(t)));
            points.Add((1 + r - r * System.Math.Sin(t), 1 + r + r * System.Math.Cos(t)));
        }
        var (c, s) = (System.Math.Cos(angle), System.Math.Sin(angle));
        foreach (var (x, y) in points)
        {
            var rx = x * c - y * s;
            var ry = x * s + y * c;
            Assert.InRange(rx, o.MinX - 1e-9, o.MaxX + 1e-9);
            Assert.InRange(ry, o.MinY - 1e-9, o.MaxY + 1e-9);
        }
        foreach (var p in o.Outline)
        {
            Assert.InRange(p.x, o.MinX, o.MaxX);
            Assert.InRange(p.y, o.MinY, o.MaxY);
        }
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(System.Math.PI / 2, false)]
    [InlineData(System.Math.PI, false)]
    [InlineData(3 * System.Math.PI / 2, false)]
    [InlineData(0.0, true)]
    [InlineData(System.Math.PI / 2, true)]
    [InlineData(System.Math.PI, true)]
    [InlineData(3 * System.Math.PI / 2, true)]
    public void ShortEdgeChainBoundsKeepEveryAnalyticEndpoint(double angle, bool extraOrientation)
    {
        var job = Job(new[] { Part("rect", Shapes.Polyline(ShortEdgeChainVertices()), 1,
            RotationPolicy.Fixed(extraOrientation ? 0 : angle)) }, new[] { Stock("sheet", 20, 20) });
        var type = PartCatalog.Build(job).Single();
        var orientation = extraOrientation
            ? PartCatalog.CreateOrientation(type, 100, angle)!
            : type.Orientations.Single();
        Assert.NotNull(orientation);

        var (c, s) = (System.Math.Cos(angle), System.Math.Sin(angle));
        var endpoints = ShortEdgeChainVertices()
            .Select(p => (X: p.X * c - p.Y * s, Y: p.X * s + p.Y * c)).ToArray();
        Assert.Equal(endpoints.Min(p => p.X), orientation.MinX, 12);
        Assert.Equal(endpoints.Min(p => p.Y), orientation.MinY, 12);
        Assert.Equal(endpoints.Max(p => p.X), orientation.MaxX, 12);
        Assert.Equal(endpoints.Max(p => p.Y), orientation.MaxY, 12);
        Assert.Equal(PartCatalog.ChordTolerance, orientation.Tolerance);
    }

    [Fact]
    public void ShortEdgeChainCannotEscapeAnExactWorkArea()
    {
        var job = ShortEdgeChainJob(8.5);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Empty(NestLayoutCheck.Violations(job, result));
        AssertUnplaced(job, result);
    }

    [Fact]
    public void ShortEdgeChainFitsWhenStockContainsItsTrueExtent()
    {
        var job = ShortEdgeChainJob(8.500024);
        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal((1, 1, 0), (result.Fulfillment.Single().Requested,
            result.Fulfillment.Single().Placed, result.Fulfillment.Single().Unplaced));
        var sheet = Assert.Single(result.Plates);
        var pose = Assert.Single(sheet.Placements);
        Assert.True(OracleGaps(sheet.Stock, pose, ShortEdgeChainVertices()).Min() >= Edge - OracleNoise);
        LayoutAssert.Valid(job, result);
        Assert.Empty(NestLayoutCheck.Violations(job, result));
        Validate(job, pose);
    }

    // Polygon cleanup can collapse these individually short edges, but their accumulated
    // outward extent is real material and exceeds the unchanged work-area slack.
    private static (double X, double Y)[] ShortEdgeChainVertices() =>
        [(0, 0), (0, 3), (8, 3), (8.000008, 2.999992),
         (8.000016, 2.999984), (8.000024, 2.999976), (8, 0)];

    private static NestJob ShortEdgeChainJob(double length) =>
        Job(new[] { Part("rect", Shapes.Polyline(ShortEdgeChainVertices()), 1, RotationPolicy.Fixed(0)) },
            new[] { Stock("sheet", 3.5, length, spacing: Spacing, edge: new Spacing(Edge, Edge, Edge, Edge)) });

    private static NestJob RectangleJob(double width, double length, int quadrant, RotationPolicy rotation) =>
        Job(new[] { Rectangle("rect", PartLength, PartWidth, 1, rotation) },
            new[] { Stock("sheet", width, length, spacing: Spacing, edge: new Spacing(Edge, Edge, Edge, Edge), quadrant: quadrant) });

    private static void AssertPlacedSafely(NestJob job, NestJobResult result)
    {
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var fulfillment = Assert.Single(result.Fulfillment);
        Assert.Equal((1, 1, 0), (fulfillment.Requested, fulfillment.Placed, fulfillment.Unplaced));
        var sheet = Assert.Single(result.Plates);
        Assert.Equal(1, Assert.Single(result.StockUsage).Used);
        var pose = Assert.Single(sheet.Placements);

        Assert.True(OracleGaps(sheet.Stock, pose).Min() >= Edge - OracleNoise,
            $"pose ({pose.X:R}, {pose.Y:R}, {pose.Rotation:R}) gaps {string.Join(", ", OracleGaps(sheet.Stock, pose))}");
        LayoutAssert.Valid(job, result);
        Assert.Empty(NestLayoutCheck.Violations(job, result));
        Validate(job, pose);
    }

    private static void AssertUnplaced(NestJob job, NestJobResult result)
    {
        Assert.NotEqual(NestJobStatus.Complete, result.Status);
        Assert.Empty(result.Plates);
        var fulfillment = Assert.Single(result.Fulfillment);
        Assert.Equal((1, 0, 1), (fulfillment.Requested, fulfillment.Placed, fulfillment.Unplaced));
        LayoutAssert.Valid(job, result);
    }

    /// <summary>Clearance from the rotated nominal rectangle to each physical sheet edge
    /// (left, bottom, right, top), computed from corners and the quadrant's sheet origin.</summary>
    private static double[] OracleGaps(NestPlateStock stock, NestJobPlacement pose) =>
        OracleGaps(stock, pose, [(0, 0), (PartLength, 0), (PartLength, PartWidth), (0, PartWidth)]);

    private static double[] OracleGaps(NestPlateStock stock, NestJobPlacement pose,
        (double X, double Y)[] vertices)
    {
        var (c, s) = (System.Math.Cos(pose.Rotation), System.Math.Sin(pose.Rotation));
        var corners = vertices
            .Select(p => (X: p.X * c - p.Y * s + pose.X, Y: p.X * s + p.Y * c + pose.Y))
            .ToArray();
        var sheetLeft = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var sheetBottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        return new[]
        {
            corners.Min(p => p.X) - sheetLeft,
            corners.Min(p => p.Y) - sheetBottom,
            sheetLeft + stock.Size.Length - corners.Max(p => p.X),
            sheetBottom + stock.Size.Width - corners.Max(p => p.Y),
        };
    }

    private static IReadOnlyList<string> Violations(NestJob job, NestJobPlacement pose)
    {
        var stock = job.Plates[0];
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, new[] { pose }) },
            new[] { new PartFulfillment("rect", 1, 1, 0) }, new[] { new StockUsage(stock.Id, 1, null) });
        return NestLayoutCheck.Violations(job, result);
    }

    private static void Validate(NestJob job, NestJobPlacement pose) =>
        NestJobValidator.ValidateCandidate(new PlateCandidate(new[] { pose }), job.Plates[0],
            new Dictionary<string, int> { ["rect"] = 1 }, job.Parts.ToDictionary(p => p.Id));
}
