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

    private static double Distance(Vector a, Vector b) => a.DistanceTo(b);
}
