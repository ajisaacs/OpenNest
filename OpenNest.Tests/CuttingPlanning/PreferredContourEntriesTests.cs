using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The preferred hole-entry proposal: walk the proposed hole route BACKWARD from the chosen
/// perimeter entry, each hole facing the downstream contour's ACTUAL emitted pierce (native
/// rounding included), corners outranking midpoints, with an explicit no-preference result
/// when a hole cannot flow. Proposal only — nothing installed, nothing certified here.
/// (Contour layout: holes take ordinals 0..N-1, the perimeter is the last ordinal.)
/// </summary>
public class PreferredContourEntriesTests
{
    private static readonly Vector Arrival = new(0, 5);

    private static Program CirclesProgram(params (double X, double Y)[] centres)
    {
        var p = ExplicitContourTests.Square(false);
        foreach (var (x, y) in centres)
        {
            // Unit circle centred at (x, y): two half-arcs, CCW.
            p.MoveTo(x + 1, y);
            p.ArcTo(new Vector(x - 1, y), new Vector(x, y), RotationType.CCW);
            p.ArcTo(new Vector(x + 1, y), new Vector(x, y), RotationType.CCW);
        }
        return p;
    }

    private static Program RectHolesProgram(params (double X1, double Y1, double X2, double Y2)[] boxes)
    {
        var p = ExplicitContourTests.Square(false);
        foreach (var (x1, y1, x2, y2) in boxes)
        {
            p.MoveTo(x1, y1);
            p.LineTo(x2, y1); p.LineTo(x2, y2); p.LineTo(x1, y2); p.LineTo(x1, y1);
        }
        return p;
    }

    private static (PreparedContours Prepared, ContourEntryFeasibility Feasibility) Capture(Program clean,
        Vector location)
    {
        var prepared = PreparedContours.Capture(clean, ExplicitContourTests.Parameters());
        var material = LeadMaterialSnapshot.Capture(clean, location);
        var feasibility = new ContourEntryFeasibility(prepared, location, material, []);
        return (prepared, feasibility);
    }

    private static bool At(Vector point, double x, double y) =>
        point.DistanceTo(new Vector(x, y)) < 1e-6;

    private static Vector Pierce(PreparedContours prepared, ContourChoice choice)
    {
        var program = prepared.EmitCandidateForValidation(choice);
        var execution = ExecutionMotionReader.Read(program, Vector.Zero, null, default);
        var first = execution.Motions.First(m => !m.Rapid);
        return first.Start ?? first.End;
    }

    /// <summary>The owned compass candidate of a circle contour at an exact point.</summary>
    private static ContourChoice Compass(PreparedContours prepared, int contour, double x, double y,
        Vector lookAhead)
    {
        var match = prepared.AutomaticEntryCandidatesWithFallbacks(contour, lookAhead)
            .FirstOrDefault(c => At(c.Choice.Point, x, y));
        Assert.NotNull(match);
        return match.Choice;
    }

