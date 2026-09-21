using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Direct filler-backed coverage for the built-in plate nesters (Default/Strip/remnant). The
/// runner's placement validator enforces geometric safety on every committed candidate, so these
/// tests assert fulfillment, status, and the identity/rotation safety boundaries; exact committed
/// layouts are pinned separately by <see cref="GoldenLayoutTests"/>.
/// </summary>
public class PlateNesterParityTests
{
    private const double Tolerance = 1e-6;

    private static readonly Size PlateSize = new(30, 50);
    private static readonly Spacing Edge = new(1, 1, 1, 1);

    private static NestJob Job(
        IReadOnlyList<NestJobPart> parts,
        int? stockQuantity = 3,
        string strategy = "Default"
    )
    {
        var stock = new NestPlateStock("stock", PlateSize, stockQuantity, 1, Edge);
        return new NestJob(parts, new[] { stock }, new NestJobOptions(strategy));
    }

    private static NestJobResult Solve(IPlateNester nester, NestJob job) =>
        new NestJobRunner(_ => nester).Solve(job);

    private static Dictionary<string, PartFulfillment> ByPart(NestJobResult result) =>
        result.Fulfillment.ToDictionary(f => f.PartId, StringComparer.Ordinal);

    private static bool AnglesEqual(double left, double right)
    {
        var delta = (left - right) % (System.Math.PI * 2);
        return System.Math.Abs(delta) <= Tolerance
            || System.Math.Abs(System.Math.Abs(delta) - System.Math.PI * 2) <= Tolerance;
    }

