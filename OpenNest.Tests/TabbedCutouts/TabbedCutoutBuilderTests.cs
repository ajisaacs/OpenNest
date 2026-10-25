using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.TabbedCutouts;

namespace OpenNest.Tests.TabbedCutouts;

/// <summary>
/// A 10 x 10 part with a 4 x 4 square hole from (3, 3) to (7, 7), drawn clockwise so the builder
/// must re-orient it, and a radius-1 circle hole at (8.5, 8.5).
/// </summary>
public class TabbedCutoutBuilderTests
{
    private const double W = TabbedCutout.DefaultTabWidth;

    private static (List<Entity> Entities, Line[] Square, Circle Circle) Part()
    {
        var square = new[]
        {
            new Line(3, 3, 3, 7), new Line(3, 7, 7, 7), new Line(7, 7, 7, 3), new Line(7, 3, 3, 3),
        };
        var circle = new Circle(8.5, 8.5, 0.5);
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 10), new Line(10, 10, 0, 10), new Line(0, 10, 0, 0),
        };
        entities.AddRange(square);
        entities.Add(circle);
        return (entities, square, circle);
    }

    private static TabbedCutout On(params Entity[] hole) => new() { EdgeIds = hole.Select(e => e.Id).ToList() };

    [Fact]
    public void Layout_CutsTheHoleBetweenEvenlySpacedTabs_Counterclockwise()
    {
        var (entities, square, _) = Part();

        var layout = TabbedCutoutBuilder.Layout(entities, On(square));

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(16, layout.Length, 9);
        // Counterclockwise from the lowest-left vertex: along the bottom edge first.
        Assert.Equal(new Vector(3, 3), Start(layout.Loop[0]));
        Assert.Equal(new Vector(7, 3), End(layout.Loop[0]));
        Assert.Equal(new[] { 2.0, 10.0 }, layout.TabCenters);
        Assert.Equal(TabbedCutout.DefaultTabCount, layout.Pieces.Count);
        Assert.Equal(layout.Pieces.Count, layout.Tabs.Count);
        Assert.All(layout.Pieces, piece => Assert.Equal(8 - W, piece.Sum(e => e.Length), 9));
        Assert.All(layout.Tabs, tab => Assert.Equal(W, tab.Sum(e => e.Length), 9));
        Assert.All(layout.Tabs.SelectMany(t => t), e => Assert.Same(SpecialLayers.Tab, e.Layer));
        Assert.All(layout.Pieces.SelectMany(p => p), e => Assert.NotSame(SpecialLayers.Tab, e.Layer));
        // The first tab is centred on the bottom edge's midpoint, the second on the top edge's.
        Assert.Equal(new Vector(5 + W / 2, 3), Start(layout.Pieces[0][0]));
        Assert.Equal(new Vector(5 + W / 2, 7), End(layout.Pieces[0][^1]));
        // Each tab follows its piece, so pieces and tabs close the hole exactly.
        for (var i = 0; i < layout.Pieces.Count; i++)
        {
            Assert.Equal(End(layout.Pieces[i][^1]), Start(layout.Tabs[i][0]));
            Assert.Equal(End(layout.Tabs[i][^1]), Start(layout.Pieces[(i + 1) % layout.Pieces.Count][0]));
        }
    }

    [Fact]
    public void Layout_OfACircle_SplitsItIntoArcs()
    {
        var (entities, _, circle) = Part();
        var cutout = On(circle);
        cutout.TabCenters = TabbedCutoutBuilder.EvenTabCenters(System.Math.PI, 3, 0.1);

        var layout = TabbedCutoutBuilder.Layout(entities, cutout);

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(System.Math.PI, layout.Length, 9);
        Assert.Equal(new Vector(8, 8.5), Start(layout.Loop[0]));
        Assert.Equal(3, layout.Pieces.Count);
        Assert.Equal(System.Math.PI - 3 * W, layout.Pieces.Sum(p => p.Sum(e => e.Length)), 9);
    }

    [Fact]
    public void TabCentersPastTheStart_WrapAroundTheContour()
    {
        var (entities, square, _) = Part();
        var cutout = On(square);
        cutout.TabCenters = [0.01, 8];

        var layout = TabbedCutoutBuilder.Layout(entities, cutout);

        Assert.True(layout.IsValid, layout.Error);
        // The first tab straddles the start corner: half on the left edge, half on the bottom.
        Assert.Equal(new Vector(3, 3 + W / 2 - 0.01), Start(layout.Tabs[^1][0]));
        Assert.Equal(16 - 2 * W, layout.Pieces.Sum(p => p.Sum(e => e.Length)), 9);
    }

    [Fact]
    public void ThePerimeter_IsRefused()
    {
        var (entities, _, _) = Part();

        Assert.Equal("It must be a hole inside the part's outline.",
            TabbedCutoutBuilder.Layout(entities, On(entities.Take(4).ToArray())).Error);
    }

    [Fact]
    public void OverlappingTabs_AreRefused()
    {
        var (entities, square, _) = Part();
        var cutout = On(square);
        cutout.TabCenters = [4, 4 + W / 2];

        Assert.Equal("Two tabs overlap; space the tabs evenly again.", TabbedCutoutBuilder.Layout(entities, cutout).Error);
    }

    [Fact]
    public void HiddenHole_IsReportedAndNotApplied()
    {
        var (entities, square, _) = Part();
        var cutout = On(square);
        entities.Remove(square[0]);

        var applied = TabbedCutoutBuilder.Apply(entities, [cutout], out var errors);

        Assert.Equal("Tabbed cutout 1: Its contour is hidden or missing.", Assert.Single(errors));
        Assert.DoesNotContain(applied, e => e.Layer == SpecialLayers.Tab);
    }

    [Fact]
    public void FindHole_FromAnyEdge_ReturnsTheWholeLoop()
    {
        var (entities, square, circle) = Part();

        Assert.Equal(square.ToHashSet<Entity>(), TabbedCutoutBuilder.FindHole(entities, square[2]).ToHashSet());
        Assert.Equal([circle], TabbedCutoutBuilder.FindHole(entities, circle));
        var open = new Line(1, 1, 2, 1);
        entities.Add(open);
        Assert.Empty(TabbedCutoutBuilder.FindHole(entities, open));
    }

    [Fact]
    public void ClampTabCenter_KeepsTabsApartAroundTheContour()
    {
        var (entities, square, _) = Part();
        var layout = TabbedCutoutBuilder.Layout(entities, On(square));
        var spacing = W + layout.MinGap;

        Assert.Equal(10 - spacing, layout.ClampTabCenter(0, 13), 9);
        Assert.Equal(2 + spacing, layout.ClampTabCenter(1, 1), 9);
        Assert.Equal(0, layout.ClampTabCenter(0, -1), 9);
        Assert.Equal(3, layout.ClampTabCenter(0, 3), 9);
    }

    [Fact]
    public void BuildDrawing_KeepsTheHoleAsMaterialAndCutsOnlyThePieces()
    {
        var (entities, square, _) = Part();

        var drawing = Build(entities, On(square));

        Assert.Equal(100 - 16 - System.Math.PI * 0.25, drawing.Area, 4);
        var prepared = PreparedContours.Capture(drawing.Program, new CuttingParameters());
        Assert.Equal(2, prepared.Count);
        Assert.Equal(1, prepared.TabbedCutoutCount);
        Assert.Equal(TabbedCutout.DefaultTabCount, prepared.TabbedPieceCount);
        Assert.Equal(TabbedCutout.DefaultTabCount * W,
            Moves(drawing.Program).Where(m => m.Layer == LayerType.Tab).Sum(m => m.Length), 9);
        Assert.Single(drawing.TabbedCutouts);
        Assert.True(LeadMaterialSnapshot.Capture(drawing.Program, Vector.Zero).IsComplete);
    }

    [Theory]
    [MemberData(nameof(LeadIns))]
    public void PlanCutting_EntersEachPieceFromTheSlug_WithNoLeadOut(LeadIn leadIn)
    {
        var (entities, square, _) = Part();
        var drawing = Build(entities, On(square));

        var (planned, part) = Plan(drawing, new CuttingParameters { InternalLeadIn = leadIn });

        Assert.True(planned.IsReady, planned.Describe);
        AssertCutInPieces(Moves(planned.Program, part.Location), part, drawing, leadIn is not NoLeadIn);
    }

    public static TheoryData<LeadIn> LeadIns() => new()
    {
        new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
        new LineLeadIn { Length = 0.25, ApproachAngle = 45 },
        new ArcLeadIn { Radius = 0.1 },
        new NoLeadIn(),
    };

    [Fact]
    public void PlanCutting_CutsATabbedCircleInArcPieces()
    {
        var (entities, _, circle) = Part();
        var cutout = On(circle);
        cutout.TabCenters = TabbedCutoutBuilder.EvenTabCenters(System.Math.PI, 3, 0);
        var drawing = Build(entities, cutout);

        var (planned, part) = Plan(drawing, new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
            InternalLeadIn = new ArcLeadIn { Radius = 0.05 },
        });

        Assert.True(planned.IsReady, planned.Describe);
        var motions = Moves(planned.Program, part.Location);
        var arcs = motions.Where(m => !m.Rapid && m.Layer == LayerType.Display && m.Curve.IsFiniteArc
            && System.Math.Abs((m.End - part.Location + drawing.Source.Offset).DistanceTo(new Vector(8.5, 8.5)) - 0.5) < 1e-9).ToList();
        Assert.Equal(System.Math.PI - 3 * W, arcs.Sum(m => m.Length), 6);
        Assert.Equal(3, motions.Where((m, i) => m.Layer == LayerType.Leadin && i + 1 < motions.Count
            && arcs.Contains(motions[i + 1])).Count());
        Assert.True(part.RestoreLeadInProgram(planned.Program, false));
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(planned.Nest).Findings,
            f => f.Kind == PostVerificationKind.Incomplete);
    }

    [Fact]
    public void ApplyLeadIns_EntersEachPieceFromTheSlug_WithNoLeadOut()
    {
        var (entities, square, _) = Part();
        var drawing = Build(entities, On(square));
        var part = new Part(drawing, new Vector(5, 5));

        part.ApplyLeadIns(new CuttingParameters { InternalLeadIn = new ArcLeadIn { Radius = 0.1 } }, Vector.Zero);

        AssertCutInPieces(Moves(part.Program, part.Location), part, drawing, true);
    }

    [Fact]
    public void PrePostReview_PassesAPlannedTabbedCutout()
    {
        var (entities, square, _) = Part();
        var drawing = Build(entities, On(square));
        var (planned, part) = Plan(drawing, new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
            InternalLeadIn = new ArcLeadIn { Radius = 0.1 },
        });
        Assert.True(planned.IsReady, planned.Describe);
        Assert.True(part.RestoreLeadInProgram(planned.Program, false));

        var report = PostVerificationAnalyzer.Analyze(planned.Nest);

        Assert.DoesNotContain(report.Findings, f => f.Kind is PostVerificationKind.Incomplete
            or PostVerificationKind.MissingLeadIn or PostVerificationKind.RapidCrossing);
    }

    [Fact]
    public void PrePostReview_ReportsAnUnplannedTabbedCutout()
    {
        var (entities, square, _) = Part();
        var part = new Part(Build(entities, On(square)), new Vector(5, 5));

        var report = PostVerificationAnalyzer.Analyze(NestOf(part));

        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Incomplete
            && f.Message.Contains("tabbed cutout still has its tabs"));
    }

    private static void AssertCutInPieces(IReadOnlyList<ExecutionMotion> motions, Part part, Drawing drawing,
        bool leads)
    {
        Vector Local(Vector p) => p - part.Location + drawing.Source.Offset;
        bool InHole(Vector p) => p.X > 3 - 1e-9 && p.X < 7 + 1e-9 && p.Y > 3 - 1e-9 && p.Y < 7 + 1e-9;
        Assert.DoesNotContain(motions, m => m.Layer == LayerType.Tab);
        // Each piece is a run of cuts inside the hole's outline, started by a rapid (and lead-in).
        var pieces = new List<List<ExecutionMotion>>();
        for (var i = 0; i < motions.Count; i++)
        {
            var m = motions[i];
            if (m.Rapid || m.Layer is not (LayerType.Cut or LayerType.Display) || !InHole(Local(m.End))
                || !InHole(Local(m.Curve.Start)))
                continue;
            if (i == 0 || motions[i - 1].Layer is not (LayerType.Cut or LayerType.Display) || motions[i - 1].Rapid)
            {
                pieces.Add(new List<ExecutionMotion>());
                var before = motions[i - 1];
                Assert.Equal(leads, before.Layer == LayerType.Leadin);
                if (leads)
                {
                    // Pierced inside the slug, never reaching back over the tab behind the piece.
                    var pierce = Local(before.Start!.Value);
                    Assert.True(pierce.X > 3 && pierce.X < 7 && pierce.Y > 3 && pierce.Y < 7, $"pierce {pierce}");
                    Assert.True(before.Curve.Reach(m.Curve.Start, m.Curve.Tangent(false) * -1) <= 1e-9,
                        "the lead-in reaches back over the tab");
                }
            }
            pieces[^1].Add(m);
            // No lead-out: a piece ends at a tab.
            if (i + 1 < motions.Count)
                Assert.NotEqual(LayerType.Leadout, motions[i + 1].Layer);
        }
        Assert.Equal(TabbedCutout.DefaultTabCount, pieces.Count);
        Assert.All(pieces, piece => Assert.Equal(8 - W, piece.Sum(m => m.Length), 6));
        // Real gaps: each piece ends a tab's width short of the next one's start.
        foreach (var a in pieces)
            Assert.Equal(W, pieces.Min(b => a[^1].End.DistanceTo(b[0].Curve.Start)), 6);
    }

    private static Nest NestOf(Part part)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(60, 120);
        plate.Parts.Add(part);
        return nest;
    }

    private static Drawing Build(List<Entity> entities, params TabbedCutout[] cutouts) =>
        CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "tabbed" }, entities,
            new List<Bend>(), 1, null, null, null, cutouts);

    private static (PlannedPart Planned, Part Part) Plan(Drawing drawing, CuttingParameters parameters)
    {
        var part = new Part(drawing, new Vector(5, 5));
        var nest = NestOf(part);
        var proposal = CuttingPlanBatch.Capture([nest.Plates[0]], parameters, false).Plan();
        var result = Assert.Single(proposal.Plates);
        return (new PlannedPart(result.IsReady, string.Join("\n", proposal.Describe("in")),
            result.IsReady ? Assert.Single(result.Result.ProposedOrder).CopyProgram() : null, nest), part);
    }

    private sealed record PlannedPart(bool IsReady, string Describe, Program Program, Nest Nest);

    private static IReadOnlyList<ExecutionMotion> Moves(Program program, Vector? location = null) =>
        ExecutionMotionReader.Read(program, location ?? Vector.Zero, null, default).Motions;

    private static Vector Start(Entity e) => e is Line l ? l.StartPoint : ((Arc)e).StartPoint();

    private static Vector End(Entity e) => e is Line l ? l.EndPoint : ((Arc)e).EndPoint();
}
