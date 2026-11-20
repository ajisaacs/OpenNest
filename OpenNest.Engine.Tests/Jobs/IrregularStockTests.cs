using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Irregular stock: a <see cref="SheetOutline"/> on the stock reaches the Irregular packer as its
/// boundary, every placement stays inside the outline and off its obstacles, the layout check and
/// the runner's validation reject one that does not, and engines that only fill rectangles place
/// nothing on it.
/// </summary>
public class IrregularStockTests
{
    private const double Spacing = 0.5;

    /// <summary>A 96 x 48 sheet (X by Y) missing its upper-right 48 x 24 corner.</summary>
    private static SheetOutline Ell() => new(new[]
    {
        new Vector(0, 0), new Vector(96, 0), new Vector(96, 24), new Vector(48, 24), new Vector(48, 48), new Vector(0, 48),
    });

    /// <summary>A 96 x 48 sheet whose upper-right corner is cut off by a diagonal from (72, 48) to (96, 24).</summary>
    private static SheetOutline Chamfered() => new(new[]
    {
        new Vector(0, 0), new Vector(96, 0), new Vector(96, 24), new Vector(72, 48), new Vector(0, 48),
    });

    /// <summary>A whole 96 x 48 sheet with a 20 x 20 burned patch in the middle.</summary>
    private static SheetOutline Burned() => new(
        new[] { new Vector(0, 0), new Vector(96, 0), new Vector(96, 48), new Vector(0, 48) },
        new[] { new[] { new Vector(38, 14), new Vector(58, 14), new Vector(58, 34), new Vector(38, 34) } });

    private static NestPlateStock Stock(SheetOutline outline, int quadrant = 1, Spacing edge = default, int? quantity = 1) =>
        new("drop", outline.Size, quantity, Spacing, edge, quadrant, null, null, outline);

    private static NestJobPart Square(string id, double side, int quantity) =>
        new(id, PartGeometrySnapshot.FromProgram(TestShapes.Rectangle(side, side)), quantity);

    private static void AssertInsideOutline(NestJob job, NestJobResult result)
    {
        Assert.Empty(NestLayoutCheck.Violations(job, result));
        foreach (var sheet in result.Plates)
        {
            var usable = sheet.Stock.UsableOutline();
            foreach (var pose in sheet.Placements)
            {
                var geometry = JobPartGeometry.Read(job.Parts.Single(p => p.Id == pose.PartId).Geometry);
                var perimeter = (Shape)geometry.Perimeter.Clone();
                perimeter.Rotate(pose.Rotation);
                perimeter.Offset(pose.X, pose.Y);
                var path = ClipperBridge.ToPath(ClipperBridge.Flatten(perimeter, 0.001, circumscribe: true), positive: true);
                var outside = Clipper.Difference(new PathsD { path }, usable, FillRule.NonZero, 4);
                Assert.True(outside.Sum(p => System.Math.Abs(Clipper.Area(p))) <= 0,
                    $"{pose.PartId} at ({pose.X:F2},{pose.Y:F2}) leaves the usable outline");
            }
        }
    }

    [Fact]
    public void Stock_CarriesTheOutlineAndItsArea()
    {
        var stock = Stock(Ell(), quadrant: 3, edge: new Spacing(1, 0.5, 0.25, 0.75));

        Assert.Equal(96 * 48 - 48 * 24, stock.Area, 6);
        Assert.Equal(1, stock.OutlineInset);
        Assert.Equal(new Vector(-96, -48), stock.PlacementOrigin);
        var usable = stock.UsableOutline();
        var bounds = Clipper.GetBounds(usable);
        Assert.Equal(-95, bounds.left, 6);
        Assert.Equal(-47, bounds.top, 6);
        Assert.Equal(-1, bounds.right, 6);
        Assert.Equal(-1, bounds.bottom, 6);
        Assert.Throws<ArgumentException>(() =>
            new NestPlateStock("bad", new Size(48, 100), 1, Spacing, default, 1, null, null, Ell()));
    }

