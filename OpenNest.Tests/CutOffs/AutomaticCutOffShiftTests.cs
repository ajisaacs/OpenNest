using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffShiftTests
{
    internal static AutomaticCutOffOptions Options(double scale = 1)
    {
        return new AutomaticCutOffOptions { Spacing = 35 * scale, MinimumStripLength = 17.5 * scale };
    }

    internal static Part Rectangle(double length, double width, double x, double y)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(length, 0));
        program.Codes.Add(new LinearMove(length, width));
        program.Codes.Add(new LinearMove(0, width));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("shift rectangle", program), new Vector(x, y));
    }

    internal static Plate UserCase(int quadrant = 1, double scale = 1)
    {
        var plate = new Plate(60 * scale, 120 * scale) { Quadrant = quadrant, PartSpacing = 0.3 * scale };
        foreach (var right in new[] { 34.75, 69.75, 80.0 })
            plate.Parts.Add(Rectangle((right == 80 ? 5 : 10) * scale, (right == 80 ? 5 : 20) * scale,
                (quadrant is 2 or 3 ? -right : right - (right == 80 ? 5 : 10)) * scale,
                (quadrant is 3 or 4 ? -30 : 10) * scale));
        return plate;
    }

    internal static double CutLength(Program program) => Enumerable.Range(0, program.Codes.Count / 2)
        .Sum(i => System.Math.Abs(((LinearMove)program.Codes[2 * i + 1]).EndPoint.Y -
            ((RapidMove)program.Codes[2 * i]).EndPoint.Y));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 25.4)]
    [InlineData(2, 25.4)]
    [InlineData(3, 25.4)]
    [InlineData(4, 25.4)]
    public void ParallelEdgeCase_SavesFortyInchesWithSameCountAndSeparator(int quadrant, double scale)
    {
        var plate = UserCase(quadrant, scale);
        var settings = new CutOffSettings { PartClearance = 0.02 * scale };
        var legacy = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 * scale }, settings);
        var shifted = AutomaticCutOffPlanner.Create(plate, Options(scale), settings);
        Assert.Equal(legacy.TailSeparatorX, shifted.TailSeparatorX);
        Assert.Equal(3, shifted.Definitions.Count);
        Assert.True(legacy.PreviewParts.Sum(p => CutLength(p.Program)) -
            shifted.PreviewParts.Sum(p => CutLength(p.Program)) >= 39.9 * scale);
        AssertLimits(shifted, 35 * scale, 17.5 * scale);
        Assert.Empty(plate.CutOffs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.00002)]
    [InlineData(36)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MinimumStrip_RejectsInvalidValues(double minimum)
    {
        var options = Options();
        options.MinimumStripLength = minimum;
        Assert.Throws<ArgumentException>(() => AutomaticCutOffPlanner.Create(UserCase(), options, new CutOffSettings()));
    }

    [Fact]
    public void EqualCosts_SelectNominalPositions_AndLastStripMayBeShort()
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.3 };
        plate.Parts.Add(Rectangle(40, 20, 0, 10));
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        Assert.Equal(new[] { 35.0, 40.3 + AutomaticCutOffPlanner.GeometryTolerance }, plan.Definitions.Select(c => c.Position.X));
        Assert.DoesNotContain(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ShiftedCut);
        Assert.Equal(CutLength(plan.PreviewParts[0].Program), plan.StripCutLength, 8);
        Assert.Equal(plan.StripCutLength, plan.FixedGridCutLength);
        AssertLimits(plan, 35, 17.5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NullAndEqualSpacing_AreIdenticalIncludingDiagnosticsAndTotals(int quadrant)
    {
        var plate = UserCase(quadrant);
        plate.CutOffs.Add(new CutOff(new Vector(quadrant is 2 or 3 ? -35 : 35, 0), CutOffAxis.Vertical));
        var options = new AutomaticCutOffOptions { Spacing = 35 };
        var legacy = AutomaticCutOffPlanner.Create(plate, options, new CutOffSettings());
        options.MinimumStripLength = 35;
        var fixedGrid = AutomaticCutOffPlanner.Create(plate, options, new CutOffSettings());
        Assert.Equal(legacy.Definitions.Select(c => (c.Position, c.Axis, c.StartLimit, c.EndLimit)),
            fixedGrid.Definitions.Select(c => (c.Position, c.Axis, c.StartLimit, c.EndLimit)));
        Assert.Equal(legacy.PreviewParts.Select(p => p.Program.ToString()), fixedGrid.PreviewParts.Select(p => p.Program.ToString()));
        Assert.Equal(legacy.Diagnostics, fixedGrid.Diagnostics);
        Assert.Equal(legacy.TailSeparatorX, fixedGrid.TailSeparatorX);
        Assert.Equal(legacy.UsedSpan, fixedGrid.UsedSpan);
        Assert.Equal(legacy.StripCutLength, fixedGrid.StripCutLength);
        Assert.Equal(legacy.FixedGridCutLength, fixedGrid.FixedGridCutLength);
        Assert.Equal(0, legacy.StripCutLength);
        Assert.Equal(0, legacy.FixedGridCutLength);
    }

    [Fact]
    public void SeededTwoHundredPlates_NeverLonger_RespectLimitsAndAreDeterministic()
    {
        var random = new Random(42131);
        for (var sample = 0; sample < 200; sample++)
        {
            var quadrant = sample % 4 + 1;
            var plate = new Plate(60, 120) { Quadrant = quadrant, PartSpacing = 0.3 };
            for (var index = 0; index < 4; index++)
            {
                var right = 20 + random.NextDouble() * 90;
                var width = 2 + random.NextDouble() * 8;
                var height = 2 + random.NextDouble() * 12;
                var top = 20 + random.NextDouble() * 20;
                plate.Parts.Add(Rectangle(width, height, quadrant is 2 or 3 ? -right : right - width,
                    quadrant is 3 or 4 ? -top : top - height));
            }
            var options = Options();
            var settings = new CutOffSettings();
            var first = AutomaticCutOffPlanner.Create(plate, options, settings);
            var second = AutomaticCutOffPlanner.Create(plate, options, settings);
            var legacy = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, settings);
            Assert.Equal(legacy.Definitions.Count, first.Definitions.Count);
            Assert.Equal(legacy.TailSeparatorX, first.TailSeparatorX);
            Assert.True(first.StripCutLength <= first.FixedGridCutLength + 0.000001);
            Assert.Equal(first.Definitions.Select(c => c.Position.X), second.Definitions.Select(c => c.Position.X));
            Assert.Equal(first.Diagnostics, second.Diagnostics);
            Assert.Equal(first.StripCutLength, second.StripCutLength);
            Assert.Equal(first.PreviewParts.Take(first.PreviewParts.Count - 1).Sum(p => CutLength(p.Program)), first.StripCutLength, 8);
            AssertLimits(first, 35, 17.5);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void FullSpanManualLine_AtTwentyIsBoundary_NotAnExtraObstacle(int quadrant, bool explicitLimits)
    {
        var plate = UserCase(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        var settings = new CutOffSettings { Overtravel = 0.2 };
        var manual = new CutOff(new Vector(sign * 20, 0), CutOffAxis.Vertical);
        if (explicitLimits)
        {
            var bounds = plate.BoundingBox(false);
            manual.StartLimit = bounds.Bottom;
            manual.EndLimit = bounds.Top + settings.Overtravel;
        }
        plate.CutOffs.Add(manual);
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), settings);
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(2, plan.Definitions.Count);
        // The feasible window 45.3..55 misses every rectangle, so all costs tie.
        // One unbroken full-width segment at the gap-relative nominal X wins.
        Assert.Equal(sign * 55, plan.Definitions[0].Position.X);
        Assert.Single(plan.PreviewParts[0].Program.Codes.OfType<LinearMove>());
        Assert.Equal(60.2, CutLength(plan.PreviewParts[0].Program), 8);
        Assert.Equal(plan.StripCutLength, plan.FixedGridCutLength);
        Assert.InRange(System.Math.Abs(plan.Definitions[0].Position.X) - 20, 17.5, 35);
        Assert.InRange(plan.UsedSpan - System.Math.Abs(plan.Definitions[0].Position.X), 0, 35);
        Assert.Same(manual, Assert.Single(plate.CutOffs));
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(2, -1)]
    [InlineData(3, -1)]
    [InlineData(4, -1)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void LimitedManualLine_AtBestPosition_IsAvoidedWithoutBlocking(int quadrant, int toleranceOffset)
    {
        var plate = UserCase(quadrant);
        var best = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        var x = best.Definitions[0].Position.X;
        var manualX = x + toleranceOffset * AutomaticCutOffPlanner.GeometryTolerance;
        plate.CutOffs.Add(new CutOff(new Vector(manualX, 0), CutOffAxis.Vertical)
        {
            EndLimit = plate.BoundingBox(false).Bottom + 2,
        });
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(3, plan.Definitions.Count);
        Assert.DoesNotContain(plan.Definitions, c => c.Position.X == x ||
            System.Math.Abs(c.Position.X - manualX) <= AutomaticCutOffPlanner.GeometryTolerance);
        Assert.True(plan.StripCutLength < plan.FixedGridCutLength);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LimitedManualLine_AtSeparator_StillBlocksAndEmptiesProposedTotals(int quadrant)
    {
        var plate = UserCase(quadrant);
        var first = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        plate.CutOffs.Add(new CutOff(new Vector(first.TailSeparatorX!.Value, 0), CutOffAxis.Vertical)
        {
            EndLimit = plate.BoundingBox(false).Bottom + 2,
        });
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        Assert.True(plan.HasBlockingDiagnostics);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.LimitedCutOffConflict && d.IsBlocking);
        Assert.False(plan.HasSeparatedTail);
        Assert.Empty(plan.Definitions);
        Assert.Empty(plan.PreviewParts);
        Assert.Equal(0, plan.StripCutLength);
        Assert.True(plan.FixedGridCutLength > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ApplyAndRerun_FullSpanDefinitionsMakeEveryGapANoOp(int quadrant)
    {
        var plate = UserCase(quadrant);
        var settings = new CutOffSettings();
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), settings);
        plate.CutOffs.AddRange(plan.Definitions);
        plate.RegenerateCutOffs(settings);
        var parts = plate.Parts.ToArray();
        var rerun = AutomaticCutOffPlanner.Create(plate, Options(), settings);
        Assert.Empty(rerun.Definitions);
        Assert.False(rerun.HasBlockingDiagnostics);
        Assert.Equal(plan.TailSeparatorX, rerun.TailSeparatorX);
        Assert.Equal(parts, plate.Parts.ToArray());
        Assert.Equal(0, rerun.StripCutLength);
        Assert.Equal(0, rerun.FixedGridCutLength);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FullSpanLineInTail_IsIgnoredAsAStripBoundary(int quadrant)
    {
        var plate = UserCase(quadrant);
        var before = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        plate.CutOffs.Add(new CutOff(new Vector(quadrant is 2 or 3 ? -110 : 110, 0), CutOffAxis.Vertical));
        var after = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        Assert.Equal(before.Definitions.Select(c => c.Position.X), after.Definitions.Select(c => c.Position.X));
        Assert.Equal(before.StripCutLength, after.StripCutLength);
        Assert.Equal(before.FixedGridCutLength, after.FixedGridCutLength);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UnsortedDuplicateBoundaries_SpacingSizedGapsNeedNoNewLines(int quadrant)
    {
        var plate = UserCase(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        foreach (var distance in new[] { 55.0, 110, 20, 55, 0 })
            plate.CutOffs.Add(new CutOff(new Vector(sign * distance, 0), CutOffAxis.Vertical));
        var plan = AutomaticCutOffPlanner.Create(plate, Options(), new CutOffSettings());
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(plan.TailSeparatorX, Assert.Single(plan.Definitions).Position.X);
        Assert.Equal(0, plan.StripCutLength);
        Assert.Equal(0, plan.FixedGridCutLength);
        Assert.Equal(5, plate.CutOffs.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LimitedManualLine_WhenNoAllowedCandidate_FallbackStillBlocks(int quadrant)
    {
        var plate = UserCase(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        plate.CutOffs.Add(new CutOff(new Vector(sign * 35, 0), CutOffAxis.Vertical)
        {
            EndLimit = plate.BoundingBox(false).Bottom + 2,
        });
        var options = Options();
        // Only nominal 35 is on the lattice in this first-line window.
        options.MinimumStripLength = 35 - AutomaticCutOffPlanner.GeometryTolerance / 2;
        var plan = AutomaticCutOffPlanner.Create(plate, options, new CutOffSettings());
        Assert.True(plan.HasBlockingDiagnostics);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.LimitedCutOffConflict && d.IsBlocking);
        Assert.Empty(plan.Definitions);
        Assert.False(plan.HasSeparatedTail);
        Assert.Equal(0, plan.StripCutLength);
        Assert.True(plan.FixedGridCutLength > 0);
    }

    internal static void AssertLimits(AutomaticCutOffPlan plan, double spacing, double minimum)
    {
        var positions = plan.Definitions.Where(c => c.Position.X != plan.TailSeparatorX)
            .Select(c => System.Math.Abs(c.Position.X)).ToArray();
        var previous = 0.0;
        foreach (var position in positions)
        {
            Assert.InRange(position - previous, minimum - AutomaticCutOffPlanner.GeometryTolerance,
                spacing + AutomaticCutOffPlanner.GeometryTolerance);
            previous = position;
        }
        Assert.InRange(plan.UsedSpan - previous, 0, spacing + AutomaticCutOffPlanner.GeometryTolerance);
    }
}
