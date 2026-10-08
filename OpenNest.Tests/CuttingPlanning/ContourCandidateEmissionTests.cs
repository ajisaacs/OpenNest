using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The single-contour diagnostic emitter: identical emitted lead groups in isolation and
/// inside a valid complete program (holes first), with the perimeter keeping its External
/// identity — and the normal perimeter-last gate left untouched.
/// </summary>
public class ContourCandidateEmissionTests
{
    private static PreparedContours Capture(Program program, CuttingParameters? parameters = null) =>
        PreparedContours.Capture(program, parameters ?? ExplicitContourTests.Parameters());

    // --- fixtures ---------------------------------------------------------------

    /// <summary>Square perimeter with two circular holes; Capture lists holes 0-1, perimeter 2.</summary>
    private static Program TwoHoles(bool reversed = false)
    {
        var p = ExplicitContourTests.Square(reversed);
        foreach (var center in new[] { new Vector(3, 3), new Vector(7, 7) })
        {
            p.MoveTo(center.X + 0.5, center.Y);
            p.ArcTo(center.X + 0.5, center.Y, center.X, center.Y, RotationType.CCW);
        }
        return p;
    }

    private static Program WithScribe(Program p)
    {
        p.MoveTo(1, 1);
        p.Codes.Add(new LinearMove(new Vector(2, 1)) { Layer = LayerType.Scribe });
        return p;
    }

    private static ContourChoice PerimeterCorner(PreparedContours prepared)
    {
        var corner = prepared.AutomaticEntryCandidates(prepared.PerimeterOrdinal)
            .First(c => c.Kind == AutomaticEntryKind.ConvexCorner).Choice;
        return corner;
    }

    private static Program CompleteWithHolesFirst(PreparedContours prepared, ContourChoice perimeterChoice) =>
        prepared.Emit(new[]
        {
            prepared.ClosestEntry(0, new Vector(3, 3)),
            prepared.ClosestEntry(1, new Vector(7, 7)),
            perimeterChoice,
        });

    // --- differential fidelity ------------------------------------------------------

    [Fact]
    public void PerimeterInIsolation_MatchesCompleteProgramWithHolesFirst()
    {
        // Distinct External/Internal leads: if isolation misclassified the perimeter as a
        // hole, its emitted lead geometry would differ from the complete program's.
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadIn = new LineLeadIn { Length = 1.5, ApproachAngle = 45 };
        parameters.InternalLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 };
        var prepared = Capture(TwoHoles(), parameters);
        var corner = PerimeterCorner(prepared);

        var isolated = prepared.EmitCandidateForValidation(corner);
        var complete = CompleteWithHolesFirst(prepared, corner);

