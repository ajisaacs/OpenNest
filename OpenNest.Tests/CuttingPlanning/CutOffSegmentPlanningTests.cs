using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.Tests.CutOffs;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Cut-off segments are planned one by one: each precedes only the parts in its span of the
/// line, the parts are planned as if the segments were not there, and each segment is cut as
/// late as it can be before its first part, where its rapids cross nothing cut before it.
/// </summary>
public class CutOffSegmentPlanningTests
{
    [Fact]
    public void Capture_GivesEachSegmentThePartsInItsSpan()
    {
        var (plate, cutOff, lower, upper) = CutOffSegmentsTests.ThreeSegments();
        var segments = CutOffSegments.Of(plate, cutOff).Select(s => plate.Parts.IndexOf(s.Part)).ToArray();

        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate));

        Assert.Equal(new[] { segments[0], segments[1] }, snapshot.Dependencies.PrerequisitesOf(plate.Parts.IndexOf(lower)));
        Assert.Equal(new[] { segments[1], segments[2] }, snapshot.Dependencies.PrerequisitesOf(plate.Parts.IndexOf(upper)));
        Assert.All(segments, s => Assert.Empty(snapshot.Dependencies.PrerequisitesOf(s)));
        Assert.Equal(segments, CutOffInsertion.Attached(snapshot));
    }

    // Locked squares in a row and a line through the last one, C. Both segments go straight
    // before C when the squares before it leave the tool in plain sight of their pierces. A
    // square D below the row, left from its top-left corner, walls off the bottom segment's
    // pierce: cut just before C, that segment's approach would cross D, so it goes one gap
    // earlier, before D, while the top segment (reached over D) stays before C. Segments go
    // where the route is, never all the way to the start.
    [Theory]
    [InlineData(false, "A B s0 s1 C")]
    [InlineData(true, "A B s0 D s1 C")]
    public void Plan_PlacesEachSegmentInTheLatestGapWhereItsRapidsAreClear(bool wall, string expected)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(60, 120);
        var a = LockedSquare("A", new Vector(5, 10), topLeft: false);
        var b = LockedSquare("B", new Vector(25, 10), topLeft: false);
        var c = LockedSquare("C", new Vector(50, 12), topLeft: false);
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        if (wall)
            plate.Parts.Add(LockedSquare("D", new Vector(37, 0), topLeft: true));
        plate.Parts.Add(c);
        var cutOff = new CutOff(new Vector(55, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());
        var segments = CutOffSegments.Of(plate, cutOff);
        Assert.Equal(2, segments.Count);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed, Describe(result));
        string Name(Part part) => part.BaseDrawing.IsCutOff
            ? $"s{segments.ToList().FindIndex(s => ReferenceEquals(s.Part, part))}" : part.BaseDrawing.Name;
        Assert.Equal(expected, string.Join(" ", result.ProposedOrder.Select(o => Name(o.SourcePart))));
    }

    // Two rows of interlocking parts (each column's neck sits beside its neighbours' bodies)
    // with lines through two columns and a tail separator. The parts keep the route they get
    // without the lines; each segment precedes the parts beside it and nothing else.
    [Fact]
    public void WalledColumns_PartsKeepTheirRoute_SegmentsPrecedeThePartsBesideThem()
    {
        var parameters = ExplicitContourTests.Parameters();
        var (plain, _) = WalledColumns(lines: false);
        var withoutLines = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plain, confirmedParameters: parameters));
        Assert.True(withoutLines.Status == CuttingPlanStatus.Ready, Describe(withoutLines));

        var (plate, lines) = WalledColumns(lines: true);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed, Describe(result));
        var order = result.ProposedOrder.Select(o => o.SourcePart).ToList();
        Assert.True(order[^1].BaseDrawing.IsCutOff && result.ProposedOrder[^1].SeparatesSheet);
        Assert.Equal(withoutLines.ProposedOrder.Where(o => !o.IsCutOff).Select(o => o.SourcePart.BoundingBox.Location),
            order.Where(p => !p.BaseDrawing.IsCutOff).Select(p => p.BoundingBox.Location));
        var bounds = plate.BoundingBox(includeParts: false);
        var segments = 0;
        foreach (var line in lines)
            foreach (var segment in CutOffSegments.Of(plate, line))
            {
                segments++;
                var beside = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff && CuttingDependencyGraph.CutOffCrosses(
                    line.Axis, line.Position, segment.SpanStart, segment.SpanEnd, p.BoundingBox, bounds)).ToList();
                Assert.NotEmpty(beside);
                Assert.All(beside, part => Assert.True(order.IndexOf(segment.Part) < order.IndexOf(part)));
                // Cut where the route is: never first, with at least one part cut before it.
                Assert.True(order.IndexOf(segment.Part) > 0);
            }
        Assert.Equal(6, segments);

        // Applied and replanned with the order kept, the segments are looked through: the
        // parts get the entries and routes the plan had, and it replays Ready again.
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        var kept = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: parameters,
            preservePartOrder: true));
        Assert.True(kept.Status == CuttingPlanStatus.Ready && kept.IndependentlyReplayed, Describe(kept));
        Assert.Equal(order, kept.ProposedOrder.Select(o => o.SourcePart));
        Assert.Equal(result.RapidDistance, kept.RapidDistance, 6);
    }

    private const double Gap = 0.25, Width = 10.6, Neck = 5.6, Height = 20, Margin = 0.25;
    // The slanted shoulders need more horizontal room than the gap.
    private const double Pitch = (Width + Neck) / 2 + 2 * Gap;

    private static (Plate Plate, CutOff[] Lines) WalledColumns(bool lines)
    {
        const int columns = 8;
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var sheetX = 2 * Margin + (columns - 1) * Pitch + Width + 4;
        plate.Size = new Size(2 * Margin + 2 * Height + Gap, sheetX);
        var upright = new Drawing("upright", Bottle(false));
        var inverted = new Drawing("inverted", Bottle(true));
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < columns; column++)
                plate.Parts.Add(new Part(column % 2 == 0 ? upright : inverted,
                    new Vector(Margin + column * Pitch, Margin + row * (Height + Gap))));
        // Beyond the parts: one continuous cut that splits off the tail.
        var cutOffs = new List<CutOff> { new(new Vector(sheetX - 2, 0), CutOffAxis.Vertical) };
        if (lines)
        {
            cutOffs.Add(new(new Vector(Margin + 2 * Pitch + Width * 0.6, 0), CutOffAxis.Vertical));
            cutOffs.Add(new(new Vector(Margin + 5 * Pitch + Width * 0.6, 0), CutOffAxis.Vertical));
        }
        foreach (var cutOff in cutOffs)
            plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());
        PlateSequencing.PlaceCutOffs(plate, cutOffs);
        return (plate, cutOffs.Skip(1).ToArray());
    }

    // A narrow neck below a slanted shoulder and a body with chamfered top corners, with two
    // square holes; inverted, mirrored top to bottom.
    private static Program Bottle(bool inverted)
    {
        const double overhang = (Width - Neck) / 2, chamfer = 2.5;
        var points = new List<Vector>
        {
            new(overhang, 0), new(overhang, Height * 0.4), new(0, Height * 0.6), new(0, Height * 0.85),
            new(chamfer, Height), new(Width - chamfer, Height), new(Width, Height * 0.85),
            new(Width, Height * 0.6), new(overhang + Neck, Height * 0.4), new(overhang + Neck, 0),
        };
        if (inverted)
            points = points.Select(p => new Vector(p.X, Height - p.Y)).Reverse().ToList();
        var program = new Program();
        program.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
            program.LineTo(point.X, point.Y);
        program.LineTo(points[0].X, points[0].Y);
        foreach (var y in new[] { 2.0, Height - 2.5 })
        {
            const double x = Width / 2 - 0.25;
            program.MoveTo(x, y);
            program.LineTo(x + 0.5, y);
            program.LineTo(x + 0.5, y + 0.5);
            program.LineTo(x, y + 0.5);
            program.LineTo(x, y);
        }
        return program;
    }

    // A 10 x 10 square cut from (and left at) its bottom-right or top-left corner, with a
    // short lead-in and lead-out, locked so the planner keeps it as given.
    private static Part LockedSquare(string name, Vector location, bool topLeft)
    {
        var clean = new Program();
        clean.MoveTo(0, 0);
        clean.LineTo(0, 10);
        clean.LineTo(10, 10);
        clean.LineTo(10, 0);
        clean.LineTo(0, 0);
        var part = new Part(new Drawing(name, clean), location);
        var placed = new Program();
        var corners = topLeft
            ? new[] { new Vector(0, 10), new Vector(10, 10), new Vector(10, 0), new Vector(0, 0) }
            : new[] { new Vector(10, 0), new Vector(0, 0), new Vector(0, 10), new Vector(10, 10) };
        var lead = topLeft ? new Vector(-0.2, 10.2) : new Vector(10.2, -0.2);
        placed.MoveTo(lead.X, lead.Y);
        placed.Codes.Add(new LinearMove(corners[0]) { Layer = LayerType.Leadin });
        foreach (var corner in corners.Skip(1).Append(corners[0]))
            placed.Codes.Add(new LinearMove(corner));
        placed.Codes.Add(new LinearMove(lead) { Layer = LayerType.Leadout });
        Assert.True(part.RestoreLeadInProgram(placed, locked: true));
        return part;
    }

    private static string Describe(CuttingPlanResult r) =>
        $"{r.Status}, expanded {r.Expansions}: " + string.Join("; ", r.Findings.Select(f => f.Message));
}
