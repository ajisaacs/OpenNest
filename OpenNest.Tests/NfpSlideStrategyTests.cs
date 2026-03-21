using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests;

public class NfpSlideStrategyTests
{
    [Fact]
    public void GenerateCandidates_ReturnsNonEmpty_ForSquare()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void GenerateCandidates_AllCandidatesHaveCorrectDrawing()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Same(drawing, c.Drawing));
    }

    [Fact]
    public void GenerateCandidates_Part1RotationIsAlwaysZero()
    {
        var strategy = new NfpSlideStrategy(Angle.HalfPI, 1, "90 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(0, c.Part1Rotation));
    }

    [Fact]
    public void GenerateCandidates_Part2RotationMatchesStrategy()
    {
        var rotation = Angle.HalfPI;
        var strategy = new NfpSlideStrategy(rotation, 1, "90 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(rotation, c.Part2Rotation));
    }

    [Fact]
    public void GenerateCandidates_NoDuplicateOffsets()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);

        var uniqueOffsets = candidates
            .Select(c => (System.Math.Round(c.Part2Offset.X, 6), System.Math.Round(c.Part2Offset.Y, 6)))
            .Distinct()
            .Count();
        Assert.Equal(candidates.Count, uniqueOffsets);
    }

    [Fact]
    public void GenerateCandidates_MoreCandidates_WithSmallerStepSize()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var largeStep = strategy.GenerateCandidates(drawing, 0.25, 5.0);
        var smallStep = strategy.GenerateCandidates(drawing, 0.25, 0.5);
        Assert.True(smallStep.Count >= largeStep.Count);
    }

    [Fact]
    public void GenerateCandidates_ReturnsEmpty_ForEmptyDrawing()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var pgm = new Program();
        var drawing = new Drawing("empty", pgm);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_LShape_ProducesMoreCandidates_ThanSquare()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var square = TestHelpers.MakeSquareDrawing();
        var lshape = TestHelpers.MakeLShapeDrawing();

        var squareCandidates = strategy.GenerateCandidates(square, 0.25, 0.25);
        var lshapeCandidates = strategy.GenerateCandidates(lshape, 0.25, 0.25);

        Assert.True(lshapeCandidates.Count > squareCandidates.Count);
    }

    [Fact]
    public void GenerateCandidates_At180Degrees_ProducesCandidates()
    {
        var strategy = new NfpSlideStrategy(System.Math.PI, 1, "180 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(System.Math.PI, c.Part2Rotation));
    }
}
