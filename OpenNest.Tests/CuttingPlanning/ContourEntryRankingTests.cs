using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The pure facing/tier/travel ranking of the entry catalogue: deterministic lexicographic
/// order toward a look-ahead target (or toward arrival alone for the last part), with the
/// catalogue itself untouched and entity order never meaningful.
/// </summary>
public class ContourEntryRankingTests
{
    private static IReadOnlyList<ContourEntryCandidate> Catalogue(Program program) =>
        PreparedContours.Capture(program, ExplicitContourTests.Parameters())
            .AutomaticEntryCandidatesWithFallbacks(0);

    // --- fixtures ---------------------------------------------------------------

    /// <summary>Square 0..10 in travel order, optionally cyclically reindexed or reversed.</summary>
    private static Program Square(IEnumerable<Vector> vertices)
    {
        var p = new Program();
        p.MoveTo(vertices.First().X, vertices.First().Y);
        foreach (var v in vertices.Skip(1).Append(vertices.First()))
            p.LineTo(v.X, v.Y);
        return p;
    }

    private static readonly Vector[] SquareVertices =
    {
        new(0, 0), new(10, 0), new(10, 10), new(0, 10),
    };

    private static Program BumpSquare()
    {
        // Half-circle bump on the top edge; tangent joints at (0,10) and (10,10).
        var p = new Program();
        p.MoveTo(0, 0);
        p.LineTo(10, 0);
        p.LineTo(10, 10);
        p.ArcTo(new Vector(0, 10), new Vector(5, 10), RotationType.CCW);
        p.LineTo(0, 0);
        return p;
    }

    private static bool At(ContourEntryCandidate c, double x, double y) =>
        c.Choice.Point.DistanceTo(new Vector(x, y)) < 1e-6;

    private static List<(AutomaticEntryKind Kind, double X, double Y)> Shape(IReadOnlyList<ContourEntryCandidate> ranked) =>
        ranked.Select(c => (c.Kind, System.Math.Round(c.Choice.Point.X, 6), System.Math.Round(c.Choice.Point.Y, 6))).ToList();

    // --- facing target ------------------------------------------------------------

    [Fact]
    public void LowerRightTarget_PrefersTheBottomRightCorner()
    {
        var ranked = Catalogue(Square(SquareVertices)).RankTowardNextCut(new Vector(14, -2));

        // Facing sides right and bottom; the shared corner is the ideal start.
        var first = Assert.Single(ranked.Take(1));
        Assert.Equal(AutomaticEntryKind.ConvexCorner, first.Kind);
        Assert.True(At(first, 10, 0));
        // It precedes every other corner and midpoint.
        Assert.True(ranked.ToList().FindIndex(c => At(c, 10, 0)) < ranked.ToList().FindIndex(c => At(c, 0, 10)));
        Assert.True(ranked.ToList().FindIndex(c => At(c, 10, 0)) < ranked.ToList().FindIndex(c => At(c, 5, 0)));
    }

    [Fact]
    public void FacingMidpoint_BeatsANonFacingCorner()
    {
        // Target due right: only the right side faces it; the left corners face away.
        var ranked = Catalogue(Square(SquareVertices)).RankTowardNextCut(new Vector(14, 5));
        var order = ranked.ToList();

        var rightMid = order.FindIndex(c => c.Kind == AutomaticEntryKind.StraightMidpoint && At(c, 10, 5));
        var farCorner = order.FindIndex(c => c.Kind == AutomaticEntryKind.ConvexCorner && At(c, 0, 10));
        Assert.True(rightMid < farCorner);
    }

    [Fact]
    public void SameFacingClass_CornerBeatsMidpoint()
    {
        var ranked = Catalogue(Square(SquareVertices)).RankTowardNextCut(new Vector(14, -2));
        var order = ranked.ToList();

        var corner = order.FindIndex(c => c.Kind == AutomaticEntryKind.ConvexCorner && At(c, 10, 0));
        var bottomMid = order.FindIndex(c => c.Kind == AutomaticEntryKind.StraightMidpoint && At(c, 5, 0));
        var rightMid = order.FindIndex(c => c.Kind == AutomaticEntryKind.StraightMidpoint && At(c, 10, 5));
        // The shared facing corner beats both facing midpoints; tier breaks the facing tie.
        Assert.True(corner < bottomMid && corner < rightMid);
        // Between the two facing midpoints travel decides: right-mid is nearer the target.
        Assert.True(rightMid < bottomMid);
    }