    [Fact]
    public void DefaultDirectFiller_Rectangles_FulfilledAndValid()
    {
        var parts = new[]
        {
            new NestJobPart(
                "a",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
                4
            ),
            new NestJobPart(
                "b",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)),
                3
            ),
        };

        var result = Solve(new DefaultPlateNester(), Job(parts));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(4, ByPart(result)["a"].Placed);
        Assert.Equal(3, ByPart(result)["b"].Placed);
        var usage = Assert.Single(result.StockUsage);
        Assert.Equal(1, usage.Used);
        Assert.Equal(7, result.Plates.SelectMany(p => p.Placements).Count());
    }

    [Fact]
    public void StripDirectFiller_Rectangles_FulfilledAndValid()
    {
        var parts = new[]
        {
            new NestJobPart(
                "a",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
                4
            ),
            new NestJobPart(
                "b",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)),
                3
            ),
        };

        var result = Solve(new StripPlateNester(), Job(parts, strategy: "Strip"));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(4, ByPart(result)["a"].Placed);
        Assert.Equal(3, ByPart(result)["b"].Placed);
        Assert.Equal(7, result.Plates.SelectMany(p => p.Placements).Count());
    }

    [Fact]
    public void Builtins_AreResolvedByProductionFactory()
    {
        Assert.IsType<DefaultPlateNester>(PlateNesterFactory.Create("Default"));
        Assert.IsType<StripPlateNester>(PlateNesterFactory.Create("Strip"));
        Assert.IsType<RemnantPlateNester>(PlateNesterFactory.Create("Vertical Remnant"));
        Assert.IsType<RemnantPlateNester>(PlateNesterFactory.Create("Horizontal Remnant"));
        Assert.Throws<NotSupportedException>(() => PlateNesterFactory.Create("not registered"));
    }

    [Fact]
    public void AsymmetricPart_ValidAndFulfilled()
    {
        // L-shape: 6x4 outer with a corner notch removed (single closed contour, asymmetric).
        var lshape = new Program();
        lshape.MoveTo(0, 0);
        lshape.LineTo(6, 0);
        lshape.LineTo(6, 4);
        lshape.LineTo(3, 4);
        lshape.LineTo(3, 2);
        lshape.LineTo(0, 2);
        lshape.LineTo(0, 0);

        var parts = new[]
        {
            new NestJobPart("l", PartGeometrySnapshot.FromProgram(lshape), 3),
            new NestJobPart(
                "sq",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)),
                2
            ),
        };
        var result = Solve(new DefaultPlateNester(), Job(parts));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(3, ByPart(result)["l"].Placed);
        Assert.Equal(2, ByPart(result)["sq"].Placed);
        Assert.Equal(5, result.Plates.SelectMany(p => p.Placements).Count());
    }

    [Fact]
    public void HoleAndArcParts_ValidAndFulfilled()
    {
        // 6x6 rectangle with a 2x2 inner hole (rapid contour), plus a D-shape with a semicircular arc.
        var holed = new Program();
        holed.MoveTo(0, 0);
        holed.LineTo(6, 0);
        holed.LineTo(6, 6);
        holed.LineTo(0, 6);
        holed.LineTo(0, 0);
        holed.MoveTo(2, 2);
        holed.LineTo(4, 2);
        holed.LineTo(4, 4);
        holed.LineTo(2, 4);
        holed.LineTo(2, 2);

        var arc = new Program();
        arc.MoveTo(0, 0);
        arc.LineTo(3, 0);
        arc.ArcTo(3, 5, 3, 2.5, RotationType.CCW);
        arc.LineTo(0, 5);
        arc.LineTo(0, 0);

        var parts = new[]
        {
            new NestJobPart("holed", PartGeometrySnapshot.FromProgram(holed), 2),
            new NestJobPart("arc", PartGeometrySnapshot.FromProgram(arc), 2),
        };
        var result = Solve(new DefaultPlateNester(), Job(parts));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, ByPart(result)["holed"].Placed);
        Assert.Equal(2, ByPart(result)["arc"].Placed);
    }

    [Fact]
    public void FixedRotation_Respected()
    {
        var part = new NestJobPart(
            "fixed",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
            2,
            rotation: RotationPolicy.Fixed(0)
        );
        var result = Solve(new DefaultPlateNester(), Job(new[] { part }));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var placements = result.Plates.SelectMany(p => p.Placements).ToList();
        Assert.Equal(2, placements.Count);
        foreach (var placement in placements)
            Assert.True(
                AnglesEqual(placement.Rotation, 0),
                $"fixed rotation violated: {placement.Rotation}"
            );
    }

    [Fact]
    public void DefaultRestrictedRotation_NeverTouchesFiller()
    {
        // Safety rule, not layout: any non-automatic rotation must bypass the Default fill
        // pipeline entirely — its Pairs/RectBestFit strategies rotate freely and would propose
        // forbidden poses. A throwing filler factory proves the filler is never constructed, and
        // completion at the locked angle proves OrderedPlateNester handled the request.
        var part = new NestJobPart(
            "fixed",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
            2,
            rotation: RotationPolicy.Fixed(0)
        );
        var nester = new DefaultPlateNester(_ =>
            throw new InvalidOperationException("filler must not run for restricted rotation")
        );

        var result = Solve(nester, Job(new[] { part }));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var placements = result.Plates.SelectMany(p => p.Placements).ToList();
        Assert.Equal(2, placements.Count);
        Assert.All(placements, p => Assert.True(AnglesEqual(p.Rotation, 0)));
    }

    [Fact]
    public void RemnantRestrictedRotation_NeverTouchesFiller()
    {
        // Same safety rule for the remnant nester, whose fillers inherit the Default pipeline's
        // automatic-rotation limitation. The throwing factory proves the filler is never
        // constructed; completion at the locked angle proves OrderedPlateNester handled it.
        var part = new NestJobPart(
            "fixed",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
            2,
            rotation: RotationPolicy.Fixed(0)
        );
        var nester = new RemnantPlateNester(_ =>
            throw new InvalidOperationException("filler must not run for restricted rotation")
        );

        var result = Solve(nester, Job(new[] { part }, strategy: "Vertical Remnant"));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var placements = result.Plates.SelectMany(p => p.Placements).ToList();
        Assert.Equal(2, placements.Count);
        Assert.All(placements, p => Assert.True(AnglesEqual(p.Rotation, 0)));
    }

    [Fact]
    public void RepeatedNames_KeepIndependentIdentity()
    {
        // Two distinct requirements sharing identical geometry (and, via the mapper, name) but different IDs.
        var program = TestDrawingFactory.Rectangle(6, 4);
        var parts = new[]
        {
            new NestJobPart("first", PartGeometrySnapshot.FromProgram(program), 2),
            new NestJobPart("second", PartGeometrySnapshot.FromProgram(program), 1),
        };
        var result = Solve(new DefaultPlateNester(), Job(parts));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, ByPart(result)["first"].Placed);
        Assert.Equal(1, ByPart(result)["second"].Placed);
        var ids = result.Plates.SelectMany(p => p.Placements).Select(p => p.PartId);
        Assert.Equal(2, ids.Count(id => id == "first"));
        Assert.Equal(1, ids.Count(id => id == "second"));
    }

    [Fact]
    public void OffsetGeometry_ValidAndFulfilled()
    {
        // Part contour starting at a nonzero origin (offset geometry).
        var program = new Program();
        program.MoveTo(12, 7);
        program.LineTo(18, 7);
        program.LineTo(18, 11);
        program.LineTo(12, 11);
        program.LineTo(12, 7);

        var part = new NestJobPart("offset", PartGeometrySnapshot.FromProgram(program), 2);
        var result = Solve(new DefaultPlateNester(), Job(new[] { part }));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, ByPart(result)["offset"].Placed);
        // The runner's validator guarantees containment and non-overlap for every committed placement.
    }

    [Fact]
    public void RunScopedCache_DrawingReusedAcrossTrials()
    {
        // 14x9 parts on 30x20: one sheet holds fewer than five, so the runner runs multiple candidate
        // trials through the same nester instance. The run-scoped drawing cache must keep producing
        // valid, correctly-attributed placements across trials.
        var part = new NestJobPart(
            "p",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(14, 9)),
            5
        );
        var stock = new NestPlateStock("stock", new Size(30, 20), 3);
        var nester = new DefaultPlateNester();
        var result = new NestJobRunner(_ => nester).Solve(
            new NestJob(new[] { part }, new[] { stock })
        );

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(5, result.Fulfillment.Single(f => f.PartId == "p").Placed);
        Assert.Equal(2, result.Plates.Count);
        var usage = result.StockUsage.Single();
        Assert.Equal(2, usage.Used);
        Assert.Equal(1, usage.Remaining);
    }

    [Fact]
    public void DirectRemnantStrategies_FulfillThroughFactory()
    {
        // Remnant strategies resolve to the direct remnant nester and still fulfill after the
        // legacy adapter was deleted.
        var part = new NestJobPart(
            "p",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 4)),
            2
        );
        foreach (var strategy in new[] { "Vertical Remnant", "Horizontal Remnant" })
        {
            var result = Solve(
                PlateNesterFactory.Create(strategy),
                Job(new[] { part }, strategy: strategy)
            );
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Assert.Equal(2, result.Fulfillment.Single(f => f.PartId == "p").Placed);
        }
    }
}
