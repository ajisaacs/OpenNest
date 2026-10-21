using OpenNest.BestFit;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

[Collection(nameof(FillCacheCollection))]
public class BestFitCacheStoreTests
{
    private const double Spacing = 0.25;

    private static Drawing MakeDrawing() => new TShape { Width = 10, Height = 8 }.GetDrawing();

    private static BestFitResult MakeResult(Drawing drawing, bool keep, string reason = "Valid", List<double> hullAngles = null) =>
        new()
        {
            Candidate = new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = 0.5,
                Part2Rotation = System.Math.PI,
                Part2Offset = new Vector(3.25, -1.5),
                StrategyIndex = 2,
                TestNumber = 7,
                Spacing = Spacing,
            },
            RotatedArea = 90,
            BoundingWidth = 12,
            BoundingHeight = 7.5,
            OptimalRotation = 0.1,
            Keep = keep,
            Reason = reason,
            TrueArea = 70,
            HullAngles = hullAngles,
        };

    [Fact]
    public void Export_ReturnsOnlyRequestedPlateKeysAndKeptResults()
    {
        BestFitCache.Clear();
        var drawing = MakeDrawing();
        BestFitCache.Populate(drawing, 60, 40, Spacing, new()
        {
            MakeResult(drawing, keep: true, hullAngles: new() { 0.25 }),
            MakeResult(drawing, keep: false, reason: "Too wide"),
        });
        BestFitCache.Populate(drawing, 14, 30, Spacing, new() { MakeResult(drawing, keep: true) });

        var sets = BestFitCacheStore.Shared.Export(
            drawing, new HashSet<(double, double, double)> { (60, 40, Spacing), (99, 99, Spacing) });

        var set = Assert.Single(sets);
        Assert.Equal((60.0, 40.0, Spacing), (set.PlateWidth, set.PlateHeight, set.Spacing));
        Assert.Equal(drawing.Source.Angle, set.CanonicalAngle);
        var result = Assert.Single(set.Results);
        Assert.Equal(
            new SavedPairResult(0.5, System.Math.PI, 3.25, -1.5, 2, 7, Spacing, 90, 12, 7.5, 0.1, true, "Valid", 70, result.HullAngles),
            result);
        Assert.Equal(new[] { 0.25 }, result.HullAngles);
    }

    [Fact]
    public void Export_DrawingWithoutCacheEntry_ReturnsNoSets()
    {
        BestFitCache.Clear();

        var sets = BestFitCacheStore.Shared.Export(
            MakeDrawing(), new HashSet<(double, double, double)> { (60, 40, Spacing) });

        Assert.Empty(sets);
    }

    [Fact]
    public void Import_PopulatesCacheWithCandidatesOnACanonicalCopyOfTheDrawing()
    {
        BestFitCache.Clear();
        var drawing = MakeDrawing();
        var saved = new SavedPairResult(0.5, System.Math.PI, 3.25, -1.5, 2, 7, Spacing, 90, 12, 7.5, 0.1, true, "Valid", 70, new[] { 0.25, -0.5 });

        BestFitCacheStore.Shared.Import(
            drawing, new[] { new SavedPairSet(60, 40, Spacing, new[] { saved }, drawing.Source.Angle) });

        var restored = Assert.Single(BestFitCache.GetAllForDrawing(drawing));
        Assert.Equal((60.0, 40.0, Spacing), restored.Key);
        var result = Assert.Single(restored.Value);
        Assert.NotSame(drawing, result.Candidate.Drawing);
        Assert.Same(drawing, CanonicalFrame.SourceOf(result.Candidate.Drawing));
        Assert.Equal(0.0, result.Candidate.Drawing.Source.Angle);
        Assert.Equal(0.5, result.Candidate.Part1Rotation);
        Assert.Equal(System.Math.PI, result.Candidate.Part2Rotation);
        Assert.Equal(new Vector(3.25, -1.5), result.Candidate.Part2Offset);
        Assert.Equal(2, result.Candidate.StrategyIndex);
        Assert.Equal(7, result.Candidate.TestNumber);
        Assert.Equal(Spacing, result.Candidate.Spacing);
        Assert.Equal(90, result.RotatedArea);
        Assert.Equal(12, result.BoundingWidth);
        Assert.Equal(7.5, result.BoundingHeight);
        Assert.Equal(0.1, result.OptimalRotation);
        Assert.True(result.Keep);
        Assert.Equal("Valid", result.Reason);
        Assert.Equal(70, result.TrueArea);
        Assert.Equal(new[] { 0.25, -0.5 }, result.HullAngles);
    }

    [Fact]
    public void Import_ExistingSetForTheSameKey_IsKept()
    {
        BestFitCache.Clear();
        var drawing = MakeDrawing();
        var existing = new List<BestFitResult> { MakeResult(drawing, keep: true) };
        BestFitCache.Populate(drawing, 60, 40, Spacing, existing);
        var saved = new SavedPairResult(0, 0, 1, 1, 0, 1, Spacing, 5, 1, 5, 0, true, "Valid", 4, new double[0]);

        BestFitCacheStore.Shared.Import(
            drawing, new[] { new SavedPairSet(60, 40, Spacing, new[] { saved }, drawing.Source.Angle) });

        Assert.Same(existing, BestFitCache.GetAllForDrawing(drawing)[(60, 40, Spacing)]);
    }

    [Fact]
    public void Import_EmptySet_AddsNothing()
    {
        BestFitCache.Clear();
        var drawing = MakeDrawing();

        BestFitCacheStore.Shared.Import(
            drawing, new[] { new SavedPairSet(60, 40, Spacing, new SavedPairResult[0], drawing.Source.Angle) });

        Assert.Empty(BestFitCache.GetAllForDrawing(drawing));
    }

    [Fact]
    public void Import_SetSavedUnderTheCurrentCanonicalAngle_IsKept()
    {
        BestFitCache.Clear();
        var drawing = MakeRotatedDrawing();
        BestFitCache.Populate(drawing, 60, 40, Spacing, new() { MakeResult(drawing, keep: true) });
        var sets = BestFitCacheStore.Shared.Export(drawing, new HashSet<(double, double, double)> { (60, 40, Spacing) });
        BestFitCache.Clear();

        BestFitCacheStore.Shared.Import(drawing, sets);

        Assert.Single(Assert.Single(BestFitCache.GetAllForDrawing(drawing)).Value);
    }

    [Fact]
    public void Import_SetSavedUnderAnotherCanonicalAngle_IsDropped()
    {
        // A changed canonical-angle computation turns the drawing's frame; pairs saved in the
        // old frame would be replayed turned and could overlap.
        BestFitCache.Clear();
        var drawing = MakeRotatedDrawing();
        BestFitCache.Populate(drawing, 60, 40, Spacing, new() { MakeResult(drawing, keep: true) });
        var sets = BestFitCacheStore.Shared.Export(drawing, new HashSet<(double, double, double)> { (60, 40, Spacing) });
        BestFitCache.Clear();
        drawing.Source.Angle += 0.35;

        BestFitCacheStore.Shared.Import(drawing, sets);

        Assert.Empty(BestFitCache.GetAllForDrawing(drawing));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public void Import_SetWithoutAUsableCanonicalAngle_IsDropped(double? angle)
    {
        BestFitCache.Clear();
        var drawing = MakeDrawing();
        var saved = new SavedPairResult(0, 0, 1, 1, 0, 1, Spacing, 5, 1, 5, 0, true, "Valid", 4, new double[0]);

        BestFitCacheStore.Shared.Import(drawing, new[] { new SavedPairSet(60, 40, Spacing, new[] { saved }, angle) });

        Assert.Empty(BestFitCache.GetAllForDrawing(drawing));
    }

    [Fact]
    public void Import_AngleAWholeTurnAway_MatchesTheSameFrame()
    {
        BestFitCache.Clear();
        var drawing = MakeRotatedDrawing();
        var saved = new SavedPairResult(0, 0, 1, 1, 0, 1, Spacing, 5, 1, 5, 0, true, "Valid", 4, new double[0]);

        BestFitCacheStore.Shared.Import(
            drawing,
            new[] { new SavedPairSet(60, 40, Spacing, new[] { saved }, drawing.Source.Angle - 2 * System.Math.PI) });

        Assert.Single(BestFitCache.GetAllForDrawing(drawing));
    }

    private static Drawing MakeRotatedDrawing()
    {
        var drawing = MakeDrawing();
        drawing.Program.Rotate(0.5, drawing.Program.BoundingBox().Center);
        drawing.RecomputeCanonicalAngle();
        Assert.NotEqual(0.0, drawing.Source.Angle);
        return drawing;
    }
}
