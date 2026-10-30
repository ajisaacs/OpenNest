using OpenNest.Bending;
using OpenNest.Breakoffs;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Breakoffs;

/// <summary>
/// A 10 x 6 part with a triangular breakoff (10,1) -> (12,3) -> (10,5) on its right edge, joined
/// along x = 10 from y = 1 to y = 5. The triangle (area 4) is the smaller side, so it is the scrap.
/// </summary>
public class BreakoffBuilderTests
{
    private const double W = Breakoff.DefaultTabWidth;

    // The outline drawn around part and scrap, with a separate joining line across it.
    private static (List<Entity> Entities, Line Join) Chord()
    {
        var join = new Line(10, 1, 10, 5);
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 1), new Line(10, 1, 12, 3), new Line(12, 3, 10, 5),
            new Line(10, 5, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0), join,
        };
        return (entities, join);
    }

    // The part outline with the scrap's outside edges drawn separately (as in imported nests).
    private static (List<Entity> Entities, Line Join) PartAndScrapEdges()
    {
        var join = new Line(10, 1, 10, 5);
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 1), join, new Line(10, 5, 10, 6), new Line(10, 6, 0, 6),
            new Line(0, 6, 0, 0), new Line(10, 1, 12, 3), new Line(12, 3, 10, 5),
        };
        return (entities, join);
    }

    private static Breakoff On(params Entity[] join) => new() { EdgeIds = join.Select(e => e.Id).ToList() };

    [Fact]
    public void Layout_CutsTheJoiningLineBetweenTabs_TravelingWithTheScrapOnTheLeft()
    {
        var (entities, join) = Chord();

        var layout = BreakoffBuilder.Layout(entities, On(join));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(4, layout.Length, 9);
        Assert.Equal(64, new Shape { Entities = layout.Outline.ToList() }.Area(), 6);
        Assert.Equal(4, new Shape { Entities = layout.Scrap.ToList() }.Area(), 6);
        Assert.Equal(Breakoff.DefaultTabCount - 1, layout.Cuts.Count);
        var spans = layout.Cuts.Select(Span).OrderBy(s => s.Low).ToList();
        for (var i = 1; i < spans.Count; i++)
            Assert.Equal(W, spans[i].Low - spans[i - 1].High, 9);
        Assert.Equal(1 + W, spans[0].Low, 9);
        Assert.Equal(5 - W, spans[^1].High, 9);
        foreach (var cut in layout.Cuts)
        {
            Assert.All(cut, e => Assert.Same(SpecialLayers.Break, e.Layer));
            // Heading down x = 10, the scrap (x > 10) is on the left.
            Assert.True(Start(cut[0]).Y > End(cut[^1]).Y);
        }
    }

    [Fact]
    public void Flip_MakesTheLargerSideTheScrap()
    {
        var (entities, join) = Chord();
        var breakoff = On(join);
        breakoff.Flip = true;

        var layout = BreakoffBuilder.Layout(entities, breakoff);

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(60, new Shape { Entities = layout.Scrap.ToList() }.Area(), 6);
        Assert.All(layout.Cuts, cut => Assert.True(Start(cut[0]).Y < End(cut[^1]).Y));
    }

    [Fact]
    public void ScrapEdgesDrawnApart_AreJoinedIntoTheOutline()
    {
        var (entities, join) = PartAndScrapEdges();

        var layout = BreakoffBuilder.Layout(entities, On(join));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(64, new Shape { Entities = layout.Outline.ToList() }.Area(), 6);
        Assert.Equal(4, new Shape { Entities = layout.Scrap.ToList() }.Area(), 6);
    }

    [Fact]
    public void OutlineJointsWithinImportTolerance_AreMadeExact()
    {
        var (entities, join) = PartAndScrapEdges();
        // The scrap edge stops 0.00005 short of the outline corner, as in the reported nest.
        entities[6] = new Line(10.00005, 1, 12, 3);

        var drawing = Build(entities, On(join));

        Assert.Single(PreparedContours.Capture(drawing.Program, new CuttingParameters()).Enumerate());
    }

    [Fact]
    public void StrayLinesEndingOnTheOutline_DoNotBreakIt()
    {
        var (entities, join) = PartAndScrapEdges();
        // Unmarked one-inch bend marks: one starts where the joining line meets the outline,
        // one is a two-segment chain starting on an outline corner.
        entities.Add(new Line(10, 1, 9.3, 1.7));
        entities.Add(new Line(10, 6, 9.5, 5.5));
        entities.Add(new Line(9.5, 5.5, 9.3, 5.2));

        var applied = BreakoffBuilder.Apply(entities, [On(join)], out var errors);

        Assert.Empty(errors);
        Assert.Equal(Breakoff.DefaultTabCount - 1, applied.Count(e => e.Layer == SpecialLayers.Break));
        Assert.Equal(64, new Shape { Entities = BreakoffBuilder.Layout(entities, On(join)).Outline.ToList() }.Area(), 6);
    }

    [Fact]
    public void BuildDrawing_CutsBreakMovesAndKeepsTheScrapAsMaterial()
    {
        var (entities, join) = Chord();

        var drawing = Build(entities, On(join));

        var prepared = PreparedContours.Capture(drawing.Program, new CuttingParameters());
        Assert.Equal(1, prepared.Count);
        Assert.Equal(Breakoff.DefaultTabCount - 1, prepared.BreakCutCount);
        Assert.Equal(64, drawing.Area, 6);
        Assert.Equal(4 - Breakoff.DefaultTabCount * W,
            Moves(drawing.Program).Where(m => m.Layer == LayerType.Break).Sum(m => m.Length), 9);
        Assert.Single(drawing.Breakoffs);
    }

    [Fact]
    public void PlanCutting_LeadsEveryBreakCutInFromTheScrap()
    {
        var (entities, join) = Chord();
        var drawing = Build(entities, On(join));
        var (planned, part) = Plan(drawing, new CuttingParameters());

        Assert.True(planned.IsReady, planned.Describe);
        var motions = Moves(planned.Program, part.Location);
        var leads = motions.Where((m, i) => m.Layer == LayerType.Leadin
            && i + 1 < motions.Count && motions[i + 1].Layer == LayerType.Break).ToList();
        Assert.Equal(Breakoff.DefaultTabCount - 1, leads.Count);
        foreach (var lead in leads)
        {
            var pierce = lead.Start!.Value - part.Location + drawing.Source.Offset;
            Assert.True(pierce.X > 10 + 1e-6 && System.Math.Abs(pierce.Y - 3) < 2 - (pierce.X - 10),
                $"pierce {pierce} is not in the scrap");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiSegmentJoiningLine_KeepsItsScrapSideThroughTheProgram(bool flip)
    {
        // A bent joining line: winding normalization would re-orient a chain like this.
        var joins = new[] { new Line(10, 1, 10.1, 2), new Line(10.1, 2, 10.1, 4), new Line(10.1, 4, 10, 5) };
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 1), new Line(10, 1, 12, 3), new Line(12, 3, 10, 5),
            new Line(10, 5, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
        };
        entities.AddRange(joins);
        var breakoff = On(joins);
        breakoff.Flip = flip;
        var drawing = Build(entities, breakoff);

        var (planned, part) = Plan(drawing, new CuttingParameters());

        Assert.True(planned.IsReady, planned.Describe);
        var motions = Moves(planned.Program, part.Location);
        var leads = motions.Where((m, i) => m.Layer == LayerType.Leadin
            && i + 1 < motions.Count && motions[i + 1].Layer == LayerType.Break).ToList();
        Assert.Equal(Breakoff.DefaultTabCount - 1, leads.Count);
        foreach (var lead in leads)
        {
            var pierce = lead.Start!.Value - part.Location + drawing.Source.Offset;
            var entry = lead.End - part.Location + drawing.Source.Offset;
            // The triangle is the scrap unless flipped.
            Assert.True(flip ? pierce.X < entry.X : pierce.X > entry.X, $"pierce {pierce} entry {entry}");
        }
    }

    [Fact]
    public void PlanCutting_WithoutLeadIns_PiercesOnTheLine()
    {
        var (entities, join) = Chord();
        var drawing = Build(entities, On(join));
        var parameters = new CuttingParameters { InternalLeadIn = new NoLeadIn() };

        var (planned, part) = Plan(drawing, parameters);

        Assert.True(planned.IsReady, planned.Describe);
        var motions = Moves(planned.Program, part.Location);
        Assert.DoesNotContain(motions, m => m.Layer == LayerType.Leadin);
        var breakStarts = motions.Where((m, i) => m.Layer == LayerType.Break && motions[i - 1].Rapid)
            .Select(m => m.Start!.Value - part.Location + drawing.Source.Offset).ToList();
        Assert.Equal(Breakoff.DefaultTabCount - 1, breakStarts.Count);
        Assert.All(breakStarts, p => Assert.Equal(10, p.X, 9));
    }

    [Fact]
    public void ApplyLeadIns_LeadsBreakCutsInFromTheScrap()
    {
        var (entities, join) = Chord();
        var drawing = Build(entities, On(join));
        var part = new Part(drawing, new Vector(5, 5));

        part.ApplyLeadIns(new CuttingParameters(), new Vector(0, 0));

        var motions = Moves(part.Program, part.Location);
        var leads = motions.Where((m, i) => m.Layer == LayerType.Leadin
            && i + 1 < motions.Count && motions[i + 1].Layer == LayerType.Break).ToList();
        Assert.Equal(Breakoff.DefaultTabCount - 1, leads.Count);
        Assert.All(leads, lead => Assert.True((lead.Start!.Value - part.Location + drawing.Source.Offset).X > 10));
    }

    [Fact]
    public void PrePostReview_DoesNotReportBreakCutsAsLooseFragments()
    {
        var (entities, join) = Chord();
        var drawing = Build(entities, On(join));
        var (planned, part) = Plan(drawing, new CuttingParameters());
        Assert.True(planned.IsReady, planned.Describe);
        Assert.True(part.RestoreLeadInProgram(planned.Program, false));

        var findings = new ReleasedContourState().Check(
            ExecutionMotionReader.Read(part.Program, part.Location, null, default), Vector.Zero, 1);

        Assert.DoesNotContain(findings, f => f.Kind == PostVerificationKind.Incomplete);
        Assert.DoesNotContain(findings, f => f.Message.Contains("break cut"));
        Assert.DoesNotContain(findings, f => f.Kind == PostVerificationKind.RapidCrossing);
    }

    [Fact]
    public void TwoBreakoffsOnOneOutline_BothApply()
    {
        var (entities, join) = Chord();
        var left = new Line(0, 1, 0, 5);
        entities[6] = new Line(0, 6, 0, 5);
        entities.AddRange([new Line(0, 5, -2, 3), new Line(-2, 3, 0, 1), new Line(0, 1, 0, 0), left]);

        var applied = BreakoffBuilder.Apply(entities, [On(join), On(left)], out var errors);

        Assert.Empty(errors);
        Assert.Equal(2 * (Breakoff.DefaultTabCount - 1), applied.Count(e => e.Layer == SpecialLayers.Break)
            / 1);
        var drawing = Build(entities, On(join), On(left));
        Assert.Equal(68, drawing.Area, 6);
    }

    [Fact]
    public void JoiningLineEndingAtAnArcStart_SplitsTheOutlineThere()
    {
        // A radius-5 disc of two half arcs, cut by a chord from (5, 0) (the first arc's start)
        // to the point at 120 degrees.
        var upper = new Arc(Vector.Zero, 5, 0, System.Math.PI);
        var lower = new Arc(Vector.Zero, 5, System.Math.PI, 0);
        var at120 = new Vector(5 * System.Math.Cos(2 * System.Math.PI / 3), 5 * System.Math.Sin(2 * System.Math.PI / 3));
        var join = new Line(new Vector(5, 0), at120);
        var entities = new List<Entity> { upper, lower, join };

        var layout = BreakoffBuilder.Layout(entities, On(join));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(new Vector(5, 0).DistanceTo(at120), layout.Length, 9);
        var segment = 25 / 2.0 * (2 * System.Math.PI / 3 - System.Math.Sin(2 * System.Math.PI / 3));
        Assert.Equal(segment, new Shape { Entities = layout.Scrap.ToList() }.Area(), 2);
    }

    [Fact]
    public void JoiningLineOutsideTheOutline_IsRefused()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
        };
        var outside = new[] { new Line(10, 1, 12, 3), new Line(12, 3, 10, 5) };
        entities.AddRange(outside);

        Assert.Equal("The joining line must run inside the outline.",
            BreakoffBuilder.Layout(entities, On(outside)).Error);
    }

    [Fact]
    public void HiddenJoiningLine_IsReportedAndNotApplied()
    {
        var (entities, join) = Chord();
        var breakoff = On(join);
        entities.Remove(join);

        var applied = BreakoffBuilder.Apply(entities, [breakoff], out var errors);

        Assert.Equal("Breakoff 1: Its joining line is hidden or missing.", Assert.Single(errors));
        Assert.Equal(entities, applied);
    }

    [Fact]
    public void OverlappingTabs_AreRefused()
    {
        var (entities, join) = Chord();
        var breakoff = On(join);
        breakoff.TabCenters = [W / 2, W, 4 - W / 2];

        Assert.Equal("Two tabs overlap; space the tabs evenly again.", BreakoffBuilder.Layout(entities, breakoff).Error);
    }

    [Fact]
    public void FindEdgeChain_FromTheJoiningLine_StopsAtTheOutline()
    {
        var (entities, join) = PartAndScrapEdges();

        Assert.Equal([join], BreakoffBuilder.FindEdgeChain(entities, join));
    }

    [Fact]
    public void ClampTabCenter_KeepsTabsOnTheLineAndApart()
    {
        var (entities, join) = Chord();
        var layout = BreakoffBuilder.Layout(entities, On(join));

        Assert.Equal(W / 2, layout.ClampTabCenter(0, -5), 9);
        Assert.Equal(layout.TabCenters[1] - W - W / 2, layout.ClampTabCenter(0, layout.TabCenters[1]), 9);
        Assert.Equal(4 - W / 2, layout.ClampTabCenter(layout.TabCenters.Count - 1, 99), 9);
    }

    private static Drawing Build(List<Entity> entities, params Breakoff[] breakoffs) =>
        CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "breakoff" }, entities,
            new List<Bend>(), 1, null, null, breakoffs);

    private static (PlannedPart Planned, Part Part) Plan(Drawing drawing, CuttingParameters parameters)
    {
        var part = new Part(drawing, new Vector(5, 5));
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(60, 120);
        plate.Parts.Add(part);
        var proposal = CuttingPlanBatch.Capture([plate], parameters, false).Plan();
        var result = Assert.Single(proposal.Plates);
        return (new PlannedPart(result.IsReady, string.Join("\n", proposal.Describe("in")),
            result.IsReady ? Assert.Single(result.Result.ProposedOrder).CopyProgram() : null), part);
    }

    private sealed record PlannedPart(bool IsReady, string Describe, Program Program);

    private static IReadOnlyList<ExecutionMotion> Moves(Program program, Vector? location = null) =>
        ExecutionMotionReader.Read(program, location ?? Vector.Zero, null, default).Motions;

    private static (double Low, double High) Span(IReadOnlyList<Entity> cut)
    {
        var ys = cut.SelectMany(e => new[] { Start(e).Y, End(e).Y }).ToList();
        return (ys.Min(), ys.Max());
    }

    private static Vector Start(Entity e) => e is Line l ? l.StartPoint : ((Arc)e).StartPoint();

    private static Vector End(Entity e) => e is Line l ? l.EndPoint : ((Arc)e).EndPoint();
}

internal static class PreparedContoursTestExtensions
{
    public static IEnumerable<int> Enumerate(this PreparedContours prepared) => Enumerable.Range(0, prepared.Count);
}
