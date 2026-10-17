using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A nominal contour may lawfully close within <see cref="NominalContourClosure.Tolerance"/>, and
/// capture and preparation already agree on that inclusive boundary. Replay verification must
/// agree too: a rotated tool path legitimately bridges that one admitted joint, so refusing the
/// whole plate over it makes every such converted drawing unplanable.
/// </summary>
public class SeamClosureReplayTests
{
    private static readonly Vector Start = new(-2, 5);

    /// <summary>
    /// Square entering mid-edge so the tool path wraps across the closing seam. The seam offset is
    /// perpendicular to the edge that follows it, so bridging it shifts that edge's support by the
    /// admitted gap instead of merely shortening it.
    /// </summary>
    private static Program SeamRing(double seam)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(seam, 0);
        return program;
    }

    private static (Part Part, CuttingParameters Settings) Fixture(double seam)
    {
        var settings = ExplicitContourTests.Parameters();
        var program = SeamRing(seam);
        var prepared = PreparedContours.Capture(program, settings);
        Assert.Equal(1, prepared.Count); // Preparation accepts the same closure boundary as capture.
        var part = new Part(new Drawing("seam", program));
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.Entry(0, 0, new Vector(0, 5))]), false));
        return (part, settings);
    }

    private static CuttingPlanSnapshot Capture(Part part, CuttingParameters settings)
        => CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));

    private static Program Path(params Vector[] points)
    {
        var program = new Program();
        program.MoveTo(-0.3, 5);
        program.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        foreach (var point in points) program.LineTo(point);
        return program;
    }

    private static OwnedExecution Read(Program program) => ExecutionMotionReader.Read(program, Vector.Zero, null, default);

    [Theory]
    [InlineData(0.0)]       // exactly closed: unchanged behaviour
    [InlineData(5e-7)]      // admitted closure seam
    [InlineData(1e-6)]      // inclusive closure boundary that capture already accepts
    public void AdmittedClosureSeam_StillReplays(double seam)
    {
        var (part, settings) = Fixture(seam);
        var snapshot = Capture(part, settings);
        Assert.True(snapshot.Placements[0].Material!.IsComplete, snapshot.Placements[0].Material!.Reason);

        var source = snapshot.Placements[0];
        var choice = source.Prepared!.Entry(0, 0, new Vector(0, 5)); // path wraps across the seam
        var result = CuttingPlanService.ReplayPrograms(snapshot,
            [source.Propose(source.Prepared.Emit([choice]), Read(source.Prepared.Emit([choice])), [choice])],
            0, default);

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
    }

    [Theory]
    [InlineData(5e-7)]
    [InlineData(1e-6)]
    public void AdmittedClosureSeam_ThatLengthensThePath_StillReplays(double seam)
    {
        // The ring stops short of its start across the following edge, so bridging the seam adds
        // up to the admitted gap to the cut length and extends that edge before the next corner
        // (A0227-08's sprocket outline). Entering on the top edge puts the seam mid-path.
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, -seam);
        var settings = ExplicitContourTests.Parameters();
        var part = new Part(new Drawing("seam", program));
        var snapshot = Capture(part, settings);
        var source = snapshot.Placements[0];
        Assert.True(source.Material!.IsComplete, source.Material.Reason);

        var choice = source.Prepared!.Entry(0, 1, new Vector(5, 10));
        var emitted = source.Prepared.Emit([choice]);
        var result = CuttingPlanService.ReplayPrograms(snapshot,
            [source.Propose(emitted, Read(emitted), [choice])], 0, default);

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
    }

    [Fact]
    public void AdmittedClosureSeam_StillPlansWithGeneratedLeadIns()
    {
        // The same plate through the planner entry point: a searched route over an admitted seam
        // must not be refused at replay.
        var (part, settings) = Fixture(9e-7);
        var result = CuttingPlanService.Plan(Capture(part, settings));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
    }

    [Theory]
    [InlineData("missing-edge")]     // a real coverage hole next to an admitted seam still refuses
    [InlineData("reversed-edge")]    // wrong traversal direction still refuses
    [InlineData("interior-shift")]   // displacement larger than the admitted seam still refuses
    [InlineData("second-circuit")]   // retracing the ring still refuses
    public void AdmittedClosureSeam_KeepsRefusingRealAccountingFaults(string fault)
    {
        var (part, settings) = Fixture(9e-7);
        var snapshot = Capture(part, settings);
        var source = snapshot.Placements[0];
        var choice = source.Prepared!.Entry(0, 0, new Vector(0, 5));
        var actual = fault switch
        {
            // Leaves the bottom edge uncut: coverage, not closure slack, is what fails.
            "missing-edge" => Path(new Vector(0, 10), new Vector(10, 10), new Vector(10, 0)),
            // Runs the right edge backwards against the nominal direction.
            "reversed-edge" => Path(new(0, 5), new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5)),
            // Closes exactly but shifts an interior vertex by far more than the admitted seam.
            "interior-shift" => Path(new Vector(0, 5), new(0, 10), new(10, 9.9999), new(10, 0), new(0, 0), new(0, 5)),
            // Two circuits are never closure slack.
            "second-circuit" => Path(new(0, 5), new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5),
                new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5)),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        var result = CuttingPlanService.ReplayPrograms(snapshot, [source.Propose(actual, Read(actual), [choice])],
            0, default);

        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
    }

    [Fact]
    public void GapBeyondAdmittedClosure_StillRefusesAtCapture()
    {
        // The fix must not widen closure admission itself: beyond tolerance capture still refuses.
        var program = SeamRing(2e-6);
        var material = LeadMaterialSnapshot.Capture(program, new Vector(30, 20));
        Assert.False(material.IsComplete);
        Assert.Contains("closure tolerance", material.Reason);
    }
}
