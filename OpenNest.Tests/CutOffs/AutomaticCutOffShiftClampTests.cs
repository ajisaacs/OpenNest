using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffShiftClampTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClampAroundNominalLine_MovesInsteadOfSkipping(int quadrant)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        plate.EdgeSpacing = new Spacing(0, 0.25, 0, 0);
        var rail = new ClampRail(2, 0.625, new[] { 35.0 });
        plate.SetClampRail(rail);
        var zones = plate.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)).ToArray();
        var parts = plate.Parts.ToArray();
        var plan = AutomaticCutOffPlanner.Create(plate, AutomaticCutOffShiftTests.Options(), new CutOffSettings());

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(3, plan.Definitions.Count);
        Assert.True(System.Math.Abs(plan.Definitions[0].Position.X) < 33.75);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ClampBlockedCut);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ShiftedCut);
        AutomaticCutOffShiftTests.AssertLimits(plan, 35, 17.5);
        AssertClearsPhysicalFootprints(plate, rail, plan);
        Assert.Equal(zones, plate.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)));
        Assert.Equal(parts, plate.Parts.ToArray());
        Assert.Empty(plate.CutOffs);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClampClearanceOnly_ExcludesOtherwiseOptimalCandidate(int quadrant)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        var rail = new ClampRail(0.2, 0.625, new[] { 35.2 });
        plate.SetClampRail(rail);
        var settings = new CutOffSettings();
        var options = AutomaticCutOffShiftTests.Options();
        var withoutClearance = AutomaticCutOffPlanner.Create(plate, options, settings);
        var first = System.Math.Abs(withoutClearance.Definitions[0].Position.X);
        // The line on the part's right edge ties with lines inside it and is nearest nominal.
        Assert.InRange(first, 34.74, 34.76);
        Assert.True(first < rail.Centers[0] - rail.Width / 2);

        // Bottom clearance is the physical rail clearance in every quadrant, not Top.
        plate.EdgeSpacing = new Spacing(0, 0.553, 0, 0);
        var plan = AutomaticCutOffPlanner.Create(plate, options, settings);
        Assert.Equal(3, plan.Definitions.Count);
        Assert.True(System.Math.Abs(plan.Definitions[0].Position.X) < 34.547);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ClampBlockedCut);
        Assert.Equal(withoutClearance.TailSeparatorX, plan.TailSeparatorX);
        AutomaticCutOffShiftTests.AssertLimits(plan, 35, 17.5);
        AssertClearsPhysicalFootprints(plate, rail, plan);
    }

    [Theory]
    [InlineData(1, 18, 26.25)]
    [InlineData(2, 18, 26.25)]
    [InlineData(3, 18, 26.25)]
    [InlineData(4, 18, 26.25)]
    [InlineData(1, 40, 40)]
    [InlineData(2, 40, 40)]
    [InlineData(3, 40, 40)]
    [InlineData(4, 40, 40)]
    public void NoClampFreeArrangement_FallsBackToWholeGapNominalPositions(int quadrant, double width, double center)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        var rail = new ClampRail(width, 0.625, new[] { center });
        plate.SetClampRail(rail);
        // Width 18 covers the entire first-line window. Width 40 leaves candidates
        // in both rows, but the allowed first/second windows are over 35 apart.
        var plan = AutomaticCutOffPlanner.Create(plate, AutomaticCutOffShiftTests.Options(), new CutOffSettings());

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.True(plan.HasSeparatedTail);
        Assert.Equal(new[] { sign * 70.0, plan.TailSeparatorX!.Value }, plan.Definitions.Select(c => c.Position.X));
        var blocked = Assert.Single(plan.Diagnostics.Where(d => d.Code == AutomaticCutOffDiagnosticCode.ClampBlockedCut));
        Assert.Equal(sign * 35, blocked.X);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ShiftedCut);
        Assert.Equal(120, plan.FixedGridCutLength);
        Assert.Equal(60, plan.StripCutLength);
        AssertClearsPhysicalFootprints(plate, rail, plan);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClampFallback_IsLocalToGap_OtherGapStillShifts(int quadrant)
    {
        var plate = new Plate(60, 200) { Quadrant = quadrant, PartSpacing = 0.3 };
        var sign = quadrant is 2 or 3 ? -1 : 1;
        foreach (var right in new[] { 34.75, 69.75, 114.75, 149.75, 180 })
            plate.Parts.Add(AutomaticCutOffShiftTests.Rectangle(10, 20,
                quadrant is 2 or 3 ? -right : right - 10, quadrant is 3 or 4 ? -30 : 10));
        plate.CutOffs.Add(new CutOff(new Vector(sign * 80, 0), CutOffAxis.Vertical));
        var rail = new ClampRail(18, 0.625, new[] { 26.25 });
        plate.SetClampRail(rail);
        var plan = AutomaticCutOffPlanner.Create(plate, AutomaticCutOffShiftTests.Options(), new CutOffSettings());

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(4, plan.Definitions.Count);
        Assert.Equal(sign * 70, plan.Definitions[0].Position.X);
        var shifted = plan.Definitions.Skip(1).Take(2).Select(c => System.Math.Abs(c.Position.X)).ToArray();
        Assert.InRange(shifted[0], 110.3, 114.75);
        Assert.InRange(shifted[1] - shifted[0], 17.5 - AutomaticCutOffPlanner.GeometryTolerance,
            35 + AutomaticCutOffPlanner.GeometryTolerance);
        Assert.True(shifted[1] < 150);
        Assert.InRange(plan.UsedSpan - shifted[1], 0, 35);
        Assert.Single(plan.Diagnostics.Where(d => d.Code == AutomaticCutOffDiagnosticCode.ClampBlockedCut));
        Assert.Equal(2, plan.Diagnostics.Count(d => d.Code == AutomaticCutOffDiagnosticCode.ShiftedCut));
        AssertClearsPhysicalFootprints(plate, rail, plan);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NearExistingSeparatorInsideClamp_RemainsUncertified(int quadrant)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        var options = AutomaticCutOffShiftTests.Options();
        var settings = new CutOffSettings();
        var first = AutomaticCutOffPlanner.Create(plate, options, settings);
        var separator = System.Math.Abs(first.TailSeparatorX!.Value);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        var existing = new CutOff(new Vector(sign * (separator - AutomaticCutOffPlanner.GeometryTolerance / 2), 0),
            CutOffAxis.Vertical);
        plate.CutOffs.Add(existing);
        existing.Regenerate(plate, settings);
        var original = existing.Drawing.Program;
        // Proposed separator is clear, but its Near-equivalent manual coordinate is not.
        var rail = new ClampRail(1, 0.625,
            new[] { separator - AutomaticCutOffPlanner.GeometryTolerance / 4 - 0.5 });
        plate.SetClampRail(rail);
        var plan = AutomaticCutOffPlanner.Create(plate, options, settings);

        Assert.False(plan.HasBlockingDiagnostics);
        Assert.False(plan.HasSeparatedTail);
        Assert.Equal(plate.Size.Length, plan.UsedSpan);
        Assert.Equal(2, plan.Definitions.Count);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ClampBlockedCut);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.NoSafeTailSeparator);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ExistingCutOff);
        Assert.Same(original, existing.Drawing.Program);
        AssertClearsPhysicalFootprints(plate, rail, plan);
    }

    private static void AssertClearsPhysicalFootprints(Plate plate, ClampRail rail, AutomaticCutOffPlan plan)
    {
        // Independent physical oracle: distances from the origin, not mapped zone bounds.
        foreach (var cut in plan.Definitions)
        {
            var distance = System.Math.Abs(cut.Position.X);
            foreach (var center in rail.Centers)
            {
                var low = center - rail.Width / 2 - plate.EdgeSpacing.Bottom;
                var high = center + rail.Width / 2 + plate.EdgeSpacing.Bottom;
                Assert.True(distance < low || distance > high, $"Cut {distance:R} enters [{low:R}, {high:R}].");
            }
            foreach (var move in cut.Drawing.Program.Codes.OfType<Motion>())
                Assert.Equal(cut.Position.X, move.EndPoint.X);
        }
    }
}