    [Fact]
    public void Mapper_CarriesTheOutlineBothWays()
    {
        var plate = new Plate { Outline = Ell(), Quadrant = 2 };
        var stock = DrawingJobMapper.FromPlate("s", plate, null);
        Assert.Equal(Ell(), stock.Outline);
        var rebuilt = DrawingJobMapper.CreatePlate(stock);
        Assert.Equal(Ell(), rebuilt.Outline);
        Assert.Equal(plate.Size, rebuilt.Size);
        Assert.Equal(2, rebuilt.Quadrant);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Irregular_FillsAnEllSheetWithoutEnteringTheMissingCorner(int quadrant)
    {
        // Six 20 x 20 squares fit: four along the 96 x 24 arm and two more on the 48 x 48 column.
        var job = new NestJob([Square("sq", 20, 6)], [Stock(Ell(), quadrant)]);

        var result = new IrregularNestingEngine().Solve(job);

        Assert.Equal(6, result.Plates.Sum(p => p.Placements.Count));
        AssertInsideOutline(job, result);
    }

    [Fact]
    public void Irregular_KeepsTheLargestEdgeSpacingFromADiagonalEdge()
    {
        var job = new NestJob([Square("sq", 10, 40)], [Stock(Chamfered(), edge: new Spacing(0.25, 1.5, 0.25, 0.25))]);

        var result = new IrregularNestingEngine().Solve(job);

        // The chamfer removes a 24 x 24 triangle (288) from 4608; at 1.5 inset about 36 squares fit.
        Assert.InRange(result.Plates.Sum(p => p.Placements.Count), 30, 40);
        AssertInsideOutline(job, result);
    }

    [Fact]
    public void Irregular_NestsAroundAnObstacle()
    {
        var job = new NestJob([Square("sq", 15, 12)], [Stock(Burned())]);

        var result = new IrregularNestingEngine().Solve(job);

        Assert.True(result.Plates.Sum(p => p.Placements.Count) >= 10);
        AssertInsideOutline(job, result);
    }

    [Fact]
    public void Default_UsesIrregularForAnOutlinedSheetAndRectanglesPlacesNothing()
    {
        var job = new NestJob([Square("sq", 20, 6)], [Stock(Ell())]);

        var rectangles = new RectanglesNestingEngine().Solve(job);
        Assert.Equal(0, rectangles.Plates.Sum(p => p.Placements.Count));
        Assert.Equal(NestJobStopReason.NoPlacementFound, rectangles.StopReason);

        var fill = new FixedStrategyNestingEngine("Fill").Solve(job);
        Assert.Equal(0, fill.Plates.Sum(p => p.Placements.Count));

        var chosen = new DefaultNestingEngine().Solve(job);
        Assert.Equal(6, chosen.Plates.Sum(p => p.Placements.Count));
        AssertInsideOutline(job, chosen);
    }

    [Fact]
    public void Default_PrefersARectangleWhenAnOutlinedSheetIsAlsoOffered()
    {
        var rectangle = new NestPlateStock("sheet", new Size(48, 96), 1, Spacing);
        var job = new NestJob([Square("sq", 20, 6)], [Stock(Ell()), rectangle]);

        var result = new DefaultNestingEngine().Solve(job);

        Assert.Equal(6, result.Plates.Sum(p => p.Placements.Count));
        Assert.Empty(NestLayoutCheck.Violations(job, result));
    }

    [Fact]
    public void LayoutCheck_RejectsAPartInTheMissingCornerOrOverAnObstacle()
    {
        var corner = new NestJob([Square("sq", 20, 2)], [Stock(Ell())]);
        var inCorner = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, corner.Plates[0], [new NestJobPlacement("sq", 0, 1, 1, 0), new NestJobPlacement("sq", 1, 60, 26, 0)])],
            [new PartFulfillment("sq", 2, 2, 0)], [new StockUsage("drop", 1, 0)]);
        var violation = Assert.Single(NestLayoutCheck.Violations(corner, inCorner));
        Assert.Contains("(60.00,26.00)", violation);
        Assert.Contains("outline", violation);

        var burned = new NestJob([Square("sq", 20, 1)], [Stock(Burned())]);
        var onPatch = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, burned.Plates[0], [new NestJobPlacement("sq", 0, 30, 20, 0)])],
            [new PartFulfillment("sq", 1, 1, 0)], [new StockUsage("drop", 1, 0)]);
        Assert.Contains(NestLayoutCheck.Violations(burned, onPatch), v => v.Contains("obstacle"));

        // A placement inside the arm, exactly at the edge, passes.
        var fine = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, corner.Plates[0], [new NestJobPlacement("sq", 0, 0, 0, 0), new NestJobPlacement("sq", 1, 76, 4, 0)])],
            [new PartFulfillment("sq", 2, 2, 0)], [new StockUsage("drop", 1, 0)]);
        Assert.Empty(NestLayoutCheck.Violations(corner, fine));
    }

    // A disc turned 0.1 rad puts a vertex of its circumscribed outline about 0.0009 past its
    // true bottom and top: on an outlined sheet that used to be a violation, on a rectangle not.
    private const double Turned = 0.1;
    private const double Radius = 1.5;

    private static readonly Spacing Quarter = new(0.25, 0.25, 0.25, 0.25);

    /// <summary>Disc centers resting on the Ell's usable edge at 0.25 edge spacing: the box's
    /// bottom and left edges, and the arm's top (y = 24), an outline wall inside the box.</summary>
    public static TheoryData<double, double> OnTheUsableEdge => new()
    {
        { 20, 0.25 + Radius },
        { 0.25 + Radius, 20 },
        { 70, 23.75 - Radius },
    };

    private static NestJob DiscJob(SheetOutline outline) =>
        new([new NestJobPart("disc", PartGeometrySnapshot.FromProgram(TestShapes.Disc(Radius)), 1)],
            [Stock(outline, edge: Quarter)]);

    private static NestJobResult DiscAt(NestJob job, double x, double y) =>
        new(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, job.Plates[0], [new NestJobPlacement("disc", 0, x, y, Turned)])],
            [new PartFulfillment("disc", 1, 1, 0)], [new StockUsage("drop", 1, 0)]);

    [Theory]
    [MemberData(nameof(OnTheUsableEdge))]
    public void LayoutCheck_AcceptsARoundPartRestingOnTheUsableEdge(double x, double y)
    {
        var job = DiscJob(Ell());

        Assert.Empty(NestLayoutCheck.Violations(job, DiscAt(job, x, y)));
    }

    [Fact]
    public void LayoutCheck_RejectsARoundPartPastTheUsableEdge()
    {
        // 0.001 into the edge spacing of the arm's top: the true material is past the edge.
        var job = DiscJob(Ell());

        var violation = Assert.Single(NestLayoutCheck.Violations(job, DiscAt(job, 70, 23.751 - Radius)));
        Assert.Contains("outline", violation);
    }

    [Theory]
    [MemberData(nameof(OnTheUsableEdge))]
    public void Runner_AcceptsARoundCandidateRestingOnTheUsableEdge(double x, double y)
    {
        var job = DiscJob(Ell());

        NestJobValidator.ValidateCandidate(new PlateCandidate([new NestJobPlacement("disc", 0, x, y, Turned)]),
            job.Plates[0], new Dictionary<string, int> { ["disc"] = 1 }, job.Parts.ToDictionary(p => p.Id));
    }

    [Fact]
    public void Runner_RefusesARoundCandidatePastTheUsableEdge()
    {
        var job = DiscJob(Ell());
        var candidate = new PlateCandidate([new NestJobPlacement("disc", 0, 70, 23.751 - Radius, Turned)]);

        var exception = Assert.Throws<InvalidOperationException>(() => NestJobValidator.ValidateCandidate(
            candidate, job.Plates[0], new Dictionary<string, int> { ["disc"] = 1 }, job.Parts.ToDictionary(p => p.Id)));
        Assert.Contains("usable stock area", exception.Message);
    }

    [Fact]
    public void RunnerAndLayoutCheck_AgreeOnARoundPartAtASlantedEdge()
    {
        // The chamfer runs from (72, 48) to (96, 24); its middle is (84, 36). The disc backs off
        // along the normal from resting on the usable edge (0.25 inside), turned every which way:
        // no straight box edge helps here, so both checks must reach one verdict pose by pose.
        var job = DiscJob(Chamfered());
        var parts = job.Parts.ToDictionary(p => p.Id);

        bool Runner(NestJobPlacement pose)
        {
            try
            {
                NestJobValidator.ValidateCandidate(new PlateCandidate([pose]), job.Plates[0],
                    new Dictionary<string, int> { ["disc"] = 1 }, parts);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        NestJobPlacement Pose(double back, double turn)
        {
            var d = (0.25 + Radius + back) / System.Math.Sqrt(2);
            return new NestJobPlacement("disc", 0, 84 - d, 36 - d, turn);
        }

        bool Layout(NestJobPlacement pose) => NestLayoutCheck.Violations(job, new NestJobResult(
            NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, job.Plates[0], [pose])],
            [new PartFulfillment("disc", 1, 1, 0)], [new StockUsage("drop", 1, 0)])).Count == 0;

        for (var k = -8; k <= 8; k++)
            for (var t = 0; t < 12; t++)
            {
                var pose = Pose(k * 0.00005, t * 0.137);
                Assert.True(Runner(pose) == Layout(pose), $"back {k * 0.00005}, turn {t * 0.137}");
            }

        // Clear of the edge by a grid unit both accept; 0.001 past it both refuse.
        for (var t = 0; t < 12; t++)
        {
            Assert.True(Runner(Pose(0.0001, t * 0.137)) && Layout(Pose(0.0001, t * 0.137)), $"turn {t * 0.137}");
            Assert.False(Runner(Pose(-0.001, t * 0.137)) || Layout(Pose(-0.001, t * 0.137)), $"turn {t * 0.137}");
        }
    }

    [Fact]
    public void Runner_RefusesACandidateOutsideTheOutline()
    {
        var job = new NestJob([Square("sq", 20, 1)], [Stock(Ell())]);
        var candidate = new PlateCandidate([new NestJobPlacement("sq", 0, 60, 26, 0)]);
        var parts = job.Parts.ToDictionary(p => p.Id);
        var remaining = new Dictionary<string, int> { ["sq"] = 1 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NestJobValidator.ValidateCandidate(candidate, job.Plates[0], remaining, parts));
        Assert.Contains("usable stock area", exception.Message);

        NestJobValidator.ValidateCandidate(new PlateCandidate([new NestJobPlacement("sq", 0, 76, 4, 0)]),
            job.Plates[0], remaining, parts);
    }

    [Fact]
    public void Pipeline_CommitsTheOutlineToThePlate()
    {
        var nest = new Nest("drop");
        var drawing = new Drawing("sq", TestShapes.Rectangle(20, 20));
        drawing.Quantity.Required = 6;
        nest.Drawings.Add(drawing);
        var template = new Plate { Outline = Ell(), PartSpacing = Spacing };
        nest.Plates.Add(template);
        var request = new NestPipelineRequest("Irregular",
            [new NestItem { Drawing = drawing, Quantity = 6 }], NestStockBuilder.SinglePlate(template), new NestJobOptions());

        var result = NestPipeline.Run(request);
        Assert.True(result.IsValid, string.Join("\n", result.Violations));
        var applied = NestPipelineCommit.ApplyToPlates(result, () => template);

        var plate = Assert.Single(applied);
        Assert.Equal(Ell(), plate.Outline);
        Assert.Equal(6, plate.Parts.Count);
    }

    [Fact]
    public void FillService_RefusesAnOutlinedPlate()
    {
        var plate = new Plate { Outline = Ell() };
        var item = new NestItem { Drawing = new Drawing("sq", TestShapes.Rectangle(20, 20)), Quantity = 1 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PlateFillService.FillItem("Fill", plate, item, plate.WorkArea(), null, default));
        Assert.Contains("rectangular", exception.Message);

        // Callers check the refusal first and show it instead of catching the exception.
        Assert.Equal(exception.Message, PlateFillService.Refusal(plate));
        Assert.Null(PlateFillService.Refusal(new Plate(48, 96)));
        Assert.Null(PlateFillService.Refusal(null));
    }

    [Fact]
    public void RepeatedPlates_DoNotGroupAnOutlinedSheetWithItsRectangle()
    {
        var outlined = Stock(Ell(), quantity: null);
        var plain = new NestPlateStock("drop", new Size(48, 96), null, Spacing);
        Assert.False(RepeatedPlateGrouping.SameStock(outlined, plain));
        Assert.True(RepeatedPlateGrouping.SameStock(outlined, Stock(Ell(), quantity: null)));
    }
}
