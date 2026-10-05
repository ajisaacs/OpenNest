using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class LeadPathValidationTests
{
    [Fact]
    public void ExternalStraightLead_SafeButRejectsCrossingPlacedMaterial()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        Assert.True(target.IsComplete, target.Reason);
        var execution = Read(ExternalLead());
        Assert.True(LeadPathValidator.Check(execution, target, [target]).IsClear);
        var obstacle = LeadMaterialSnapshot.Capture(Rectangle(-2, 4, -1, 6), Vector.Zero);
        var result = LeadPathValidator.Check(execution, target, [obstacle]);
        Assert.True(result.IsComplete, result.Reason);
        Assert.False(result.IsClear);
    }

    [Fact]
    public void HoleScrap_SafeStraightAndArcButRejectsBulgingArc()
    {
        var clean = Rectangle(0, 0, 10, 10);
        clean.Codes.AddRange(Rectangle(2, 2, 8, 8).Codes);
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        Assert.True(target.IsComplete, target.Reason);
        var straight = new Program();
        straight.MoveTo(5, 5);
        straight.Codes.Add(new LinearMove(2, 5) { Layer = LayerType.Leadin });
        straight.LineTo(2, 8);
        AssertClear(straight, target);
        var curved = new Program();
        curved.MoveTo(3, 4);
        curved.Codes.Add(new ArcMove(new Vector(2, 5), new Vector(3, 5), RotationType.CW) { Layer = LayerType.Leadin });
        curved.LineTo(2, 8);
        AssertClear(curved, target);
        var bulge = new Program();
        bulge.MoveTo(4, 3);
        bulge.Codes.Add(new ArcMove(new Vector(2, 5), new Vector(3, 4), RotationType.CW) { Layer = LayerType.Leadin });
        bulge.LineTo(2, 8);
        AssertUnsafe(bulge, target);
        straight.Codes[0] = new RapidMove(1, 5); // Begins in material, not the hole.
        AssertUnsafe(straight, target);
    }

    [Fact]
    public void OtherHoleIsScrap_AndLineArcTangenciesReject()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var ring = Rectangle(-4, 2, 2, 8);
        ring.Codes.AddRange(Rectangle(-3.5, 3, 1, 7).Codes);
        var hole = LeadMaterialSnapshot.Capture(ring, Vector.Zero);
        Assert.True(hole.IsComplete, hole.Reason);
        AssertClear(ExternalLead(), target, hole);
        var tangent = LeadMaterialSnapshot.Capture(Circle(-1.5, 6, 1), Vector.Zero);
        Assert.True(tangent.IsComplete, tangent.Reason);
        AssertUnsafe(ExternalLead(), target, tangent);
        var arc = new Program();
        arc.MoveTo(-2, 5);
        arc.Codes.Add(new ArcMove(new Vector(0, 5), new Vector(-1, 5), RotationType.CW) { Layer = LayerType.Leadin });
        arc.LineTo(0, 10);
        AssertClear(arc, target);
        AssertUnsafe(arc, target, LeadMaterialSnapshot.Capture(Circle(-1, 7, 1), Vector.Zero));
    }

    [Fact]
    public void CoincidentBoundariesAndArbitraryJointsAreNotClear()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var along = new Program();
        along.MoveTo(0, 2);
        along.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        along.LineTo(0, 10);
        AssertUnsafe(along, target);
        var arbitrary = ExternalLead();
        arbitrary.Codes[2] = new LinearMove(5, 5); // Adjacent move is NOT nominal contour.
        AssertUnsafe(arbitrary, target);
        var circular = LeadMaterialSnapshot.Capture(Circle(0, 0, 2), Vector.Zero);
        var coincident = new Program();
        coincident.MoveTo(2, 0);
        coincident.Codes.Add(new ArcMove(new Vector(-2, 0), Vector.Zero) { Layer = LayerType.Leadin });
        coincident.Codes.Add(new ArcMove(new Vector(2, 0), Vector.Zero));
        AssertUnsafe(coincident, circular);
    }

    [Fact]
    public void FullCircleLeadCannotReuseItsJointAsAnArbitraryPierceEndpoint()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var p = new Program(); p.MoveTo(0, 5);
        p.Codes.Add(new ArcMove(new Vector(0, 5), new Vector(-1, 5)) { Layer = LayerType.Leadin });
        p.LineTo(0, 10);
        AssertUnsafe(p, target);
    }

    [Fact]
    public void TinyNativeUncertainContactRefusesRatherThanApproves()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var obstacle = LeadMaterialSnapshot.Capture(Circle(-0.000005, 5, 0.000001), Vector.Zero);
        Assert.True(obstacle.IsComplete, obstacle.Reason);
        var p = ExternalLead(); p.Codes[0] = new RapidMove(-0.00001, 5);
        var check = LeadPathValidator.Check(Read(p), target, [obstacle]);
        Assert.False(check.IsComplete); Assert.False(check.IsClear);
    }

    [Fact]
    public void LeadoutUsesActualTabbedCutEndpoint_NotNominalClosure()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var p = new Program();
        p.MoveTo(0, 5); p.LineTo(0, 10); p.LineTo(10, 10); p.LineTo(10, 0); p.LineTo(0, 0); p.LineTo(0, 4.8);
        p.Codes.Add(new LinearMove(-2, 4.8) { Layer = LayerType.Leadout });
        AssertClear(p, target);
        p.Codes[^1] = new LinearMove(-2, 5) { Layer = LayerType.Leadout };
        AssertClear(p, target); // Departure is still (0,4.8), not the entry (0,5).
        p.Codes.Insert(p.Codes.Count - 1, new RapidMove(0, 5));
        AssertUnsafe(p, target); // No adjacent cut: a rapid cannot bridge the tab.
    }

    [Fact]
    public void ActualArcLeadoutChecksFullSweep_NotOnlyScrapEndpoints()
    {
        var target = LeadMaterialSnapshot.Capture(Rectangle(0, 0, 10, 10), Vector.Zero);
        var p = new Program(); p.MoveTo(0, 10); p.LineTo(0, 5);
        p.Codes.Add(new ArcMove(new Vector(-1, 4), new Vector(-1, 5), RotationType.CW) { Layer = LayerType.Leadout });
        AssertClear(p, target);
        AssertUnsafe(p, target, LeadMaterialSnapshot.Capture(Circle(-1, 3, 1), Vector.Zero));
        p.Codes[^1] = new ArcMove(new Vector(-1, 4), new Vector(0, 4), RotationType.CW) { Layer = LayerType.Leadout };
        AssertUnsafe(p, target);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealStrategyLeadouts_SafePathsOrRefusedMalformedTabbedArc(bool tabs, bool arc)
    {
        var clean = ExplicitContourTests.Square(false);
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        var parameters = ExplicitContourTests.Parameters();
        parameters.TabsEnabled = tabs;
        parameters.TabConfig = new NormalTab { Size = 0.2 };
        parameters.ExternalLeadOut = arc ? new ArcLeadOut { Radius = 0.2 } : new LineLeadOut { Length = 0.2 };
        var emitted = new ContourCuttingStrategy { Parameters = parameters }.Apply(clean, new Vector(-2, 5));
        if (tabs && arc)
        {
            // The retained emitter generates this arc from nominal closure, not the
            // actual tab endpoint. Do not fit a different center or bridge the gap.
            Assert.Throws<ArgumentException>(() => Read(emitted.Program));
            return;
        }
        var execution = Read(emitted.Program);
        Assert.NotEmpty(execution.Motions.Where(m => m.Layer == LayerType.Leadout));
        var result = LeadPathValidator.Check(execution, target, []);
        Assert.True(result.IsComplete, result.Reason); Assert.True(result.IsClear, result.Reason);
        if (tabs)
        {
            var cuts = execution.Motions.Where(m => m.Layer == LayerType.Display && !m.Rapid).ToArray();
            var leadout = Assert.Single(execution.Motions.Where(m => m.Layer == LayerType.Leadout));
            Assert.True(cuts[0].Start!.Value.DistanceTo(cuts[^1].End) > 0.1);
            Assert.Equal(cuts[^1].End, leadout.Start);
        }
    }

    [Fact]
    public void SourceMutationDoesNotAffectSnapshotOrExecution()
    {
        var clean = Rectangle(0, 0, 10, 10);
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        var emitted = ExternalLead();
        var execution = Read(emitted);
        clean.Codes.Clear(); emitted.Codes.Clear();
        Assert.True(LeadPathValidator.Check(execution, target, []).IsClear);
        Assert.Empty(typeof(LeadMaterialSnapshot).GetProperties().Where(p => p.PropertyType == typeof(Program)
            || typeof(Entity).IsAssignableFrom(p.PropertyType)));
    }

    [Fact]
    public void RotatedIncrementalSubprogramActualEmission_AppliesLocationOnce()
    {
        var clean = Rectangle(0, 0, 10, 10);
        var emitted = ExternalLead();
        clean.Rotate(System.Math.PI / 2); emitted.Rotate(System.Math.PI / 2);
        clean.Mode = Mode.Incremental; emitted.Mode = Mode.Incremental;
        var cleanRoot = new Program();
        cleanRoot.Codes.Add(new SubProgramCall(clean, 90) { Offset = new Vector(4, 6) });
        var emittedRoot = new Program();
        emittedRoot.Codes.Add(new SubProgramCall(emitted, 90) { Offset = new Vector(4, 6) });
        var location = new Vector(20, 30);
        var target = LeadMaterialSnapshot.Capture(cleanRoot, location);
        Assert.True(target.IsComplete, target.Reason);
        var execution = Read(emittedRoot, location);
        var lead = Assert.Single(execution.Motions.Where(m => m.Layer == LayerType.Leadin));
        Assert.Equal(19, lead.End.X, 8); Assert.Equal(36, lead.End.Y, 8);
        Assert.True(LeadPathValidator.Check(execution, target, []).IsClear);
        var obstacle = LeadMaterialSnapshot.Capture(Rectangle(18, 33.5, 20, 34.5), Vector.Zero);
        Assert.False(LeadPathValidator.Check(execution, target, [obstacle]).IsClear);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("self")]
    [InlineData("disjoint")]
    [InlineData("nested")]
    [InlineData("nonfinite")]
    [InlineData("zero")]
    [InlineData("suppressed")]
    [InlineData("lead")]
    [InlineData("recursive")]
    public void MalformedMaterialRefuses(string kind)
    {
        var p = Rectangle(0, 0, 10, 10);
        switch (kind)
        {
            case "open": p.Codes.RemoveAt(p.Codes.Count - 1); break;
            case "self": p = new Program(); p.MoveTo(0, 0); p.LineTo(10, 10); p.LineTo(0, 10); p.LineTo(10, 0); p.LineTo(0, 0); break;
            case "disjoint": p.Codes.AddRange(Rectangle(20, 20, 30, 30).Codes); break;
            case "nested": p.Codes.AddRange(Rectangle(2, 2, 8, 8).Codes); p.Codes.AddRange(Rectangle(3, 3, 7, 7).Codes); break;
            case "nonfinite": p.Codes[1] = new LinearMove(double.NaN, 2); break;
            case "zero": p.Codes.Insert(1, new LinearMove(0, 0)); break;
            case "suppressed": ((Motion)p.Codes[1]).Suppressed = true; break;
            case "lead": ((LinearMove)p.Codes[1]).Layer = LayerType.Leadin; break;
            case "recursive": p.Codes.Add(new SubProgramCall(p, 0)); break;
        }
        var target = LeadMaterialSnapshot.Capture(p, Vector.Zero);
        Assert.False(target.IsComplete);
        var result = LeadPathValidator.Check(Read(ExternalLead()), target, []);
        Assert.False(result.IsComplete); Assert.False(result.IsClear);
    }

    [Fact]
    public void ZeroLeadRefuses_MissingLeadIsDelegated_MarksAreNotMaterial_CancellationThrows()
    {
        var clean = Rectangle(0, 0, 10, 10);
        clean.MoveTo(20, 20);
        clean.Codes.Add(new LinearMove(30, 30) { Layer = LayerType.Scribe });
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        Assert.True(target.IsComplete, target.Reason);
        AssertClear(Rectangle(0, 0, 10, 10), target);
        var p = ExternalLead(); p.Codes[0] = new RapidMove(0, 5);
        var result = LeadPathValidator.Check(Read(p), target, []);
        Assert.False(result.IsComplete); Assert.False(result.IsClear);
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => LeadMaterialSnapshot.Capture(clean, Vector.Zero, token));
        Assert.Throws<OperationCanceledException>(() => LeadPathValidator.Check(Read(ExternalLead()), target, [], token));
        p.Codes[1] = new ArcMove(new Vector(0, 5), new Vector(0, 5)) { Layer = LayerType.Leadin };
        Assert.Throws<ArgumentException>(() => Read(p));
        p.Codes[1] = new LinearMove(double.PositiveInfinity, 5) { Layer = LayerType.Leadin };
        Assert.Throws<ArgumentException>(() => Read(p));
    }

    [Theory]
    [InlineData("line")]
    [InlineData("arc")]
    [InlineData("lineline")]
    [InlineData("linearc")]
    public void RealStrategyEmission_ClearExternalAndInternalLeads(string style)
    {
        var clean = ExplicitContourTests.Square(false);
        clean.Codes.AddRange(Circle(5, 5, 2).Codes);
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        Assert.True(target.IsComplete, target.Reason);
        var parameters = ExplicitContourTests.Parameters(style);
        if (style == "linearc")
            ((LineArcLeadIn)parameters.ExternalLeadIn).ApproachAngle = 90;
        var result = new ContourCuttingStrategy { Parameters = parameters }.Apply(clean, new Vector(-2, 5));
        var execution = Read(result.Program);
        Assert.NotEmpty(execution.Motions.Where(m => m.Layer == LayerType.Leadin));
        var check = LeadPathValidator.Check(execution, target, []);
        Assert.True(check.IsComplete, check.Reason);
        Assert.True(check.IsClear, check.Reason + "\n" + string.Join("\n", execution.Motions.Select(m => $"{m.Layer} {m.Start} -> {m.End}, {m.Length}")));
    }

    [Fact]
    public void RealStrategyEmission_UnsafeCompositePierceRefusesWithoutRepair()
    {
        var clean = ExplicitContourTests.Square(false);
        clean.Codes.AddRange(Circle(5, 5, 2).Codes);
        var target = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        var emitted = new ContourCuttingStrategy { Parameters = ExplicitContourTests.Parameters("linearc") }.Apply(clean, new Vector(-2, 5));
        AssertUnsafe(emitted.Program, target);
        var orphan = new Program(); orphan.MoveTo(-5, 5);
        orphan.Codes.Add(new LinearMove(-3, 5) { Layer = LayerType.Leadin });
        AssertUnsafe(orphan, target);
    }

    private static Program Circle(double x, double y, double radius)
    {
        var p = new Program(); p.MoveTo(x + radius, y); p.ArcTo(x + radius, y, x, y, RotationType.CCW); return p;
    }

    private static void AssertClear(Program p, LeadMaterialSnapshot target, params LeadMaterialSnapshot[] others)
    {
        var result = LeadPathValidator.Check(Read(p), target, others);
        Assert.True(result.IsComplete, result.Reason); Assert.True(result.IsClear, result.Reason);
    }

    private static void AssertUnsafe(Program p, LeadMaterialSnapshot target, params LeadMaterialSnapshot[] others)
    {
        var result = LeadPathValidator.Check(Read(p), target, others);
        Assert.True(result.IsComplete, result.Reason); Assert.False(result.IsClear);
    }

    internal static Program Rectangle(double x1, double y1, double x2, double y2)
    {
        var p = new Program();
        p.MoveTo(x1, y1); p.LineTo(x1, y2); p.LineTo(x2, y2);
        p.LineTo(x2, y1); p.LineTo(x1, y1);
        return p;
    }

    private static Program ExternalLead()
    {
        var p = new Program();
        p.MoveTo(-3, 5);
        p.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        p.LineTo(0, 10);
        return p;
    }

    private static OwnedExecution Read(Program p, Vector? location = null) =>
        ExecutionMotionReader.Read(p, location ?? Vector.Zero, null, default);
}
