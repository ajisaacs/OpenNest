using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>
/// A tab trims the perimeter short of its entry, so the lead-out leaves from where the
/// trimmed cut actually ends. Generating it from the nominal entry emitted an arc whose
/// start was off its own radius.
/// </summary>
public class TabbedLeadOutTests
{
    private const double TabSize = 0.2;
    private const double LeadRadius = 0.2;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArcLeadOut_LeavesTangentlyFromTheTrimmedCutEnd(bool reversed)
    {
        var execution = Emit(ExplicitContourTests.Square(reversed), new Vector(-2, 5),
            new ArcLeadOut { Radius = LeadRadius }, tabs: true);
        var (cuts, leadout) = Split(execution);

        var end = cuts[^1].End;
        Assert.Equal(0, end.X, 9);
        Assert.Equal(TabSize, System.Math.Abs(end.Y - 5), 9);
        Assert.Equal(end, leadout.Start!.Value);
        var arc = Assert.IsType<Arc>(leadout.Curve.ToEntity());
        Assert.Equal(LeadRadius, arc.Radius, 9);
        // Centred on the actual end's outward normal, so it touches the edge only where it starts.
        Assert.Equal(-LeadRadius, arc.Center.X, 9);
        Assert.Equal(end.Y, arc.Center.Y, 9);
    }

    [Fact]
    public void ArcLeadOut_OnCurvedPerimeter_IsTangentAtTheTrimmedEnd()
    {
        // Half disc: the entry is on the arc, so the trimmed end's radial differs from the entry's.
        var clean = new Program();
        clean.MoveTo(0, 5); clean.LineTo(10, 5); clean.ArcTo(0, 5, 5, 5, RotationType.CW);
        var execution = Emit(clean, new Vector(5, -2), new ArcLeadOut { Radius = LeadRadius }, tabs: true);
        var (cuts, leadout) = Split(execution);

        var centre = new Vector(5, 5);
        var end = cuts[^1].End;
        Assert.Equal(5, end.DistanceTo(centre), 9);
        Assert.Equal(TabSize, end.DistanceTo(new Vector(5, 0)), 9);
        Assert.Equal(end, leadout.Start!.Value);
        var arc = Assert.IsType<Arc>(leadout.Curve.ToEntity());
        Assert.Equal(LeadRadius, arc.Radius, 9);
        // Externally tangent to the perimeter at the actual end: centre on its radial line.
        Assert.Equal(5 + LeadRadius, arc.Center.DistanceTo(centre), 9);
        var radial = end - centre;
        var offset = arc.Center - end;
        Assert.Equal(0, radial.X * offset.Y - radial.Y * offset.X, 9);
    }

    [Fact]
    public void LineLeadOut_RunsAlongTheEndNormalFromTheTrimmedCutEnd()
    {
        var execution = Emit(ExplicitContourTests.Square(false), new Vector(-2, 5),
            new LineLeadOut { Length = 0.3 }, tabs: true);
        var (cuts, leadout) = Split(execution);

        Assert.Equal(cuts[^1].End, leadout.Start!.Value);
        Assert.Equal(-0.3, leadout.End.X, 9);
        Assert.Equal(cuts[^1].End.Y, leadout.End.Y, 9);
    }

    [Fact]
    public void UntabbedArcLeadOut_StillLeavesFromTheEntry()
    {
        var execution = Emit(ExplicitContourTests.Square(false), new Vector(-2, 5),
            new ArcLeadOut { Radius = LeadRadius }, tabs: false);
        var (cuts, leadout) = Split(execution);

        Assert.Equal(new Vector(0, 5), cuts[^1].End);
        var arc = Assert.IsType<Arc>(leadout.Curve.ToEntity());
        Assert.Equal(-LeadRadius, arc.Center.X, 9);
        Assert.Equal(5, arc.Center.Y, 9);
    }

    private static OwnedExecution Emit(Program clean, Vector approach, LeadOut leadOut, bool tabs)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.TabsEnabled = tabs;
        parameters.TabConfig = new NormalTab { Size = TabSize };
        parameters.ExternalLeadOut = leadOut;
        var emitted = new ContourCuttingStrategy { Parameters = parameters }.Apply(clean, approach);
        return ExecutionMotionReader.Read(emitted.Program, Vector.Zero, null, default);
    }

    private static (ExecutionMotion[] Cuts, ExecutionMotion Leadout) Split(OwnedExecution execution)
    {
        var cuts = execution.Motions.Where(m => m.Layer == LayerType.Display && !m.Rapid).ToArray();
        Assert.NotEmpty(cuts);
        return (cuts, Assert.Single(execution.Motions.Where(m => m.Layer == LayerType.Leadout)));
    }
}
