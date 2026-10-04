using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutLatticeFillTests
{
    private const double Spacing = 0.25;

    // A 20" ring around a 10" round cutout. At 0.25" spacing the usable circle is 9.5" across;
    // the largest square inside it is 6.72", which holds a 5 x 5 grid of 1" squares.
    private const int InscribedRectangleCount = 25;

    private static NestJobPart Ring() => JobBuilder.Part("ring", TestShapes.Ring(20, 10), 1);

    private static NestJobPart Squares(double side, int quantity) =>
        JobBuilder.Part("square", TestShapes.Rectangle(side, side), quantity);

    [Fact]
    public void ShiftedLatticeFillsMoreOfARoundCutoutThanItsInscribedRectangle()
    {
        var poses = CutoutLatticeFill.Fill(Ring(), 0, Squares(1, 100), 100, Spacing);

        Assert.True(poses.Count > InscribedRectangleCount, $"placed {poses.Count}");
        AssertValidLayout(Ring(), Squares(1, poses.Count), poses);
    }

    [Fact]
    public void ShiftingTheLatticeKeepsMoreCopiesThanTheUnshiftedLattice()
    {
        var shifted = CutoutLatticeFill.Fill(Ring(), 0, Squares(1, 100), 100, Spacing);
        var unshifted = CutoutLatticeFill.Fill(Ring(), 0, Squares(1, 100), 100, Spacing, 0, default);

        Assert.True(shifted.Count > unshifted.Count, $"shifted {shifted.Count}, unshifted {unshifted.Count}");
    }

    [Theory]
    [InlineData(7.0)] // diagonal 9.90: wider than the cutout less its wall spacing
    [InlineData(6.75)] // corners 4.773 from the centre; the usable radius is 4.75
    public void InsertThatCannotClearTheCutoutWallIsNeverPlaced(double side)
    {
        Assert.Empty(CutoutLatticeFill.Fill(Ring(), 0, Squares(side, 4), 4, Spacing));
    }

    [Fact]
    public void QuantityCapsTheCopiesReturned()
    {
        var poses = CutoutLatticeFill.Fill(Ring(), 0, Squares(1, 5), 5, Spacing);

        Assert.Equal(5, poses.Count);
        AssertValidLayout(Ring(), Squares(1, 5), poses);
    }

    /// <summary>Places the ring 1" in from the sheet corner, moves the copies with it and runs
    /// the production layout check over the whole sheet.</summary>
    private static void AssertValidLayout(NestJobPart frame, NestJobPart insert, IReadOnlyList<NestJobPlacement> poses)
    {
        var stock = JobBuilder.Stock("sheet", 22, 22, Spacing);
        var job = JobBuilder.Job(new[] { frame, insert }, new[] { stock });
        var bounds = JobPartGeometry.Read(frame.Geometry).Bounds;
        var x = 1 - bounds.Left;
        var y = 1 - bounds.Bottom;
        var placements = new[] { new NestJobPlacement(frame.Id, 0, x, y, 0) }
            .Concat(poses.Select((p, i) => new NestJobPlacement(insert.Id, i, p.X + x, p.Y + y, p.Rotation)))
            .ToArray();
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, placements) },
            new[]
            {
                new PartFulfillment(frame.Id, 1, 1, 0),
                new PartFulfillment(insert.Id, insert.Quantity, poses.Count, insert.Quantity - poses.Count),
            },
            new[] { new StockUsage(stock.Id, 1, null) });

        LayoutAssert.Valid(job, result);
    }
}
