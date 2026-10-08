using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The uncapped preferred automatic start catalogue: convex corners first, then straight
/// midpoints, then line/arc tangent joints — from the contour's own winding, geometrically
/// deduplicated, never reflex/cusp vertices or collinear splits, and owned immutably.
/// </summary>
public class ContourEntryCandidateTests
{
    private static PreparedContours Capture(Program program) =>
        PreparedContours.Capture(program, ExplicitContourTests.Parameters());

    // --- fixtures ---------------------------------------------------------------

    private static Program ClosedContour(IEnumerable<Vector> vertices)
    {
        var p = new Program();
        p.MoveTo(vertices.First().X, vertices.First().Y);
        foreach (var v in vertices.Skip(1).Append(vertices.First()))
            p.LineTo(v.X, v.Y);
        return p;
    }

    /// <summary>Square perimeter with an L-notch hole slug; its concave vertex (3,3.5) is reflex for the slug's own travel.</summary>
    private static Program NotchedHole()
    {
        var p = ExplicitContourTests.Square(false); // CCW outer square 10x10
        p.MoveTo(2, 2);
        p.LineTo(4, 2); p.LineTo(4, 3.5); p.LineTo(3, 3.5); p.LineTo(3, 4); p.LineTo(2, 4);
        p.LineTo(2, 2); // closing leg
        return p;
    }

    // --- positive catalogue -------------------------------------------------------

    [Fact]
    public void Square_ExposesFourCornersThenFourMidpoints()
    {
        var prepared = Capture(ExplicitContourTests.Square(false));

        var candidates = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal);

        Assert.Equal(8, candidates.Count);
        Assert.Equal(4, candidates.Count(c => c.Kind == AutomaticEntryKind.ConvexCorner));
        Assert.Equal(4, candidates.Count(c => c.Kind == AutomaticEntryKind.StraightMidpoint));
        // Preference order: every corner precedes every midpoint.
        var kinds = candidates.Select(c => c.Kind).ToList();
        Assert.Equal(kinds.OrderBy(k => k).ToList(), kinds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversedWinding_SameCornersAndMidpoints(bool reversed)
    {
        static List<(AutomaticEntryKind Kind, double X, double Y)> Catalogue(Program program) =>
            Capture(program).AutomaticEntryCandidates(0)
                .Select(c => (Kind: c.Kind, X: System.Math.Round(c.Choice.Point.X, 6), Y: System.Math.Round(c.Choice.Point.Y, 6)))
                .OrderBy(x => x.Kind).ThenBy(x => x.X).ThenBy(x => x.Y).ToList();

        var forward = Catalogue(ExplicitContourTests.Square(false));
        Assert.Equal(forward, Catalogue(ExplicitContourTests.Square(reversed)));
        Assert.Equal(8, forward.Count);
    }

    [Fact]
    public void TangentLineArcJoint_IsTangentJoint_NotACorner()
    {
        // A half-circle bump on the top edge of a square: joints at (0,10) and (10,10).
        var p = new Program();
        p.MoveTo(0, 0);
        p.LineTo(10, 0);
        p.LineTo(10, 10);
        p.ArcTo(new Vector(0, 10), new Vector(5, 10), RotationType.CCW);
        p.LineTo(0, 0);
        var prepared = Capture(p);

        var candidates = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal);
        var joints = candidates.Where(c => c.Kind == AutomaticEntryKind.TangentJoint).ToList();

        Assert.Equal(2, joints.Count);
        Assert.Contains(joints, c => Distance(c.Choice.Point, new Vector(10, 10)) < 1e-6);
        Assert.Contains(joints, c => Distance(c.Choice.Point, new Vector(0, 10)) < 1e-6);
        // The joint point is never also reported as a convex corner.
        Assert.DoesNotContain(candidates, c => c.Kind == AutomaticEntryKind.ConvexCorner
            && Distance(c.Choice.Point, new Vector(10, 10)) < 1e-6);
    }

