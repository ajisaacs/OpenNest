using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

// A 0.277 in mounting hole imported from PEP (a panel door): two half circles whose stored
// centre sits 5e-8 beside the chord's midpoint. The closest entry to an approach from straight
// below lands on the hole's lowest point, 5e-8 along the first arc from its start. Emission
// splits the contour there with the 1e-5 endpoint tolerance, so the contour starts at the vertex;
// the entry must start there too, or the 5e-8 between them is never cut and the plan's own
// verification refuses it.
public class ImportedArcHoleTests
{
    private const double Offset = 4.94e-8;
    private static readonly Vector Bottom = new(1, 0.5);
    private static readonly Vector Top = new(1, 0.777);

    internal static CuttingParameters SweepParameters()
    {
        var lead = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        return new CuttingParameters
        {
            ExternalLeadIn = lead,
            InternalLeadIn = lead,
            ArcCircleLeadIn = lead,
            ExternalLeadOut = new NoLeadOut(),
            InternalLeadOut = new NoLeadOut(),
            ArcCircleLeadOut = new NoLeadOut(),
            PierceClearance = 0.0625
        };
    }

    private static Program PartWithHole(double offset, bool holeBelow = false)
    {
        var p = new Program();
        p.MoveTo(0, 0); p.LineTo(2, 0); p.LineTo(2, 2); p.LineTo(0, 2); p.LineTo(0, 0);
        if (holeBelow)
        {
            // A smaller hole straight below, cut first, so the mounting hole is approached from below.
            p.MoveTo(1 + offset, 0.1);
            p.ArcTo(new Vector(1 + offset, 0.3), new Vector(1 + offset, 0.2), RotationType.CCW);
            p.ArcTo(new Vector(1 + offset, 0.1), new Vector(1 + offset, 0.2), RotationType.CCW);
        }
        var centre = new Vector(1 + offset, (Bottom.Y + Top.Y) / 2);
        p.MoveTo(Bottom.X, Bottom.Y);
        p.ArcTo(Top, centre, RotationType.CCW);
        p.ArcTo(Bottom, centre, RotationType.CCW);
        return p;
    }

    [Theory]
    [InlineData(Offset)]
    [InlineData(-Offset)]
    public void EntryWithinTheSplitToleranceOfAVertex_StartsExactlyAtTheVertex(double offset)
    {
        var prepared = PreparedContours.Capture(PartWithHole(offset), SweepParameters());
        var hole = Enumerable.Range(0, prepared.Count).Single(i => i != prepared.PerimeterOrdinal);

        var choice = prepared.ClosestEntry(hole, new Vector(1 + offset, 0.2));

        Assert.Equal(Bottom.X, choice.Point.X, 12);
        Assert.Equal(Bottom.Y, choice.Point.Y, 12);
    }

    // The same holds for the other entries the planner does not pick from vertices: a drawing pin
    // and a perimeter pierce placed in scrap whose closest contour point lies beside a corner.
    [Fact]
    public void PinBesideACorner_StartsExactlyAtTheCorner()
    {
        var drawing = PinnedContourTests.SquareWithPin(new Vector(3e-6, 0), PinnedContourTests.Corner());
        var plain = PreparedContours.Capture(drawing.Program, PinnedContourTests.NestSettings());
        var prepared = PreparedContours.Capture(drawing.Program, PinnedContourTests.NestSettings(),
            CutPinResolver.Resolve(drawing, 0, plain));

        var pinned = Assert.IsType<ContourChoice>(prepared.PinnedEntry(prepared.PerimeterOrdinal));

        Assert.Equal(0, pinned.Point.DistanceTo(Vector.Zero), 12);
    }

    [Fact]
    public void ScrapPierceBesideACorner_EntersExactlyAtTheCorner()
    {
        var prepared = PreparedContours.Capture(ExplicitContourTests.Square(false), PinnedContourTests.NestSettings());

        var choice = prepared.ScrapPierceEntry(new Vector(-0.5, 3e-6));

        Assert.Equal(0, choice.Point.DistanceTo(Vector.Zero), 12);
    }

