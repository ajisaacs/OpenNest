using OpenNest.Bending;
using OpenNest.Breakoffs;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Breakoffs;

/// <summary>
/// A 10 x 6 part with a triangular breakoff (10,1) -> (12,3) -> (10,5) on its right edge; the
/// right edge is one line, so the breakoff's ends split it mid-entity.
/// </summary>
public class BreakoffBuilderTests
{
    private const double W = Breakoff.DefaultTabWidth;

    private static List<Entity> Part(bool leftBreakoff = false)
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
            new Line(10, 1, 12, 3), new Line(12, 3, 10, 5),
        };
        if (leftBreakoff)
            entities.AddRange(new[] { new Line(0, 1, -2, 3), new Line(-2, 3, 0, 5) });
        return entities;
    }

    private static Breakoff On(params Entity[] edges) => new() { EdgeIds = edges.Select(e => e.Id).ToList() };

    private static Breakoff Right(List<Entity> part) => On(part[4], part[5]);

    [Fact]
    public void Layout_MergesTheBreakoffIntoTheOutlineAndSlotsTheBreakLine()
    {
        var part = Part();

        var layout = BreakoffBuilder.Layout(part, Right(part));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(4, layout.Length, 9);
        Assert.Equal(new Vector(10, 1), layout.PointAt(0));
        Assert.Equal(new Vector(10, 5), layout.PointAt(4));
        Assert.Equal(64, new Shape { Entities = layout.Outline.ToList() }.Area(), 6);
        Assert.Equal(0, MaxGap(layout.Outline), 12);
        Assert.Equal(BreakoffBuilder.EvenTabCenters(4, Breakoff.DefaultTabCount, W), layout.TabCenters);
        Assert.Equal(Breakoff.DefaultTabCount - 1, layout.Slots.Count);
        Assert.All(layout.Slots, slot => Assert.Equal(0, MaxGap(slot), 12));
    }

    [Fact]
    public void Slots_CutTheBreakLineOnlyBetweenTabs_AndStayInsideTheBreakoff()
    {
        var part = Part();

        var layout = BreakoffBuilder.Layout(part, Right(part));

        var cut = layout.Slots.Select(slot => OnBreakLine(slot)).ToList();
        // Interior tabs keep exactly their width; the end slots also move clear of the breakoff's
        // edges, so the outer tabs are at least as wide.
        for (var i = 1; i < cut.Count; i++)
            Assert.Equal(W, cut[i].From - cut[i - 1].To, 9);
        Assert.True(cut[0].From >= 1 + W - 1e-9);
        Assert.True(cut[^1].To <= 5 - W + 1e-9);

        foreach (var point in layout.Slots.SelectMany(s => s).SelectMany(Ends))
        {
            Assert.True(point.X >= 10 - 1e-9, $"{point} is on the part side");
            Assert.True(System.Math.Abs(point.Y - 3) <= 2 - (point.X - 10) + 1e-9, $"{point} is outside the breakoff");
        }
        // The full slot depth is reached away from the breakoff's edges.
        Assert.Contains(layout.Slots.SelectMany(s => s).SelectMany(Ends),
            p => System.Math.Abs(p.X - (10 + Breakoff.DefaultSlotWidth)) < 1e-9);
    }

    [Fact]
    public void BuildDrawing_PlansReady_WithEveryPierceInsideTheBreakoff()
    {
        var part = Part();
        var drawing = Build(part, Right(part));
        var placed = new Part(drawing, new Vector(5, 5));
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(60, 120);
        plate.Parts.Add(placed);

        var proposal = CuttingPlanBatch.Capture([plate], new CuttingParameters(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.IsReady, string.Join("\n", proposal.Describe("in")));
        var motions = ExecutionMotionReader.Read(Assert.Single(planned.Result.ProposedOrder).CopyProgram(),
            placed.Location, null, default).Motions;
        var leads = motions.Where(m => m.Layer == LayerType.Leadin).ToList();
        Assert.Equal(Breakoff.DefaultTabCount - 1, leads.Count);
        foreach (var lead in leads)
        {
            var pierce = lead.Start!.Value - placed.Location + drawing.Source.Offset;
            Assert.True(pierce.X > 10 && System.Math.Abs(pierce.Y - 3) < 2 - (pierce.X - 10),
                $"pierce {pierce} is not in the breakoff");
        }
    }

    [Fact]
    public void BuildDrawing_KeepsACopyOfTheBreakoffAndCountsItsMaterial()
    {
        var part = Part();
        var breakoff = Right(part);

        var drawing = Build(part, breakoff);

        var kept = Assert.Single(drawing.Breakoffs);
        Assert.NotSame(breakoff, kept);
        Assert.Equal(breakoff.EdgeIds, kept.EdgeIds);
        Assert.InRange(drawing.Area, 62, 64);
        Assert.Equal(Breakoff.DefaultTabCount, PreparedContours.Capture(drawing.Program, new CuttingParameters()).Count);
    }

    [Fact]
    public void ArcBreakoff_IsMergedAndSlotted()
    {
        var part = Part().Take(4).ToList();
        var arc = new Arc(new Vector(10, 3), 2, -System.Math.PI / 2, System.Math.PI / 2);
        part.Add(arc);

        var layout = BreakoffBuilder.Layout(part, On(arc));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(60 + System.Math.PI * 2, new Shape { Entities = layout.Outline.ToList() }.Area(), 3);
        var drawing = Build(part, On(arc));
        Assert.Equal(Breakoff.DefaultTabCount, PreparedContours.Capture(drawing.Program, new CuttingParameters()).Count);
    }

    [Fact]
    public void BreakoffEndingExactlyAtAnArcStart_SplitsTheOutlineThere()
    {
        // A radius-5 disc of two half arcs; the breakoff ends at (5, 0), the first arc's start,
        // and at 30 degrees on that arc.
        var upper = new Arc(Vector.Zero, 5, 0, System.Math.PI);
        var lower = new Arc(Vector.Zero, 5, System.Math.PI, 0);
        var at30 = new Vector(5 * System.Math.Cos(System.Math.PI / 6), 5 * System.Math.Sin(System.Math.PI / 6));
        var a = new Line(new Vector(5, 0), new Vector(7, 1.5));
        var b = new Line(new Vector(7, 1.5), at30);
        var part = new List<Entity> { upper, lower, a, b };

        var layout = BreakoffBuilder.Layout(part, On(a, b));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(5 * System.Math.PI / 6, layout.Length, 6);
        Assert.Equal(at30.X, layout.PointAt(0).X, 9);
        Assert.Equal(0, layout.PointAt(layout.Length).Y, 9);
    }

    [Fact]
    public void TwoBreakoffsOnOneOutline_BothApply()
    {
        var part = Part(leftBreakoff: true);
        var breakoffs = new List<Breakoff> { Right(part), On(part[6], part[7]) };

        var applied = BreakoffBuilder.Apply(part, breakoffs, out var errors);

        Assert.Empty(errors);
        var profile = new ShapeProfile(ShapeProfile.NormalizeEntities(applied));
        Assert.Equal(68, profile.Perimeter.Area(), 6);
        Assert.Equal(2 * (Breakoff.DefaultTabCount - 1), profile.Cutouts.Count);
        Assert.Equal(2 * Breakoff.DefaultTabCount - 1,
            PreparedContours.Capture(Build(part, breakoffs.ToArray()).Program, new CuttingParameters()).Count);
    }

    [Fact]
    public void Apply_LeavesItsInputsUntouched()
    {
        var part = Part();
        var before = part.Select(e => (e.Id, Ends(e).ToArray())).ToList();

        BreakoffBuilder.Apply(part, [Right(part)], out _);

        Assert.Equal(before.Select(b => b.Id), part.Select(e => e.Id));
        Assert.Equal(before.Select(b => b.Item2), part.Select(e => Ends(e).ToArray()));
    }

    [Fact]
    public void BreakoffInsideThePart_IsRefused()
    {
        var part = Part().Take(4).ToList();
        var a = new Line(10, 1, 8, 3);
        var b = new Line(8, 3, 10, 5);
        part.AddRange([a, b]);

        Assert.Equal("It must sit outside the part outline.", BreakoffBuilder.Layout(part, On(a, b)).Error);
    }

    [Fact]
    public void HiddenEdge_IsReportedAndNotApplied()
    {
        var part = Part();
        var breakoff = Right(part);
        part.RemoveAt(5);

        var applied = BreakoffBuilder.Apply(part, [breakoff], out var errors);

        Assert.Equal("Breakoff 1: Some of its edges are hidden or missing.", Assert.Single(errors));
        Assert.Equal(part, applied);
    }

    [Fact]
    public void OverlappingTabs_AreRefused()
    {
        var part = Part();
        var breakoff = Right(part);
        breakoff.TabCenters = [W / 2, W, 4 - W / 2];

        Assert.Equal("Two tabs overlap; space the tabs evenly again.", BreakoffBuilder.Layout(part, breakoff).Error);
    }

    [Fact]
    public void SlotsWithNoRoom_AreRefused()
    {
        var part = Part();
        var breakoff = Right(part);
        breakoff.SlotWidth = 10;

        Assert.StartsWith("There is no room for slot 1", BreakoffBuilder.Layout(part, breakoff).Error);
    }

    [Fact]
    public void FindEdgeChain_FromOneEdge_StopsWhereTheBreakoffMeetsTheOutline()
    {
        var part = Part();

        var chain = BreakoffBuilder.FindEdgeChain(part, part[5]);

        Assert.Equal(new[] { part[4], part[5] }.ToHashSet(), chain.ToHashSet());
    }

    [Fact]
    public void ClampTabCenter_KeepsTabsOnTheLineAndApart()
    {
        var part = Part();
        var layout = BreakoffBuilder.Layout(part, Right(part));

        Assert.Equal(W / 2, layout.ClampTabCenter(0, -5), 9);
        Assert.Equal(layout.TabCenters[1] - W - W / 2, layout.ClampTabCenter(0, layout.TabCenters[1]), 9);
        Assert.Equal(4 - W / 2, layout.ClampTabCenter(layout.TabCenters.Count - 1, 99), 9);
    }

    private static Drawing Build(List<Entity> entities, params Breakoff[] breakoffs) =>
        CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "breakoff" }, entities,
            new List<Bend>(), 1, null, null, breakoffs);

    private static (double From, double To) OnBreakLine(IReadOnlyList<Entity> slot)
    {
        var ys = slot.OfType<Line>()
            .Where(l => System.Math.Abs(l.StartPoint.X - 10) < 1e-9 && System.Math.Abs(l.EndPoint.X - 10) < 1e-9)
            .SelectMany(l => new[] { l.StartPoint.Y, l.EndPoint.Y }).ToList();
        return (ys.Min(), ys.Max());
    }

    private static IEnumerable<Vector> Ends(Entity e) => e switch
    {
        Line l => [l.StartPoint, l.EndPoint],
        Arc a => [a.StartPoint(), a.EndPoint()],
        _ => [],
    };

    private static double MaxGap(IReadOnlyList<Entity> loop)
    {
        var max = 0.0;
        for (var i = 0; i < loop.Count; i++)
            max = System.Math.Max(max, Ends(loop[i]).Last().DistanceTo(Ends(loop[(i + 1) % loop.Count]).First()));
        return max;
    }
}
