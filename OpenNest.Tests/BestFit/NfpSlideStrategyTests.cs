using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.BestFit;

public class NfpSlideStrategyTests
{
    [Fact]
    public void GenerateCandidates_ReturnsNonEmpty_ForSquare()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void GenerateCandidates_AllCandidatesHaveCorrectDrawing()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Same(drawing, c.Drawing));
    }

    [Fact]
    public void GenerateCandidates_Part1RotationIsAlwaysZero()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, Angle.HalfPI, 1, "90 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(0, c.Part1Rotation));
    }

    [Fact]
    public void GenerateCandidates_Part2RotationMatchesStrategy()
    {
        var rotation = Angle.HalfPI;
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, rotation, 1, "90 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(rotation, c.Part2Rotation));
    }

    [Fact]
    public void GenerateCandidates_ProducesReasonableCandidateCount()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);

        // Convex hull NFP for a square produces vertices + edge samples.
        // Should have more than just vertices but not thousands.
        Assert.True(candidates.Count >= 4);
        Assert.True(candidates.Count < 1000);
    }

    [Fact]
    public void GenerateCandidates_MoreCandidates_WithSmallerStepSize()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var largeStepStrategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        var smallStepStrategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        Assert.NotNull(largeStepStrategy);
        Assert.NotNull(smallStepStrategy);
        var largeStep = largeStepStrategy.GenerateCandidates(drawing, 0.25, 5.0);
        var smallStep = smallStepStrategy.GenerateCandidates(drawing, 0.25, 0.5);
        Assert.True(smallStep.Count >= largeStep.Count);
    }

    [Fact]
    public void Create_ReturnsNull_ForEmptyDrawing()
    {
        var pgm = new Program();
        var drawing = new Drawing("empty", pgm);
        var strategy = NfpSlideStrategy.Create(drawing, 0, 1, "0 deg NFP", 0.25);
        Assert.Null(strategy);
    }

    [Fact]
    public void GenerateCandidates_LShape_ProducesCandidates()
    {
        var lshape = TestHelpers.MakeLShapeDrawing();
        var strategy = NfpSlideStrategy.Create(lshape, 0, 1, "0 deg NFP", 0.25);
        Assert.NotNull(strategy);
        var candidates = strategy.GenerateCandidates(lshape, 0.25, 0.25);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void GenerateCandidates_At180Degrees_ProducesAtLeastOneNonOverlappingCandidate()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var strategy = NfpSlideStrategy.Create(drawing, System.Math.PI, 1, "180 deg NFP", 1.0);
        Assert.NotNull(strategy);
        // Use a large spacing (1.0) and step size.
        // This should make NFP much larger than the parts.
        var candidates = strategy.GenerateCandidates(drawing, 1.0, 1.0);

        Assert.NotEmpty(candidates);

        var part1 = Part.CreateAtOrigin(drawing);
        var validCount = 0;
        foreach (var candidate in candidates)
        {
            var part2 = Part.CreateAtOrigin(drawing, candidate.Part2Rotation);
            part2.Location = candidate.Part2Offset;

            // With 1.0 spacing, parts should NOT intersect even with tiny precision errors.
            if (!part1.Intersects(part2, out _))
                validCount++;
        }

        Assert.True(validCount > 0, $"No non-overlapping candidates found out of {candidates.Count} total. Candidate 0 offset: {candidates[0].Part2Offset}");
    }
}