    [Fact]
    public void TangentAndMidpoint_ShareARankTier()
    {
        Assert.Equal(AutomaticEntryKind.StraightMidpoint.RankTier(), AutomaticEntryKind.TangentJoint.RankTier());
        Assert.True(AutomaticEntryKind.ConvexCorner.RankTier() < AutomaticEntryKind.TangentJoint.RankTier());
        Assert.True(AutomaticEntryKind.TangentJoint.RankTier() < AutomaticEntryKind.NearCorner.RankTier());
        Assert.True(AutomaticEntryKind.NearCorner.RankTier() <= AutomaticEntryKind.TargetFacing.RankTier());
        Assert.Equal(AutomaticEntryKind.NearCorner.RankTier(), AutomaticEntryKind.CircleCompass.RankTier());

        // Facing still dominates tier: a joint on both facing sides outranks a non-facing corner.
        var ranked = Catalogue(BumpSquare()).RankTowardNextCut(new Vector(14, 14));
        var joint = Assert.Single(ranked.Where(c => c.Kind == AutomaticEntryKind.TangentJoint && At(c, 10, 10)));
        Assert.True(ranked.ToList().IndexOf(joint)
            < ranked.ToList().FindIndex(c => c.Kind == AutomaticEntryKind.ConvexCorner && At(c, 0, 0)));
    }

    // --- stability ------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversedOrReindexedDrawing_SameGeometricOrdering(bool reversed)
    {
        // Same square geometry, different entity travel order: entity order must never be
        // the meaningful tie-break — the geometric ordering is identical.
        var vertices = reversed
            ? new[] { SquareVertices[3], SquareVertices[2], SquareVertices[1], SquareVertices[0] }
            : new[] { SquareVertices[1], SquareVertices[2], SquareVertices[3], SquareVertices[0] };
        var target = new Vector(14, -2);

        Assert.Equal(
            Shape(Catalogue(Square(SquareVertices)).RankTowardNextCut(target, new Vector(-1, -1))),
            Shape(Catalogue(Square(vertices)).RankTowardNextCut(target, new Vector(-1, -1))));
    }

    [Fact]
    public void InputCatalogue_IsNotMutated_AndNothingIsIntroduced()
    {
        var catalogue = Catalogue(BumpSquare());
        var before = Shape(catalogue);
        var snapshot = catalogue.ToList();

        var ranked = catalogue.RankTowardNextCut(new Vector(14, -2), new Vector(-1, -1));

        Assert.Equal(before, Shape(snapshot)); // input untouched
        Assert.Equal(catalogue.Count, ranked.Count); // permutation only
        Assert.True(ranked.All(catalogue.Contains));
    }

    [Fact]
    public void Ranking_NeverIntroducesReflexCandidates()
    {
        // L-outline: (5,5) is reflex for its own travel and absent from the catalogue.
        var p = Square(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(5, 10),
            new Vector(5, 5), new Vector(0, 5),
        });

        foreach (var target in new[] { new Vector(14, -2), new Vector(-4, 14), new Vector(14, 14) })
        {
            var ranked = Catalogue(p).RankTowardNextCut(target);
            Assert.DoesNotContain(ranked, c => At(c, 5, 5));
        }
    }

    [Fact]
    public void NonFiniteTarget_IsRejected()
    {
        var catalogue = Catalogue(Square(SquareVertices));
        Assert.Throws<ArgumentException>(() =>
            catalogue.RankTowardNextCut(new Vector(double.NaN, 5)));
    }

    // --- no target (last part) --------------------------------------------------------

    [Fact]
    public void NoTarget_TierFirstThenDistanceToArrival()
    {
        var ranked = Catalogue(Square(SquareVertices)).RankTowardNextCut(arrival: new Vector(5, 10));
        var order = ranked.ToList();

        // Tier first: an outside corner outranks the midpoint the arrival sits on.
        var firstCorner = order.FindIndex(c => c.Kind == AutomaticEntryKind.ConvexCorner && At(c, 0, 10));
        var touchedMid = order.FindIndex(c => c.Kind == AutomaticEntryKind.StraightMidpoint && At(c, 5, 10));
        Assert.True(firstCorner < touchedMid);
        Assert.Equal(AutomaticEntryKind.ConvexCorner, order[0].Kind);
        // Within corners, distance to arrival decides (tie broken by stable key): (0,10) and
        // (10,10) are equidistant from (5,10), so the lower X key wins.
        Assert.True(At(order[0], 0, 10));
    }

    [Theory]
    [InlineData(0.1, 0.1, 0.0, 0.0)]
    [InlineData(9.9, 9.9, 10.0, 10.0)]
    public void NoTarget_StartsNearTheArrival_NotBackAtTheOrigin(double arrivalX, double arrivalY, double expectedX, double expectedY)
    {
        // The last part faces where the head already is; the ranking must not drag it to
        // the plate origin when the arrival is elsewhere.
        var ranked = Catalogue(Square(SquareVertices))
            .RankTowardNextCut(arrival: new Vector(arrivalX, arrivalY));

        Assert.True(At(ranked[0], expectedX, expectedY));
    }

    [Fact]
    public void EmptyCatalogue_RanksToEmpty()
    {
        var empty = new List<ContourEntryCandidate>();
        Assert.Empty(empty.RankTowardNextCut(new Vector(3, 4), new Vector(-1, -1)));
    }
}
