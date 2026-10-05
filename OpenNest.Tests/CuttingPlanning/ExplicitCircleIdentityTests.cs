using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class ExplicitCircleIdentityTests
{
    [Fact]
    public void Emit_DistinctNearRoundedNormalsPreserveActualEntry()
    {
        var source = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        foreach (var x in new[] { 3, 7 })
        {
            source.MoveTo(x + 1, 3);
            source.ArcTo(x + 1, 3, x, 3, RotationType.CCW);
        }
        var prepared = PreparedContours.Capture(source, new CuttingParameters());
        var angle = 0.0000001;
        var choices = new[] { prepared.Entry(0, 0, new Vector(4, 3)),
            prepared.Entry(1, 0, new Vector(7 + System.Math.Cos(angle), 3 + System.Math.Sin(angle))),
            prepared.ClosestEntry(2, Vector.Zero) };
        var emitted = prepared.Emit(choices);
        var circles = ExecutionMotionReader.Read(emitted, Vector.Zero, null, default).Motions
            .Where(m => !m.Rapid && m.Curve.ToEntity() is Circle).ToArray();
        Assert.Equal(choices[1].Point.Y, circles[1].Start!.Value.Y);
        var calls = emitted.Codes.OfType<SubProgramCall>().ToArray();
        Assert.NotSame(calls[0].Program, calls[1].Program);
        Assert.Equal(new[] { 1, 2 }, calls.Select(c => c.Id));
    }

    [Theory]
    [InlineData(1.0000004, false, false)]
    [InlineData(1, true, false)]
    [InlineData(1, false, true)]
    public void Emit_SharesOnlyExactlyEquivalentCirclePrograms(double secondRadius, bool reverse, bool shared)
    {
        var source = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        source.MoveTo(4, 3);
        source.ArcTo(4, 3, 3, 3, RotationType.CCW);
        source.MoveTo(7 + secondRadius, 3);
        source.ArcTo(7 + secondRadius, 3, 7, 3, reverse ? RotationType.CW : RotationType.CCW);
        var prepared = PreparedContours.Capture(source, new CuttingParameters());
        var choices = Enumerable.Range(0, prepared.Count)
            .Select(i => prepared.ClosestEntry(i, new Vector(20, 3))).ToArray();
        var emitted = prepared.Emit(choices);
        var calls = emitted.Codes.OfType<SubProgramCall>().ToArray();
        Assert.Equal(2, calls.Length);
        var cuts = ExecutionMotionReader.Read(emitted, Vector.Zero, null, default).Motions
            .Where(m => !m.Rapid && m.Curve.ToEntity() is Circle).ToArray();
        Assert.Equal(2, cuts.Length);
        Assert.Equal(choices[0].Point, cuts[0].Start);
        Assert.Equal(choices[1].Point, cuts[1].Start);
        Assert.Equal(1, ((Circle)cuts[0].Curve.ToEntity()).Radius, 12);
        Assert.Equal(secondRadius, ((Circle)cuts[1].Curve.ToEntity()).Radius, 12);
        Assert.Equal(RotationType.CCW, Assert.Single(calls[0].Program.Codes.OfType<ArcMove>()).Rotation);
        Assert.Equal(reverse ? RotationType.CW : RotationType.CCW,
            Assert.Single(calls[1].Program.Codes.OfType<ArcMove>()).Rotation);
        Assert.Equal(shared, ReferenceEquals(calls[0].Program, calls[1].Program));
        Assert.Equal(shared, calls[0].Id == calls[1].Id);
        Assert.Equal(new[] { 1, shared ? 1 : 2 }, calls.Select(c => c.Id));
        Assert.All(calls, c => Assert.Same(c.Program, emitted.SubPrograms[c.Id]));
        var fresh = prepared.Emit(choices);
        var freshCalls = fresh.Codes.OfType<SubProgramCall>().ToArray();
        Assert.Equal(calls.Select(c => c.Id), freshCalls.Select(c => c.Id));
        Assert.All(freshCalls, c => Assert.DoesNotContain(calls, old => ReferenceEquals(old.Program, c.Program)));
        calls[0].Program.Codes.Clear();
        Assert.NotEmpty(freshCalls[0].Program.Codes);
        Assert.Equal(ExplicitContourTests.Fingerprint(fresh), ExplicitContourTests.Fingerprint(prepared.Emit(choices)));
    }
}