    [Fact]
    public void TwoCircularHoles_FlowToTheSelectedOutsideEntry()
    {
        var clean = CirclesProgram((3, 3), (7, 3));
        var (prepared, feasibility) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5)); // outside right edge
        Assert.True(At(perimeterEntry.Point, 10, 5));

        // Route: arrival (0,5) -> left circle -> right circle -> the outside entry.
        var centres = new[] { new Vector(3, 3), new Vector(7, 3), new Vector(5, 5) };
        var route = CuttingHoleOrder.Plan(new[] { 0, 1 }, centres, Arrival,
            Pierce(prepared, perimeterEntry));
        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, route,
            centres, Arrival, c => feasibility.Check(c.Choice));

        Assert.True(proposal.IsPreferred, proposal.Reason);
        Assert.Equal(new[] { 0, 1 }, proposal.HoleChoices.Select(c => c.ContourOrdinal));

        // The last hole's ACTUAL pierce flows to the perimeter pierce: strictly closer to
        // it than the opposite (west) compass pierce would be.
        var perimeterPierce = Pierce(prepared, perimeterEntry);
        var last = Pierce(prepared, proposal.HoleChoices[1]);
        var opposite = Pierce(prepared, Compass(prepared, 1, 6, 3, new Vector(6, 3)));
        Assert.True(last.DistanceTo(perimeterPierce) < opposite.DistanceTo(perimeterPierce),
            $"right hole pierce {last} does not flow to {perimeterPierce} (west pierce {opposite})");

        // The first hole faces the SECOND hole's actual pierce — not the perimeter and not
        // the downstream nominal point: closer to it than the west compass pierce would be.
        var downstream = Pierce(prepared, proposal.HoleChoices[1]);
        var first = Pierce(prepared, proposal.HoleChoices[0]);
        var firstWest = Pierce(prepared, Compass(prepared, 0, 2, 3, new Vector(2, 3)));
        Assert.True(first.DistanceTo(downstream) < firstWest.DistanceTo(downstream),
            $"left hole pierce {first} faces away from downstream {downstream} (west {firstWest})");
    }

    [Fact]
    public void RectangularHoles_CornersOutrankMidpoints()
    {
        var clean = RectHolesProgram((3, 3, 5, 5), (7, 3, 9, 5));
        var (prepared, _) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 4));
        var centres = new[] { new Vector(4, 4), new Vector(8, 4), new Vector(5, 5) };

        // All-clear evaluation isolates pure ranking: a corner on a facing side wins over
        // any midpoint, walking backward from the perimeter entry.
        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, new[] { 0, 1 },
            centres, Arrival, _ => new(ContourFeasibilityStatus.Clear, null));

        Assert.True(proposal.IsPreferred, proposal.Reason);
        // Right hole faces the perimeter: a RIGHT-edge corner of the (7,3)-(9,5) hole,
        // never the (9,4) midpoint nor a left-edge corner.
        var right = proposal.HoleChoices[1].Point;
        Assert.True(At(right, 9, 3) || At(right, 9, 5),
            $"right hole entry {right} is not a right-facing corner");
        // Left hole faces the right hole's actual pierce (eastward): a right-edge corner of
        // the (3,3)-(5,5) hole, never its (5,4) midpoint.
        var left = proposal.HoleChoices[0].Point;
        Assert.True(At(left, 5, 3) || At(left, 5, 5),
            $"left hole entry {left} is not a right-facing corner");
    }

    [Fact]
    public void BlockedIdealLead_TakesTheNextFeasibleCandidate()
    {
        var clean = CirclesProgram((3, 3), (7, 3));
        var (prepared, feasibility) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5));
        var centres = new[] { new Vector(3, 3), new Vector(7, 3), new Vector(5, 5) };

        // The ideal east compass point of the right circle is refused: fall back to its
        // next-ranked candidate rather than fail or skip the hole.
        ContourFeasibilityVerdict Evaluate(ContourEntryCandidate c) =>
            c.Choice.ContourOrdinal == 1 && At(c.Choice.Point, 8, 3)
                ? new(ContourFeasibilityStatus.Blocked, "test block")
                : feasibility.Check(c.Choice);
        var route = CuttingHoleOrder.Plan(new[] { 0, 1 }, centres, Arrival, Pierce(prepared, perimeterEntry));
        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, route, centres, Arrival, Evaluate);

        Assert.True(proposal.IsPreferred, proposal.Reason);
        Assert.Equal(1, proposal.HoleChoices[1].ContourOrdinal); // never skipped or swapped
        Assert.False(At(proposal.HoleChoices[1].Point, 8, 3), "the blocked candidate was used anyway");
        Assert.True(proposal.HoleChoices[1].Point.X >= 7.0 - 1e-9,
            $"fallback entry {proposal.HoleChoices[1].Point} abandoned the facing side entirely");
    }

    [Fact]
    public void BlockedHole_GivesAnExplicitNoPreferenceFinding()
    {
        var clean = CirclesProgram((3, 3), (7, 3));
        var (prepared, _) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5));
        var centres = new[] { new Vector(3, 3), new Vector(7, 3), new Vector(5, 5) };

        ContourFeasibilityVerdict Evaluate(ContourEntryCandidate c) =>
            c.Choice.ContourOrdinal == 0
                ? new(ContourFeasibilityStatus.Blocked, "nothing fits")
                : new(ContourFeasibilityStatus.Clear, null);
        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, new[] { 0, 1 },
            centres, Arrival, Evaluate);

        Assert.False(proposal.IsPreferred);
        Assert.Empty(proposal.HoleChoices);
        Assert.Equal(0, proposal.BlockedContour);
        Assert.Contains("no preferred lead-feasible entry", proposal.Reason);
    }

    [Fact]
    public void EveryHoleExactlyOnceInRouteOrder()
    {
        var clean = CirclesProgram((2.5, 3), (5, 3), (7.5, 3));
        var (prepared, feasibility) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(3, 2, new Vector(10, 5)); // perimeter is last
        var centres = new[]
        {
            new Vector(2.5, 3), new Vector(5, 3), new Vector(7.5, 3), new Vector(5, 5),
        };
        var route = CuttingHoleOrder.Plan(new[] { 0, 1, 2 }, centres, Arrival, Pierce(prepared, perimeterEntry));

        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, route, centres, Arrival,
            c => feasibility.Check(c.Choice));

        Assert.True(proposal.IsPreferred, proposal.Reason);
        Assert.Equal(route, proposal.HoleChoices.Select(c => c.ContourOrdinal));
        Assert.Equal(route.Count, proposal.HoleChoices.Select(c => c.ContourOrdinal).Distinct().Count());
        Assert.All(proposal.HoleChoices, c => Assert.NotEqual(3, c.ContourOrdinal)); // never the perimeter
    }

    [Fact]
    public void ChainingIsDeterministic()
    {
        // Whatever native rounding a nominal circle candidate emits, backward chaining must
        // reproduce the same earlier-hole choice on a second run: the chain is a pure
        // function of the ACTUAL pierce, not of iteration order or object identity.
        var clean = CirclesProgram((3, 3), (7.4, 4.6));
        var (prepared, feasibility) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5));
        var centres = new[] { new Vector(3, 3), new Vector(7.4, 4.6), new Vector(5, 5) };

        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, new[] { 0, 1 },
            centres, Arrival, c => feasibility.Check(c.Choice));
        var again = PreferredContourEntries.TryPlan(prepared, perimeterEntry, new[] { 0, 1 },
            centres, Arrival, c => feasibility.Check(c.Choice));

        Assert.True(proposal.IsPreferred, proposal.Reason);
        Assert.Equal(proposal.HoleChoices.Select(c => c.Point), again.HoleChoices.Select(c => c.Point));
    }

    [Fact]
    public void ForeignPerimeterChoiceIsRefused()
    {
        var (prepared, feasibility) = Capture(CirclesProgram((3, 3), (7, 3)), Vector.Zero);
        var (other, _) = Capture(ExplicitContourTests.Square(false), Vector.Zero);
        var foreign = other.Entry(0, 0, new Vector(0, 5));

        Assert.Throws<ArgumentException>(() => PreferredContourEntries.TryPlan(prepared, foreign,
            new[] { 0 }, new[] { new Vector(3, 3), new Vector(7, 3) }, Arrival,
            c => feasibility.Check(c.Choice)));
    }

    [Fact]
    public void CancellationPropagates()
    {
        var (prepared, feasibility) = Capture(CirclesProgram((3, 3), (7, 3)), Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => PreferredContourEntries.TryPlan(
            prepared, perimeterEntry, new[] { 0, 1 },
            new[] { new Vector(3, 3), new Vector(7, 3), new Vector(5, 5) }, Arrival,
            c => feasibility.Check(c.Choice), cancelled.Token));
    }

    [Fact]
    public void SourceProgramAndChoicesAreUntouched()
    {
        var clean = CirclesProgram((3, 3), (7, 3));
        var before = ExplicitContourTests.Fingerprint(clean);
        var (prepared, feasibility) = Capture(clean, Vector.Zero);
        var perimeterEntry = prepared.Entry(2, 2, new Vector(10, 5));
        var centres = new[] { new Vector(3, 3), new Vector(7, 3), new Vector(5, 5) };

        var proposal = PreferredContourEntries.TryPlan(prepared, perimeterEntry, new[] { 0, 1 },
            centres, Arrival, c => feasibility.Check(c.Choice));

        Assert.True(proposal.IsPreferred, proposal.Reason);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(clean)); // caller program untouched
        Assert.All(proposal.HoleChoices, c => Assert.True(ReferenceEquals(c.Owner, prepared)));
        Assert.NotSame(prepared.EmitCandidateForValidation(proposal.HoleChoices[0]),
            prepared.EmitCandidateForValidation(proposal.HoleChoices[0])); // fresh programs
    }
}