        // The perimeter is the LAST contour of the complete program and alone in the probe.
        AssertIsolatedMatchesTrailing(isolated, complete);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("arc")]
    [InlineData("linearc")]
    [InlineData("lineline")]
    [InlineData("clean")]
    public void EveryLeadStyle_StillIsolatedEqualComplete(string style)
    {
        var parameters = ExplicitContourTests.Parameters(style);
        var prepared = Capture(TwoHoles(), parameters);
        var corner = PerimeterCorner(prepared);

        var isolated = prepared.EmitCandidateForValidation(corner);
        var complete = CompleteWithHolesFirst(prepared, corner);
        AssertIsolatedMatchesTrailing(isolated, complete);
    }

    [Fact]
    public void HoleInIsolation_MatchesItsSliceOfTheCompleteProgram()
    {
        var prepared = Capture(TwoHoles());
        var hole = prepared.ClosestEntry(0, new Vector(3, 3));

        var isolated = prepared.EmitCandidateForValidation(hole);
        var complete = CompleteWithHolesFirst(prepared, PerimeterCorner(prepared));
        Assert.Equal(BlockHashes(isolated).Single(), BlockHashes(complete).First());
    }

    [Fact]
    public void EarlierHoleChoices_DoNotChangePerimeterEmission()
    {
        // Two different hole-entry choices/orders: the perimeter block is invariant.
        var prepared = Capture(TwoHoles());
        var corner = PerimeterCorner(prepared);

        var orderA = prepared.Emit(new[]
        {
            prepared.ClosestEntry(0, new Vector(3, 3)),
            prepared.ClosestEntry(1, new Vector(7, 7)),
            corner,
        });
        var orderB = prepared.Emit(new[]
        {
            prepared.ClosestEntry(1, new Vector(7, 7.5)),
            prepared.ClosestEntry(0, new Vector(3, 3.5)),
            corner,
        });
        var isolated = prepared.EmitCandidateForValidation(corner);

        Assert.Equal(BlockHashes(orderA)[^1..], BlockHashes(orderB)[^1..]);
        AssertIsolatedMatchesTrailing(isolated, orderA);
    }

    [Fact]
    public void Scribes_AppearOnceInIsolation()
    {
        var prepared = Capture(WithScribe(TwoHoles()));
        var corner = PerimeterCorner(prepared);

        var isolated = prepared.EmitCandidateForValidation(corner);
        var scribes = NonRapid(isolated).Where(m => m.Layer == LayerType.Scribe).ToList();
        Assert.Single(scribes);
    }

    [Fact]
    public void ReversedWinding_IsolatedMatchesComplete()
    {
        var prepared = Capture(TwoHoles(reversed: true));
        var corner = PerimeterCorner(prepared);

        var isolated = prepared.EmitCandidateForValidation(corner);
        var complete = CompleteWithHolesFirst(prepared, corner);
        AssertIsolatedMatchesTrailing(isolated, complete);
    }

    [Fact]
    public void RotatedCapture_IsolatedMatchesComplete()
    {
        var source = TwoHoles();
        source.Rotate(System.Math.PI / 7);
        var prepared = Capture(source);
        var corner = PerimeterCorner(prepared);

        var isolated = prepared.EmitCandidateForValidation(corner);
        var complete = prepared.Emit(new[]
        {
            prepared.ClosestEntry(0, new Vector(3, 3)),
            prepared.ClosestEntry(1, new Vector(7, 7)),
            corner,
        });
        AssertIsolatedMatchesTrailing(isolated, complete);
    }

    [Fact]
    public void CircleRoundingAndClamping_AreNotSharedState()
    {
        // Two circles at different normals with 90-degree rounding and clamping: the seam
        // must not leak rounded/clamped state between contours.
        var parameters = ExplicitContourTests.Parameters();
        parameters.RoundLeadInAngles = true;
        parameters.LeadInAngleIncrement = 90;
        parameters.ArcCircleLeadIn = new LineLeadIn { Length = 2 };
        var p = ExplicitContourTests.Square(false);
        foreach (var center in new[] { new Vector(3, 3), new Vector(7, 7) })
        {
            p.MoveTo(center.X + 0.5, center.Y);
            p.ArcTo(center.X + 0.5, center.Y, center.X, center.Y, RotationType.CCW);
        }
        var prepared = Capture(p, parameters);
        var hole0 = prepared.ClosestEntry(0, new Vector(3.5, 3));
        var hole1 = prepared.ClosestEntry(1, new Vector(7, 6.5));

        var isolated0 = prepared.EmitCandidateForValidation(hole0);
        var isolated1 = prepared.EmitCandidateForValidation(hole1);
        var complete = prepared.Emit(new[] { hole0, hole1, PerimeterCorner(prepared) });

        var blocks = BlockHashes(complete);
        // Each isolated circle matches its own slice; different normals give different
        // emitted circle programs — no cross-contour sharing of rounded state.
        Assert.Equal(blocks[0], BlockHashes(isolated0).Single());
        Assert.Equal(blocks[1], BlockHashes(isolated1).Single());
        Assert.NotEqual(blocks[0], blocks[1]);
    }

    // --- safety gates ---------------------------------------------------------------

    [Fact]
    public void NormalEmit_StillRefusesPerimeterBeforeHoles()
    {
        var prepared = Capture(TwoHoles());
        var corner = PerimeterCorner(prepared);

        // The diagnostic seam does not loosen the ordinary perimeter-last gate.
        Assert.Throws<ArgumentException>(() => prepared.Emit(new[] { corner }));
        Assert.Throws<ArgumentException>(() => prepared.EmitPrefix(new[] { corner }));
    }

    [Fact]
    public void ForeignOrForgedChoice_IsRejected()
    {
        var prepared = Capture(TwoHoles());
        var other = Capture(TwoHoles());
        var foreign = other.Entry(other.PerimeterOrdinal, 0, new Vector(0, 0));
        Assert.Throws<ArgumentException>(() => prepared.EmitCandidateForValidation(foreign));

        // A copy re-stamped with this preparation as owner is still rejected when its
        // contour or point does not exist on this preparation: ownership alone is not enough.
        var forgedContour = other.Entry(0, 0, new Vector(3.5, 3)) with { Owner = prepared, ContourOrdinal = 99 };
        Assert.Throws<ArgumentException>(() => prepared.EmitCandidateForValidation(forgedContour));
        var forgedEntity = other.Entry(0, 0, new Vector(3.5, 3)) with { Owner = prepared, EntityOrdinal = 99 };
        Assert.Throws<ArgumentException>(() => prepared.EmitCandidateForValidation(forgedEntity));
    }

    [Fact]
    public void SourceProgramAndSettings_AreNotMutated()
    {
        var source = TwoHoles();
        var before = ExplicitContourTests.Fingerprint(source);
        var parameters = ExplicitContourTests.Parameters();
        var prepared = Capture(source, parameters);
        var corner = PerimeterCorner(prepared);

        prepared.EmitCandidateForValidation(corner);

        Assert.Equal(before, ExplicitContourTests.Fingerprint(source));
        Assert.Equal(ExplicitContourTests.Parameters().ExternalLeadIn.GetType(),
            parameters.ExternalLeadIn.GetType());
    }

    // --- helpers --------------------------------------------------------------------

    private static List<ExecutionMotion> NonRapid(Program program) =>
        ExecutionMotionReader.Read(program, Vector.Zero, null, default).Motions
            .Where(m => !m.Rapid).ToList();

    /// <summary>
    /// One hash per emitted contour block: cut/lead motion runs in emission order, split at
    /// each first lead-in that follows cut motion (every lead style in Parameters() emits
    /// lead-in motions; scribe motions precede all blocks and are excluded). Each block is
    /// normalised to its own first motion's start and quantised to 1e-9 because the emitted
    /// programs are incremental — the reader resolves absolute positions by accumulating
    /// from the program head, so the same code chain differs from a fresh head by ulps.
    /// </summary>
    /// <summary>The complete program's LAST contour is the perimeter: assert the isolated
    /// probe's blocks equal the trailing block sequence of the complete program.</summary>
    private static void AssertIsolatedMatchesTrailing(Program isolated, Program complete)
    {
        var iso = BlockHashes(isolated);
        var blocks = BlockHashes(complete);
        Assert.True(blocks.Count >= iso.Count);
        Assert.Equal(iso, blocks.Skip(blocks.Count - iso.Count).ToList());
    }

    private static List<string> BlockHashes(Program program)
    {
        var motions = NonRapid(program).Where(m => m.Layer != LayerType.Scribe).ToList();
        var blocks = new List<List<ExecutionMotion>>();
        var current = new List<ExecutionMotion>();
        var sawCut = true;
        foreach (var motion in motions)
        {
            var isLead = motion.Layer == LayerType.Leadin || motion.Layer == LayerType.Leadout;
            if (motion.Layer == LayerType.Leadin && sawCut && current.Count > 0)
            {
                blocks.Add(current);
                current = new List<ExecutionMotion>();
            }
            current.Add(motion);
            if (!isLead)
                sawCut = true;
        }
        if (current.Count > 0)
            blocks.Add(current);
        return blocks.Select(Hash).ToList();
    }

    private static string Hash(List<ExecutionMotion> block)
    {
        var origin = block[0].Start ?? block[0].End;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", block.Select(m =>
            string.Join("|", m.Layer,
                Q((m.Start ?? m.End).X - origin.X), Q((m.Start ?? m.End).Y - origin.Y),
                Q(m.End.X - origin.X), Q(m.End.Y - origin.Y)))))));

        static string Q(double value) => (value / 1e-9).ToString("F0", CultureInfo.InvariantCulture);
    }
}
