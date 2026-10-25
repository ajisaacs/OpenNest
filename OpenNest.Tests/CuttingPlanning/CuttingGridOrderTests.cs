using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>Large plates of parts in aligned rows and columns are toured as a serpentine.</summary>
public class CuttingGridOrderTests
{
    private const double Diameter = 0.94;

    [Fact]
    public void InterleavedGrid_IsToured_LineByLine_AlternatingDirection()
    {
        var extents = Interleaved(12, 20).Select(Extent).ToArray();

        var order = CuttingGridOrder.Plan(extents, Vector.Zero, default);

        Assert.NotNull(order);
        Assert.Equal(Enumerable.Range(0, extents.Length), order.Order());
        // Columns hold every other row 1.2 apart, rows every other column 2.06 apart: the route
        // runs up and down the columns, from the corner nearest the start.
        var x = order.Select(i => extents[i].Center.X).ToArray();
        var y = order.Select(i => extents[i].Center.Y).ToArray();
        Assert.Equal(0, order[0]);
        var line = 0;
        var direction = 1;
        for (var i = 1; i < order.Length; i++)
        {
            if (System.Math.Abs(x[i] - x[i - 1]) < 1e-9)
            {
                Assert.Equal(direction, System.Math.Sign(y[i] - y[i - 1]));
                continue;
            }
            Assert.True(x[i] > x[i - 1], $"Column {line} is left before it is finished.");
            line++;
            direction = -direction;
        }
        Assert.Equal(23, line);
    }

    [Fact]
    public void SmallPlate_KeepsTheGeneralOrder()
    {
        var extents = Interleaved(6, 10).Select(Extent).ToArray();

        Assert.True(extents.Length < CuttingGridOrder.MinimumParts);
        Assert.Null(CuttingGridOrder.Plan(extents, Vector.Zero, default));
    }

    [Fact]
    public void ScatteredParts_AreNotAGrid()
    {
        var random = new Random(3);
        var extents = Enumerable.Range(0, 300)
            .Select(_ => Extent(new Vector(random.NextDouble() * 100, random.NextDouble() * 100))).ToArray();

        Assert.Null(CuttingGridOrder.Plan(extents, Vector.Zero, default));
    }

    [Fact]
    public void DriftingRows_AreNotAGrid()
    {
        // The rows slope upward by 0.1 per unit of X: no gap separates one row from the next.
        var extents = Interleaved(12, 20).Select(centre => Extent(centre + new Vector(0, centre.X * 0.1))).ToArray();

        Assert.Null(CuttingGridOrder.Plan(extents, Vector.Zero, default));
    }

    [Fact]
    public void TwoPartsInOneCell_AreNotAGrid()
    {
        var centres = Interleaved(12, 20).ToList();
        centres[5] = centres[4] + new Vector(0.1, 0.1);

        Assert.Null(CuttingGridOrder.Plan(centres.Select(Extent).ToArray(), Vector.Zero, default));
    }

    [Fact]
    public void WasherGrid_PlansReady_AlongTheSerpentine()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(30, 30);
        var drawing = new Drawing("washer", Washer());
        foreach (var centre in Interleaved(12, 20))
            plate.Parts.Add(new Part(drawing, centre - new Vector(Diameter / 2, Diameter / 2)));
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
            InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
        };

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            expansionBudget: CuttingPlanBatch.PlateBudget(plate.Parts.Count), confirmedParameters: parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: " + string.Join("; ", result.Findings.Take(3).Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        var expected = CuttingGridOrder.Plan(plate.Parts.Select(p => p.BoundingBox).ToArray(), Vector.Zero, default);
        Assert.Equal(expected, result.ProposedOrder.Select(p => p.SourceOrdinal));
    }

    // Rows 0.6 apart, each holding every other column of a 1.03 pitch, alternate rows offset by
    // one column: the interleaved layout of round parts nested as tightly as they go.
    private static IEnumerable<Vector> Interleaved(int perRow, int rows) =>
        Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, perRow)
            .Select(column => new Vector(1 + (2 * column + row % 2) * 1.03, 1 + row * 0.6)));

    private static Box Extent(Vector centre) =>
        new(centre.X - Diameter / 2, centre.Y - Diameter / 2, Diameter, Diameter);

    private static Program Washer()
    {
        var p = new Program();
        p.MoveTo(Diameter, Diameter / 2);
        p.ArcTo(Diameter, Diameter / 2, Diameter / 2, Diameter / 2, RotationType.CCW);
        p.MoveTo(Diameter / 2 + 0.2, Diameter / 2);
        p.ArcTo(Diameter / 2 + 0.2, Diameter / 2, Diameter / 2, Diameter / 2, RotationType.CCW);
        return p;
    }
}