    [Fact]
    public void CollinearSplit_IsNotATangentJoint_ButBothMidpointsRemain()
    {
        // Bottom edge split into two collinear lines at (5,0).
        var p = ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(5, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10),
        });
        var prepared = Capture(p);

        var candidates = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal);

        Assert.DoesNotContain(candidates, c => c.Kind == AutomaticEntryKind.TangentJoint);
        Assert.Equal(4, candidates.Count(c => c.Kind == AutomaticEntryKind.ConvexCorner)); // (10,0),(10,10),(0,10),(0,0)
        Assert.Contains(candidates, c => Distance(c.Choice.Point, new Vector(2.5, 0)) < 1e-6);
        Assert.Contains(candidates, c => Distance(c.Choice.Point, new Vector(7.5, 0)) < 1e-6);
    }

    // --- exclusions ---------------------------------------------------------------

    [Fact]
    public void HoleSlugClassifiesCornersFromItsOwnWinding_NotInverted()
    {
        var prepared = Capture(NotchedHole());
        var hole = 0; // Capture lists holes first; the outer square is the perimeter.
        Assert.NotEqual(prepared.PerimeterOrdinal, hole);

        var candidates = prepared.AutomaticEntryCandidates(hole);

        // The slug's five convex corners come from its own travel; the concave vertex (3,3.5)
        // is reflex for the slug and must never be enumerated for automatic placement.
        Assert.DoesNotContain(candidates, c => Distance(c.Choice.Point, new Vector(3, 3.5)) < 1e-6);
        var corners = candidates.Where(c => c.Kind == AutomaticEntryKind.ConvexCorner).ToList();
        Assert.Contains(corners, c => Distance(c.Choice.Point, new Vector(2, 2)) < 1e-6);
        Assert.Contains(corners, c => Distance(c.Choice.Point, new Vector(4, 2)) < 1e-6);
        Assert.Contains(corners, c => Distance(c.Choice.Point, new Vector(2, 4)) < 1e-6);
        Assert.Equal(5, candidates.Count(c => c.Kind == AutomaticEntryKind.ConvexCorner));
        Assert.Equal(6, candidates.Count(c => c.Kind == AutomaticEntryKind.StraightMidpoint));
    }

    [Fact]
    public void ReflexVertexOfOuterOutline_IsNeverEnumerated()
    {
        // L-shaped outline: (5,5) is reflex for the part's own CCW travel.
        var p = ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(5, 10),
            new Vector(5, 5), new Vector(0, 5),
        });
        var prepared = Capture(p);

        var candidates = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal);

        Assert.DoesNotContain(candidates, c => Distance(c.Choice.Point, new Vector(5, 5)) < 1e-6);
        Assert.Equal(5, candidates.Count(c => c.Kind == AutomaticEntryKind.ConvexCorner));
    }

    [Fact]
    public void Circle_HasNoPreferredCatalogue()
    {
        var p = new Program();
        p.MoveTo(5, 3);
        p.ArcTo(5, 3, 3, 3, RotationType.CCW);
        var prepared = Capture(p);

        Assert.Throws<ArgumentException>(() => prepared.AutomaticEntryCandidates(0));
    }

    // --- ownership / stability ------------------------------------------------------

    [Fact]
    public void ManualEntry_StillAcceptsAReflexPoint()
    {
        // The manual path is unchanged: an explicit reflex vertex is a valid choice.
        var prepared = Capture(ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(5, 10),
            new Vector(5, 5), new Vector(0, 5),
        }));
        var reflex = new Vector(5, 5);
        var choice = prepared.ClosestEntry(prepared.PerimeterOrdinal, reflex + new Vector(0.0001, 0.0001));

        // ClosestEntry lands exactly on the reflex vertex and Entry validates it.
        var manual = prepared.Entry(prepared.PerimeterOrdinal, choice.EntityOrdinal, reflex);
        Assert.Equal(reflex.X, manual.Point.X, 9);
        Assert.Equal(reflex.Y, manual.Point.Y, 9);
    }

    [Fact]
    public void MutatingSourceProgramAfterCapture_CannotChangeTheCatalogue()
    {
        var source = ExplicitContourTests.Square(false);
        var prepared = Capture(source);
        var before = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal)
            .Select(c => (c.Kind, c.Choice.EntityOrdinal, c.Choice.Point.X, c.Choice.Point.Y)).ToList();

        source.Codes.Clear();

        var after = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal)
            .Select(c => (c.Kind, c.Choice.EntityOrdinal, c.Choice.Point.X, c.Choice.Point.Y)).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void CancelledToken_Throws()
    {
        var prepared = Capture(ExplicitContourTests.Square(false));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal, cancelled.Token));
    }

    [Fact]
    public void CatalogueCandidates_BelongToThisPreparation_AndEmit()
    {
        var prepared = Capture(ExplicitContourTests.Square(false));
        var corner = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal)
            .First(c => c.Kind == AutomaticEntryKind.ConvexCorner);

        // The owned choice is accepted by this preparation's emit path...
        var program = prepared.Emit(new[] { corner.Choice });
        Assert.NotEmpty(program.Codes);

        // ...and a foreign preparation rejects it.
        var other = Capture(ExplicitContourTests.Square(false));
        Assert.Throws<ArgumentException>(() => other.Emit(new[] { corner.Choice }));
    }

    [Fact]
    public void GeometryKey_DeduplicatesTheSamePointAcrossEntities()
    {
        // Every convex corner is discovered from the entity ending at it; no two candidates
        // share a geometric point.
        var prepared = Capture(ExplicitContourTests.Square(false));
        var candidates = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal);

        Assert.Equal(candidates.Count, candidates.Select(c => c.GeometryKey).Distinct().Count());
    }

    // --- S04 fallback tier ------------------------------------------------------

    private static IReadOnlyList<ContourEntryCandidate> WithFallbacks(
        Program program, Vector? lookAhead = null, string style = "line") =>
        PreparedContours.Capture(program, ExplicitContourTests.Parameters(style))
            .AutomaticEntryCandidatesWithFallbacks(0, lookAhead);

    [Fact]
    public void Circle_FallbacksAreCompassPointsAndTargetFacing()
    {
        var p = new Program();
        p.MoveTo(5, 3);
        p.ArcTo(5, 3, 3, 3, RotationType.CCW); // end (5,3), center (3,3), radius 2

        // The preferred catalogue still refuses a whole circle...
        Assert.Throws<ArgumentException>(() => Capture(p).AutomaticEntryCandidates(0));

        // ...but the fallback catalogue makes it usable: the eight native compass points.
        var plain = WithFallbacks(p);
        Assert.Equal(8, plain.Count);
        Assert.All(plain, c => Assert.Equal(AutomaticEntryKind.CircleCompass, c.Kind));
        for (var angle = 0; angle < 8; angle++)
        {
            var expected = new Vector(3, 3) +
                new Vector(System.Math.Cos(angle * System.Math.PI / 4), System.Math.Sin(angle * System.Math.PI / 4)) * 2;
            Assert.Contains(plain, c => Distance(c.Choice.Point, expected) < 1e-9);
        }

        // With a look-ahead the exact target-facing closest point joins the set. The
        // look-ahead is off-compass so the merge cannot deduplicate it away.
        var ahead = new Vector(4, -1);
        var facing = WithFallbacks(p, ahead);
        Assert.Equal(9, facing.Count);
        var target = Assert.Single(facing, c => c.Kind == AutomaticEntryKind.TargetFacing);
        var facingPoint = new Vector(3, 3) + (ahead - new Vector(3, 3)).Normalize() * 2;
        Assert.Equal(facingPoint.X, target.Choice.Point.X, 6);
        Assert.Equal(facingPoint.Y, target.Choice.Point.Y, 6);
    }

    [Fact]
    public void FilletArcMidpoint_IsTier3_KeepsJointPreferred()
    {
        // Half-circle bump on the top edge: joints (0,10),(10,10); arc midpoint (5,15).
        var p = new Program();
        p.MoveTo(0, 0);
        p.LineTo(10, 0);
        p.LineTo(10, 10);
        p.ArcTo(new Vector(0, 10), new Vector(5, 10), RotationType.CCW);
        p.LineTo(0, 0);

        // The preferred tier has no arc midpoint at all.
        Assert.DoesNotContain(Capture(p).AutomaticEntryCandidates(0),
            c => c.Kind == AutomaticEntryKind.ArcMidpoint);

        var withFallbacks = WithFallbacks(p);
        var mid = Assert.Single(withFallbacks, c => c.Kind == AutomaticEntryKind.ArcMidpoint);
        Assert.Equal(5, mid.Choice.Point.X, 9);
        Assert.Equal(15, mid.Choice.Point.Y, 9);
        // Tier 3: strictly after every preferred kind in catalogue order.
        var lastPreferred = withFallbacks.Select(c => c.Kind).ToList()
            .FindLastIndex(k => k <= AutomaticEntryKind.TangentJoint);
        var midpointIndex = withFallbacks.ToList().IndexOf(mid);
        Assert.True(midpointIndex > lastPreferred);
    }

    [Fact]
    public void LongEdge_GetsNearCornerPointsAtTwoTimesLeadIn()
    {
        // 10x10 square, default LineLeadIn Length 0.3 -> inset 0.6 on each incident edge.
        var candidates = WithFallbacks(ExplicitContourTests.Square(false));
        var near = candidates.Where(c => c.Kind == AutomaticEntryKind.NearCorner).ToList();

        Assert.Equal(8, near.Count); // 4 convex corners x 2 incident straight edges
        Assert.Contains(near, c => Distance(c.Choice.Point, new Vector(0.6, 0)) < 1e-9);
        Assert.Contains(near, c => Distance(c.Choice.Point, new Vector(9.4, 0)) < 1e-9);
        Assert.Contains(near, c => Distance(c.Choice.Point, new Vector(10, 9.4)) < 1e-9);
        Assert.All(near, c => Assert.True(c.Kind > AutomaticEntryKind.TangentJoint));
    }

    [Fact]
    public void ShortEdge_OmitsNearCornerInsteadOfExtrapolating()
    {
        // 10 x 0.4 rectangle: every convex corner's vertical edge (0.4) is shorter than
        // 2 x lead-in (0.6), so only the horizontal edges carry near-corner points.
        var p = ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 0.4), new Vector(0, 0.4),
        });
        var near = WithFallbacks(p).Where(c => c.Kind == AutomaticEntryKind.NearCorner).ToList();

        Assert.Equal(4, near.Count);
        // Every point stays strictly inside a horizontal edge of the rectangle — never
        // extrapolated onto a vertical edge or past an endpoint.
        Assert.All(near, c =>
        {
            var onBottom = Distance(c.Choice.Point, new Vector(c.Choice.Point.X, 0)) < 1e-9
                && c.Choice.Point.X > 0 && c.Choice.Point.X < 10;
            var onTop = Distance(c.Choice.Point, new Vector(c.Choice.Point.X, 0.4)) < 1e-9
                && c.Choice.Point.X > 0 && c.Choice.Point.X < 10;
            Assert.True(onBottom || onTop);
        });
        Assert.Contains(near, c => Distance(c.Choice.Point, new Vector(0.6, 0)) < 1e-9);
        Assert.Contains(near, c => Distance(c.Choice.Point, new Vector(9.4, 0.4)) < 1e-9);
    }

    [Fact]
    public void InsetExactlyTwoLeadInsFromAReflexCorner_IsOmitted()
    {
        // Boundary notch 0.6 deep — exactly 2 x lead-in. The inset from each convex
        // opening corner lands exactly on the reflex inner corner, so neither inner
        // corner may appear in the automatic catalogue at all.
        var p = ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10),
            new Vector(0, 4.6), new Vector(0.6, 4.6), new Vector(0.6, 4), new Vector(0, 4),
        });
        var candidates = WithFallbacks(p);

        Assert.DoesNotContain(candidates, c => Distance(c.Choice.Point, new Vector(0.6, 4.6)) < 1e-9);
        Assert.DoesNotContain(candidates, c => Distance(c.Choice.Point, new Vector(0.6, 4)) < 1e-9);
    }

    [Fact]
    public void TargetExactlyAtReflexVertex_IsNotAnAutomaticStart()
    {
        // L-outline; (5,5) is reflex for its own travel. The raw closest point from that
        // position lands exactly on the reflex vertex and must be dropped.
        var p = ClosedContour(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(5, 10),
            new Vector(5, 5), new Vector(0, 5),
        });

        var reflex = new Vector(5, 5);
        var candidates = WithFallbacks(p, reflex);

        // The forbidden closest point is dropped entirely...
        Assert.DoesNotContain(candidates, c => Distance(c.Choice.Point, reflex) < 1e-9);
        Assert.DoesNotContain(candidates, c => c.Kind == AutomaticEntryKind.TargetFacing);
        // ...and the catalogue stays usable through the other fallbacks.
        Assert.Contains(candidates, c => c.Kind == AutomaticEntryKind.NearCorner);
    }

    [Fact]
    public void TargetFacingAtSharedPoint_KeepsThePreferredCorner()
    {
        // Look-ahead straight at corner (10,10): closest point IS the convex corner, so
        // the geometric merge keeps the more preferred kind at that single point.
        var candidates = WithFallbacks(ExplicitContourTests.Square(false), new Vector(10, 10));

        var atCorner = candidates.Where(c => Distance(c.Choice.Point, new Vector(10, 10)) < 1e-9).ToList();
        var corner = Assert.Single(atCorner);
        Assert.Equal(AutomaticEntryKind.ConvexCorner, corner.Kind);
    }

    [Fact]
    public void FallbackCatalogue_IsNonEmptyForEveryShape_AndHonoursCancellation()
    {
        // All-rounded contour (bump square) stays usable; cancellation is honoured.
        var bump = new Program();
        bump.MoveTo(0, 0);
        bump.LineTo(10, 0);
        bump.LineTo(10, 10);
        bump.ArcTo(new Vector(0, 10), new Vector(5, 10), RotationType.CCW);
        bump.LineTo(0, 0);
        Assert.NotEmpty(WithFallbacks(bump));

        var prepared = PreparedContours.Capture(ExplicitContourTests.Square(false), ExplicitContourTests.Parameters());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            prepared.AutomaticEntryCandidatesWithFallbacks(0, new Vector(1, 1), cancelled.Token));
    }

    [Theory]
    [InlineData("arc")]
    [InlineData("linearc")]
    [InlineData("clean")]
    public void NonLengthLeadIns_OmitNearCornerInsteadOfApproximating(string style)
    {
        // Only lead-in styles with a straight length feed the inset; others contribute 0
        // and the near-corner fallback is omitted entirely — no invented setting.
        var candidates = WithFallbacks(ExplicitContourTests.Square(false), style: style);
        Assert.DoesNotContain(candidates, c => c.Kind == AutomaticEntryKind.NearCorner);
    }

    private static double Distance(Vector a, Vector b) => a.DistanceTo(b);
}
