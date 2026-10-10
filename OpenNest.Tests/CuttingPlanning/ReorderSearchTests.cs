using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>Whole-part order chosen by the planner itself (the part order is not preserved).</summary>
public class ReorderSearchTests
{
    [Theory]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [InlineData(36, false)]
    [InlineData(36, true)]
    public void FreeOrder_DenseGrid_IsReadyWithinTheDefaultBudget(int count, bool shuffled)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        foreach (var part in Grid(count, shuffled))
            plate.Parts.Add(part);
        var parts = plate.Parts.ToArray();

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.True(result.Expansions <= 20000);
        Assert.Equal(parts.OrderBy(Key), result.ProposedOrder.Select(p => p.SourcePart).OrderBy(Key));
        Assert.All(result.ProposedOrder, p => Assert.True(p.IsRegenerated));
        Assert.Equal(parts, plate.Parts); // Planning alone never reorders the live plate.
    }

    [SkippableFact]
    public void DenseGrid144_PerformanceProbe()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_CUTTING_PERF") == "1",
            "Set OPENNEST_RUN_CUTTING_PERF=1 for the 144-part cutting-planner probe.");
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        foreach (var part in Grid(144, false))
            plate.Parts.Add(part);
        var source = plate.Parts.ToArray();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), expansionBudget: 57600));
        timer.Stop();
        // Expanded owned execution includes called hole programs; Program.ToString() does not,
        // and rounds coordinates. Hash exact scalar bits in execution order instead.
        static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString("X16");
        static string Point(Vector point) => $"{Bits(point.X)},{Bits(point.Y)}";
        var payload = string.Join("\n", result.ProposedOrder.SelectMany(p =>
            new[] { $"part:{p.SourceOrdinal}:{Point(p.Location)}:{Bits(p.Rotation)}" }.Concat(
                p.Execution.Motions.Select(m =>
                {
                    var geometry = m.Curve?.ToEntity() switch
                    {
                        Arc arc => $"arc:{Point(arc.Center)}:{Bits(arc.Radius)}:{Bits(arc.StartAngle)}:{Bits(arc.EndAngle)}:{arc.Rotation}",
                        Circle circle => $"circle:{Point(circle.Center)}:{Bits(circle.Radius)}:{circle.Rotation}",
                        Line => "line",
                        null => "none",
                        _ => throw new NotSupportedException("Unexpected execution curve."),
                    };
                    return $"{m.Layer}:{m.Rapid}:{(m.Start is { } start ? Point(start) : "null")}:{Point(m.End)}:{geometry}";
                }))));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload)));
        Console.WriteLine($"Cutting144 elapsedMs={timer.Elapsed.TotalMilliseconds:F1} expansions={result.Expansions} status={result.Status} executionSha256={hash}");
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(source.OrderBy(Key), result.ProposedOrder.Select(p => p.SourcePart).OrderBy(Key));
        Assert.Equal(source, plate.Parts);
    }

    [Fact]
    public void FreeOrder_BlockedApproach_LearnsToCutThatPartFirst()
    {
        // Locked programs lead in and out on each part's left side, so leaving a cut part to the
        // right crosses it. The shortest tour B, A, C is blocked at A (crossing B); with A before B
        // it is A, B, C, blocked at C (crossing A and B). Only right to left is safe.
        var a = LeftLeadRectangle("A", 4, 0, 4, 4);
        var b = LeftLeadRectangle("B", 0, 1, 2, 2);
        var c = LeftLeadRectangle("C", 14, 1, 2, 2);
        Assert.Contains(Analyze(b, a, c).Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Contains(Analyze(a, b, c).Findings, f => f.Kind == PostVerificationKind.RapidCrossing);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([a, b, c],
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { c, a, b }, result.ProposedOrder.Select(p => p.SourcePart));
        Assert.All(result.ProposedOrder, p => Assert.False(p.IsRegenerated));
        Assert.Empty(Analyze(c, a, b).Findings);
    }

    [Fact]
    public void FreeOrder_BlockedAfterRegeneratedParts_LearnsBeforeRetryingTheirEntries()
    {
        // Two regenerated parts come first; then the locked trio blocks as above. Retrying every
        // entry combination of the two parts (over a million) before learning would exhaust the budget.
        var drawing = new Drawing("holes", PreparedContourTests.Holes());
        var first = new Part(drawing, new Vector(1, 1));
        var second = new Part(drawing, new Vector(12, 1));
        var a = LeftLeadRectangle("A", 44, 0, 4, 4);
        var b = LeftLeadRectangle("B", 40, 1, 2, 2);
        var c = LeftLeadRectangle("C", 54, 1, 2, 2);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([first, second, a, b, c],
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(new[] { first, second, c, a, b }, result.ProposedOrder.Select(p => p.SourcePart));
        Assert.True(result.IndependentlyReplayed);
    }

    [Fact]
    public void FreeOrder_LearnedOrderContradicts_FallsBackToTheFullSearch()
    {
        // Left leads on its left, right on its right: right straight after left crosses left, and
        // left straight after right crosses right, so "cut before" rules contradict each other.
        // Going via the part above, which leaves downward, reaches right safely.
        var left = LeftLeadRectangle("left", 0, 0, 2, 2);
        var right = LeftLeadRectangle("right", 4, 0, 2, 2, mirror: true);
        var via = LeftLeadRectangle("via", -1, 5, 2, 2, departure: new Vector(-0.25, -0.25));

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([left, right, via],
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { left, via, right }, result.ProposedOrder.Select(p => p.SourcePart));
    }

    [Fact]
    public void FreeOrder_NoSafeOrder_RefusesWithTheCrossing()
    {
        // Leads on the far sides: whichever part is cut first, reaching the other crosses it.
        var left = LeftLeadRectangle("left", 0, 0, 2, 2);
        var right = LeftLeadRectangle("right", 4, 0, 2, 2, mirror: true);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([left, right],
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.Equal(CuttingPlanStatus.ConstraintConflict, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.Contains(result.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
    }

    private static Part[] Grid(int count, bool shuffled)
    {
        var drawing = new Drawing("grid", PreparedContourTests.Holes());
        var side = (int)System.Math.Ceiling(System.Math.Sqrt(count));
        var parts = Enumerable.Range(0, count)
            .Select(i => new Part(drawing, new Vector(1 + i % side * 11, 1 + i / side * 11)))
            .ToArray();
        if (!shuffled)
            return parts;
        var random = new Random(7);
        return parts.OrderBy(_ => random.Next()).ToArray();
    }

    private static (double, double) Key(Part part) => (part.Location.X, part.Location.Y);

    // A locked rectangle whose lead-in and lead-out sit 0.25 outside its left edge (its right
    // edge when mirrored), so the tool departs on that side unless a final rapid moves it on.
    private static Part LeftLeadRectangle(string name, double x, double y, double width, double height,
        bool mirror = false, Vector? departure = null)
    {
        var clean = LeadPathValidationTests.Rectangle(0, 0, width, height);
        var part = new Part(new Drawing(name, clean), new Vector(x, y));
        var edge = mirror ? width : 0;
        var outside = mirror ? width + 0.25 : -0.25;
        var placed = new Program();
        placed.MoveTo(outside, height / 2);
        placed.Codes.Add(new LinearMove(edge, height / 2) { Layer = LayerType.Leadin });
        // Same direction as the clean outline, which runs clockwise from its corner at the origin.
        if (mirror)
        {
            placed.LineTo(width, 0); placed.LineTo(0, 0); placed.LineTo(0, height);
            placed.LineTo(width, height);
        }
        else
        {
            placed.LineTo(0, height); placed.LineTo(width, height); placed.LineTo(width, 0);
            placed.LineTo(0, 0);
        }
        placed.LineTo(edge, height / 2);
        placed.Codes.Add(new LinearMove(outside, height / 2) { Layer = LayerType.Leadout });
        if (departure is { } end)
            placed.MoveTo(end.X, end.Y);
        Assert.True(part.RestoreLeadInProgram(placed, true));
        return part;
    }

    private static PostVerificationReport Analyze(params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        foreach (var source in parts)
        {
            var copy = new Part(new Drawing("replay", (Program)source.BaseDrawing.Program.Clone()), source.Location);
            Assert.True(copy.RestoreLeadInProgram((Program)source.Program.Clone(), source.LeadInsLocked));
            plate.Parts.Add(copy);
        }
        return PostVerificationAnalyzer.Analyze(nest, Vector.Zero);
    }

    private static string Describe(CuttingPlanResult r) =>
        $"{r.Status}, expanded {r.Expansions}: " + string.Join("; ", r.Findings.Select(f => f.Message).Take(5));
}