    // Snapping must not hand automatic selection a forbidden vertex: a near-corner inset on an edge
    // only just longer than twice the lead-in stays clear of the reflex corner at its far end.
    [Fact]
    public void NearCornerInset_NeverSnapsOntoAReflexVertex()
    {
        var reflex = new Vector(1.699995, 1);
        var p = new Program();
        p.MoveTo(0, 0); p.LineTo(2, 0); p.LineTo(2, 1); p.LineTo(reflex.X, reflex.Y);
        p.LineTo(reflex.X, 2); p.LineTo(0, 2); p.LineTo(0, 0);
        var prepared = PreparedContours.Capture(p, SweepParameters());

        var candidates = prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal);

        Assert.DoesNotContain(candidates, c => c.Choice.Point.DistanceTo(reflex) <= 1e-9);
    }

    // De-duplication compares the entries as emitted (snapped), so an edge shorter than the snap
    // distance does not offer its start twice.
    [Fact]
    public void Entries_OfferEachSnappedPointOnce()
    {
        var p = new Program();
        p.MoveTo(0, 0); p.LineTo(1, 0); p.LineTo(1, 1); p.LineTo(0, 1); p.LineTo(0, 5e-6); p.LineTo(0, 0);
        var prepared = PreparedContours.Capture(p, SweepParameters());

        var points = prepared.Entries(prepared.PerimeterOrdinal, new Vector(-1, -1)).Select(e => e.Point).ToList();

        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                Assert.True(points[i].DistanceTo(points[j]) > 1e-8, $"{points[i]} offered twice");
    }

    // An entry already within the verifier's epsilon of a vertex was cut whole before; it is left
    // exactly where it was, so saved pinned programs re-emit unchanged.
    [Fact]
    public void EntryWithinVerifierEpsilonOfAVertex_IsUnchanged()
    {
        var prepared = PreparedContours.Capture(ExplicitContourTests.Square(false), PinnedContourTests.NestSettings());
        var point = new Vector(-0.5, 5e-9);

        var choice = prepared.ScrapPierceEntry(point);

        Assert.Equal(5e-9, choice.Point.Y, 15);
    }

    // The emitted hole is whole: its cut, from the lead-in's end, covers the drawn contour's full length.
    [Theory]
    [InlineData(Offset)]
    [InlineData(-Offset)]
    public void EntryNearAVertex_EmitsTheWholeHole(double offset)
    {
        var prepared = PreparedContours.Capture(PartWithHole(offset), SweepParameters());
        var hole = Enumerable.Range(0, prepared.Count).Single(i => i != prepared.PerimeterOrdinal);
        var choice = prepared.ClosestEntry(hole, new Vector(1 + offset, 0.2));

        var motions = ExecutionMotionReader.Read(prepared.EmitCandidateForValidation(choice), Vector.Zero, null, default).Motions;

        // Emission marks contour motion Display; replay counts it as cut, as the verifier does.
        var cuts = motions.Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).ToList();
        var lead = motions.Last(m => m.Layer == LayerType.Leadin);
        var nominal = OpenNest.Converters.ConvertProgram.ToGeometry(PartWithHole(offset))
            .OfType<Arc>().Sum(a => a.Length);
        Assert.Equal(nominal, cuts.Sum(m => m.Length), 12);
        Assert.Equal(0, lead.End.DistanceTo(cuts[0].Curve.Start), 12);
        Assert.Equal(0, cuts[^1].End.DistanceTo(cuts[0].Curve.Start), 12);
    }

    [Theory]
    [InlineData(Offset)]
    [InlineData(-Offset)]
    [InlineData(0)]
    public void ImportedMountingHole_PlansAndApplies(double offset)
    {
        foreach (var below in new[] { false, true })
            PlansAndApplies(PartWithHole(offset, below));
    }

    private static void PlansAndApplies(Program program)
    {
        var plate = new Plate(new Size(10, 10));
        plate.Parts.Add(new Part(new Drawing("hole", program), new Vector(1, 1)));

        var proposal = CuttingPlanBatch.Capture([plate], SweepParameters(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: false).Status);
    }
}
