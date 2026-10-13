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
