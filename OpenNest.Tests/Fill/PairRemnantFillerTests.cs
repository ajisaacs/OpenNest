using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Fill;

public class PairRemnantFillerTests
{
    private const double Spacing = 0.25;

    private static Drawing MakeTriangleDrawing() =>
        new("triangle", TestHelpers.MakeClosedProgram(
            new[] { new Vector(0, 0), new Vector(10, 0), new Vector(0, 4), new Vector(0, 0) }));

    /// <summary>
    /// A pair grid ending 5.5 short of the work area's right edge and reaching its top, so only
    /// the narrow right strip is left: one single fits across it, a pair of triangles laid as a
    /// rectangle fits too, and pairs hold twice as many.
    /// </summary>
    private static (List<Part> Grid, Box WorkArea) GridLeavingNarrowStrip(Drawing drawing)
    {
        var workArea = new Box(0, 0, 21, 50);
        var low = Part.CreateAtOrigin(drawing);
        var high = Part.CreateAtOrigin(drawing);
        high.Offset(new Vector(5.25, 46));
        high.UpdateBounds();
        return (new List<Part> { low, high }, workArea);
    }

    [Fact]
    public void Fill_NarrowStrip_PlacesRowsOfPairsWhenTheyHoldMore()
    {
        var drawing = MakeTriangleDrawing();
        var plate = new Plate { Size = new Size(60, 120), PartSpacing = Spacing };
        var bestFits = BestFitCache.GetOrCompute(drawing, 120, 60, Spacing);
        var (grid, workArea) = GridLeavingNarrowStrip(drawing);
        FillResultCache.Clear();

        var singles = new PairRemnantFiller(Spacing, new DefaultFillComparer())
            .Fill(grid, drawing, workArea, CancellationToken.None);
        var withPairs = new PairRemnantFiller(plate, new DefaultFillComparer())
            .Fill(grid, drawing, workArea, CancellationToken.None, bestFits);

        Assert.NotNull(singles);
        Assert.NotNull(withPairs);
        Assert.True(withPairs.Count > singles.Count,
            $"pairs placed {withPairs.Count}, singles {singles.Count}");

        var stripLeft = grid.Max(p => p.BoundingBox.Right) + Spacing;
        foreach (var part in withPairs)
        {
            Assert.True(part.BoundingBox.Left >= stripLeft - Tolerance.Epsilon);
            Assert.True(part.BoundingBox.Right <= workArea.Right + Tolerance.Epsilon);
            Assert.True(part.BoundingBox.Bottom >= workArea.Bottom - Tolerance.Epsilon);
            Assert.True(part.BoundingBox.Top <= workArea.Top + Tolerance.Epsilon);
        }

        var check = new Plate { Size = new Size(60, 120), PartSpacing = Spacing };
        check.Parts.AddRange(withPairs);
        Assert.Empty(PartOverlapChecker.FindOverlappingPairs(check.Parts));
    }

    [Fact]
    public void Fill_WithoutBestFits_KeepsSingles()
    {
        var drawing = MakeTriangleDrawing();
        var plate = new Plate { Size = new Size(60, 120), PartSpacing = Spacing };
        var (grid, workArea) = GridLeavingNarrowStrip(drawing);
        FillResultCache.Clear();

        var singles = new PairRemnantFiller(Spacing, new DefaultFillComparer())
            .Fill(grid, drawing, workArea, CancellationToken.None);
        var noPairs = new PairRemnantFiller(plate, new DefaultFillComparer())
            .Fill(grid, drawing, workArea, CancellationToken.None);

        Assert.Equal(singles?.Count ?? 0, noPairs?.Count ?? 0);
    }
}
