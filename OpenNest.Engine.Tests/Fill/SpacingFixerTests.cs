using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>Fix Spacing: only flagged pairs open, by the least movement, rippling into crowded neighbors.</summary>
public class SpacingFixerTests
{
    private const double Spacing = 0.25;

    private static Plate MakePlate(double lengthX, double widthY, double edge = 0.5)
    {
        var plate = new Plate(new Size(widthY, lengthX)) { PartSpacing = Spacing };
        plate.EdgeSpacing = new Spacing(edge, edge);
        return plate;
    }

    private static Part AddSquare(Plate plate, double x, double y, double size = 4)
    {
        var part = new Part(new Drawing($"sq{plate.Parts.Count}", TestDrawingFactory.Rectangle(size, size)),
            new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static PlateOverlapReport Check(Plate plate) =>
        PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));

    private static void AssertClean(Plate plate)
    {
        var report = Check(plate);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Pairs);
        Assert.Empty(report.SpacingPairs);
        Assert.Empty(report.EdgeFindings);
    }

    [Fact]
    public void CleanPlate_MovesNothing()
    {
        var plate = MakePlate(30, 10);
        var a = AddSquare(plate, 1, 1);
        var b = AddSquare(plate, 1 + 4 + Spacing, 1);

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(0, result.FlaggedPairs);
        Assert.Empty(result.MovedParts);
        Assert.Equal(new Vector(1, 1), a.Location);
        Assert.Equal(1 + 4 + Spacing, b.Location.X);
    }

    [Fact]
    public void TooClosePair_OpensToTheSpacing_AndLeavesOtherPartsAlone()
    {
        var plate = MakePlate(30, 20);
        AddSquare(plate, 1, 1);
        AddSquare(plate, 5.1, 1); // 0.1 apart
        var far = AddSquare(plate, 20, 12);
        var farAt = far.Location;

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(1, result.FlaggedPairs);
        Assert.Equal(0, result.RemainingPairs);
        Assert.Single(result.MovedParts);
        // Just the shortfall, plus the check's slack.
        Assert.InRange(result.LargestMove, 0.15 - 1e-6, 0.152);
        Assert.Equal(farAt, far.Location);
        AssertClean(plate);
    }

    [Fact]
    public void OverlappingPair_IsPushedApart()
    {
        var plate = MakePlate(30, 20);
        AddSquare(plate, 1, 1);
        AddSquare(plate, 4.9, 1); // overlaps by 0.1

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(1, result.FlaggedPairs);
        Assert.Equal(0, result.RemainingPairs);
        Assert.InRange(result.LargestMove, 0.35 - 1e-6, 0.352);
        AssertClean(plate);
    }

    [Fact]
    public void PushRipplesThroughPartsAlreadyAtTheSpacing()
    {
        // The left square sits on the edge, so the fix must push the row to its right, whose
        // squares are already exactly one spacing apart.
        var plate = MakePlate(30, 5.5);
        var anchor = AddSquare(plate, 0.5, 0.5);
        var row = new[]
        {
            AddSquare(plate, 4.6, 0.5), // 0.1 from the anchor
            AddSquare(plate, 4.6 + 4 + Spacing, 0.5),
            AddSquare(plate, 4.6 + 2 * (4 + Spacing), 0.5),
        };
        var rowAt = row.Select(part => part.Location).ToList();

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(0, result.RemainingPairs);
        Assert.Equal(new Vector(0.5, 0.5), anchor.Location);
        for (var i = 0; i < row.Length; i++)
            Assert.InRange(row[i].Location.X - rowAt[i].X, 0.15 - 1e-6, 0.152);
        AssertClean(plate);
    }

    [Fact]
    public void BoxedInPair_SharesTheOpeningBetweenBothSides()
    {
        // Each square has 0.1 to its edge but the pair needs 0.15: neither can go alone.
        var plate = MakePlate(9.3, 5.5);
        var left = AddSquare(plate, 0.6, 0.5);
        var right = AddSquare(plate, 4.7, 0.5);

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(0, result.RemainingPairs);
        Assert.True(left.Location.X < 0.6);
        Assert.True(right.Location.X > 4.7);
        AssertClean(plate);
    }

    [Fact]
    public void BoxedInOverlap_SharesTheWayOut()
    {
        // Overlapping by 0.05, the pair needs 0.3 of separation; each square has 0.2 to its edge.
        var plate = MakePlate(9.35, 5.5);
        var left = AddSquare(plate, 0.7, 0.5);
        var right = AddSquare(plate, 4.65, 0.5);

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(0, result.RemainingPairs);
        Assert.Empty(result.TurnedParts);
        Assert.True(left.Location.X < 0.7);
        Assert.True(right.Location.X > 4.65);
        AssertClean(plate);
    }

    /// <summary>
    /// A bar pinned at its left end by a square on the bottom edge, and a square crowding its
    /// right end against the top edge: no move opens the pair, a slight tilt of the bar does.
    /// </summary>
    private static (Plate Plate, Part Bar, Part Square) PinnedBar()
    {
        var plate = MakePlate(21.5, 7.6);
        plate.Parts.Add(new Part(new Drawing("pin", TestDrawingFactory.Rectangle(4, 1.25)), new Vector(0.5, 0.5)));
        var bar = new Part(new Drawing("bar", TestDrawingFactory.Rectangle(20, 1)), new Vector(0.5, 2.0));
        plate.Parts.Add(bar);
        var square = AddSquare(plate, 16.5, 3.1); // 0.1 above the bar, its top on the top edge
        return (plate, bar, square);
    }

    [Fact]
    public void PinnedPart_TiltsSlightlyWhenNoMoveOpensThePair()
    {
        var (plate, bar, _) = PinnedBar();
        var rotation = bar.Rotation;

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(0, result.RemainingPairs);
        Assert.Contains(bar, result.TurnedParts);
        Assert.InRange(result.LargestTurn, 1e-4, SpacingFixer.DefaultMaxTilt);
        Assert.NotEqual(rotation, bar.Rotation);
        AssertClean(plate);
    }

    [Fact]
    public void ZeroTilt_NeverTurns()
    {
        var (plate, bar, square) = PinnedBar();
        var at = (bar.Location, square.Location, bar.Rotation);

        var result = SpacingFixer.Fix(plate, null, default, maxTilt: 0);

        Assert.Equal(1, result.RemainingPairs);
        Assert.Empty(result.MovedParts);
        Assert.Equal(at, (bar.Location, square.Location, bar.Rotation));
    }

    [Fact]
    public void RestrictedRotation_NeverTurns()
    {
        var (plate, bar, _) = PinnedBar();
        bar.BaseDrawing.Constraints = new NestConstraints { StepAngle = System.Math.PI / 2, EndAngle = System.Math.PI };

        var result = SpacingFixer.Fix(plate);

        Assert.DoesNotContain(bar, result.TurnedParts);
    }

    [Fact]
    public void NoRoom_MovesNothing()
    {
        // Both squares sit on the edges with only 0.1 between them.
        var plate = MakePlate(9.1, 5.5);
        var left = AddSquare(plate, 0.5, 0.5);
        var right = AddSquare(plate, 4.6, 0.5);

        var result = SpacingFixer.Fix(plate);

        Assert.Equal(1, result.FlaggedPairs);
        Assert.Equal(1, result.RemainingPairs);
        Assert.Empty(result.MovedParts);
        Assert.Equal(new Vector(0.5, 0.5), left.Location);
        Assert.Equal(new Vector(4.6, 0.5), right.Location);
    }

    [Fact]
    public void Scope_LimitsTheFixToPairsTouchingIt()
    {
        var plate = MakePlate(30, 20);
        var a = AddSquare(plate, 1, 1);
        var b = AddSquare(plate, 5.1, 1);
        var c = AddSquare(plate, 1, 10);
        var d = AddSquare(plate, 5.1, 10);
        var cAt = c.Location;
        var dAt = d.Location;

        var result = SpacingFixer.Fix(plate, new[] { a });

        Assert.Equal(1, result.FlaggedPairs);
        Assert.Equal(0, result.RemainingPairs);
        Assert.Equal(cAt, c.Location);
        Assert.Equal(dAt, d.Location);
        Assert.Single(Check(plate).SpacingPairs);
    }

    [Fact]
    public void CanceledRun_MovesNothing()
    {
        var plate = MakePlate(30, 20);
        var a = AddSquare(plate, 1, 1);
        var b = AddSquare(plate, 5.1, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = SpacingFixer.Fix(plate, null, cts.Token);

        Assert.True(result.Canceled);
        Assert.Equal(new Vector(1, 1), a.Location);
        Assert.Equal(new Vector(5.1, 1), b.Location);
    }
}
