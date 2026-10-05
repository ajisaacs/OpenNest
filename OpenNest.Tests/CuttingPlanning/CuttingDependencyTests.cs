using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingDependencyTests
{
    // Started just above P, nearest-first cuts P before the far cutoff start. P leaves upward
    // and the cutoff's trimmed cut stops short of P, so P, cutoff, Q crosses nothing: only the
    // nominal-span dependency puts the cutoff first.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_CutOffWhoseNominalSpanCrossesAPart_IsCutFirstAndAppliedThatWay(bool regenerate)
    {
        var parameters = ExplicitContourTests.Parameters();
        var (_, plate, p, q, cut) = CutOffPlate(parameters, orphan: false);
        var request = new CuttingPlanRequest(plate, new Vector(11, 12.5),
            confirmedParameters: regenerate ? parameters : null);
        var snapshot = CuttingPlanService.Capture(request);
        Assert.Equal(new[] { 2 }, snapshot.Dependencies.PrerequisitesOf(0));
        Assert.Empty(snapshot.Dependencies.PrerequisitesOf(1));
        Assert.Empty(snapshot.Dependencies.PrerequisitesOf(2));

        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var order = result.ProposedOrder.Select(o => o.SourcePart).ToList();
        Assert.True(order.IndexOf(cut) < order.IndexOf(p), string.Join(",", result.ProposedOrder.Select(o => o.SourceOrdinal)));
        Assert.Equal(regenerate, result.ProposedOrder.Single(o => ReferenceEquals(o.SourcePart, p)).IsRegenerated);
        Assert.False(result.ProposedOrder.Single(o => ReferenceEquals(o.SourcePart, cut)).IsRegenerated);

        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        Assert.Equal(order, plate.Parts);
        Assert.Contains(q, plate.Parts);
    }

    [Fact]
    public void Apply_CutOffReclassifiedAfterPlanning_IsStale()
    {
        var (_, plate, p, q, cut) = CutOffPlate(ExplicitContourTests.Parameters(), orphan: false);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate, new Vector(11, 12.5)));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        cut.BaseDrawing.IsCutOff = false;

        var commit = CuttingPlanService.Apply([result]);

        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
        Assert.Equal(new[] { p, q, cut }, plate.Parts);
    }

    [Fact]
    public void Plan_OrphanedCutOff_PrecedesEveryPart()
    {
        var (_, plate, p, q, cut) = CutOffPlate(ExplicitContourTests.Parameters(), orphan: true);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(plate, new Vector(11, 12.5)));
        Assert.Equal(new[] { 2 }, snapshot.Dependencies.PrerequisitesOf(0));
        Assert.Equal(new[] { 2 }, snapshot.Dependencies.PrerequisitesOf(1));

        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Same(cut, result.ProposedOrder[0].SourcePart);
    }

    [Fact]
    public void Plan_PreservedOrderCuttingAPartBeforeItsCutOff_IsAConstraintConflict()
    {
        var (_, plate, p, _, cut) = CutOffPlate(ExplicitContourTests.Parameters(), orphan: false);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate, new Vector(11, 12.5), preservePartOrder: true));

        Assert.Equal(CuttingPlanStatus.ConstraintConflict, result.Status);
        Assert.Empty(result.ProposedOrder);
        var finding = Assert.Single(result.Findings);
        Assert.Same(p, finding.SourcePart);
        Assert.Same(cut, finding.OtherSourcePart);
        Assert.Contains("cutoff", finding.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replay_RechecksDependenciesInsteadOfTrustingTheSearch(bool regenerate)
    {
        var parameters = ExplicitContourTests.Parameters();
        var (_, plate, _, _, _) = CutOffPlate(parameters, orphan: false);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(plate, new Vector(11, 12.5),
            confirmedParameters: regenerate ? parameters : null));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, ready.Status);
        // Same verified placements, counterfeit order: crossed part before its cutoff.
        var counterfeit = ready.ProposedOrder.OrderBy(o => o.SourceOrdinal).ToArray();

        var replayed = regenerate
            ? CuttingPlanService.ReplayPrograms(snapshot, counterfeit, 0, default)
            : CuttingPlanService.Replay(snapshot, counterfeit.Select(o => o.SourceOrdinal).ToArray(), 0, default);

        Assert.Equal(CuttingPlanStatus.InvalidInput, replayed.Status);
        Assert.Empty(replayed.ProposedOrder);
        Assert.Contains(replayed.Findings, f => f.SourceOrdinal == 0 && f.OtherSourceOrdinal == 2);
    }

    [Fact]
    public void Capture_DetachedListWithACutOff_IsUnsupported()
    {
        var (_, plate, _, _, _) = CutOffPlate(ExplicitContourTests.Parameters(), orphan: false);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate.Parts.ToArray()));

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains("plate-scoped", Assert.Single(result.Findings).Message);
    }

    [Fact]
    public void Plan_PartInsideAHostCutout_IsCutBeforeTheHost()
    {
        var (plate, host, inner) = NestedPlate(Rectangle(2.7, 2.7, 0.6, 0.6));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(plate));
        Assert.Equal(new[] { 1 }, snapshot.Dependencies.PrerequisitesOf(0));
        Assert.Empty(snapshot.Dependencies.PrerequisitesOf(1));

        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(new[] { inner, host }, result.ProposedOrder.Select(o => o.SourcePart));

        var preserved = CuttingPlanService.Plan(new CuttingPlanRequest(plate, preservePartOrder: true));
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, preserved.Status);
        var finding = Assert.Single(preserved.Findings);
        Assert.Same(host, finding.SourcePart);
        Assert.Same(inner, finding.OtherSourcePart);
        Assert.Contains("nested in its cutout", finding.Message);
    }

    [Fact]
    public void Dependencies_NonmaterialMotionsCannotHideANestedPart()
    {
        // Same insert as above plus a remote rapid and scribe mark: material is unchanged, so
        // the inner-before-host prerequisite must not depend on whole-program bounds.
        var (plate, host, inner) = NestedPlate(Marked(Rectangle(2.7, 2.7, 0.6, 0.6)));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(plate));
        Assert.Null(snapshot.Findings.FirstOrDefault()?.Message);
        Assert.True(inner.BoundingBox.Left < host.BoundingBox.Left);

        Assert.Equal(new[] { 1 }, snapshot.Dependencies.PrerequisitesOf(0));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(new[] { inner, host }, result.ProposedOrder.Select(o => o.SourcePart));
    }

    [Fact]
    public void Capture_PartStraddlingAHostCutoutEdge_IsAmbiguousContainment()
    {
        var (plate, host, inner) = NestedPlate(Rectangle(3.6, 2.8, 0.8, 0.4));

        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate));

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Same(inner, finding.SourcePart);
        Assert.Same(host, finding.OtherSourcePart);
        Assert.Contains("Containment", finding.Message);
    }

    [Fact]
    public void Capture_PartInAConcavePocketOutsideTheHost_HasNoDependency()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var host = Pocketed();
        var inner = Rectangle(4.5, 5, 1, 1);
        plate.Parts.Add(host);
        plate.Parts.Add(inner);

        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(plate));

        Assert.Null(snapshot.Findings.FirstOrDefault()?.Message);
        Assert.Empty(snapshot.Dependencies.PrerequisitesOf(0));
        Assert.Empty(snapshot.Dependencies.PrerequisitesOf(1));
        Assert.Equal(CuttingPlanStatus.Ready, CuttingPlanService.Plan(snapshot).Status);
    }

    [Fact]
    public void DependencyGraph_ReportsCyclesAndFirstViolation()
    {
        Assert.NotNull(CuttingDependencyGraph.FromPrerequisites([[1], [0], []]).FindCycle());
        var chain = CuttingDependencyGraph.FromPrerequisites([[], [0], [1]]);
        Assert.Null(chain.FindCycle());
        Assert.Null(chain.FirstViolation([0, 1, 2]));
        Assert.Equal((2, 1), chain.FirstViolation([0, 2, 1]));
        Assert.True(chain.IsReady(1, [0]));
        Assert.False(chain.IsReady(2, [0]));
    }

    private static (Nest, Plate, Part P, Part Q, Part Cut) CutOffPlate(CuttingParameters parameters, bool orphan)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var p = Emitted(parameters, new Vector(10, 10));
        var q = Emitted(parameters, new Vector(40, 20));
        var cutOff = new CutOff(new Vector(11, 0), CutOffAxis.Vertical) { StartLimit = 0, EndLimit = 30 };
        var program = new Program();
        program.Codes.Add(new RapidMove(11, 30));
        program.Codes.Add(new LinearMove(11, 12.5) { Layer = LayerType.Cut });
        cutOff.Drawing.Program = program;
        if (!orphan)
            plate.CutOffs.Add(cutOff);
        var cut = new Part(cutOff.Drawing);
        plate.Parts.Add(p);
        plate.Parts.Add(q);
        plate.Parts.Add(cut);
        return (nest, plate, p, q, cut);
    }

    // A regenerable 2x2 square with its emitted lead at the top-edge midpoint.
    private static Part Emitted(CuttingParameters parameters, Vector location)
    {
        var clean = LeadPathValidationTests.Rectangle(0, 0, 2, 2);
        var prepared = PreparedContours.Capture(clean, parameters);
        var part = new Part(new Drawing("same", clean), location);
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(1, 3))]), false));
        part.CuttingParameters = parameters;
        return part;
    }

    // Host: 10x10 square with circular cutouts of radius 1 at (3,3) and (7,3), cut hole, hole,
    // perimeter from the right so its own program has no completed-contour crossing.
    private static (Plate, Part Host, Part Inner) NestedPlate(Part inner)
    {
        var clean = PreparedContourTests.Holes();
        var parameters = ExplicitContourTests.Parameters();
        var prepared = PreparedContours.Capture(clean, parameters);
        var program = prepared.Emit(
        [
            prepared.Entry(0, 0, new Vector(4, 3)),
            prepared.Entry(1, 0, new Vector(8, 3)),
            prepared.ClosestEntry(2, new Vector(12, 3))
        ]);
        var host = new Part(new Drawing("same", clean));
        Assert.True(host.RestoreLeadInProgram(program, false));
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(host);
        plate.Parts.Add(inner);
        return (plate, host, inner);
    }

    // Prefixes a remote rapid and scribe mark to both the clean and placed programs.
    private static Part Marked(Part part)
    {
        static Program Prefix(Program source)
        {
            var program = new Program();
            program.Codes.Add(new RapidMove(-20, -20));
            program.Codes.Add(new LinearMove(-19, -20) { Layer = LayerType.Scribe });
            program.Codes.AddRange(source.Codes);
            return program;
        }

        var marked = new Part(new Drawing("same", Prefix(part.BaseDrawing.Program)), part.Location);
        Assert.True(marked.RestoreLeadInProgram(Prefix(part.Program), false));
        return marked;
    }

    // A U-shaped part open at the top between x 3 and 7, lead on its right edge.
    private static Part Pocketed()
    {
        var outline = new[] { (10.0, 0.0), (0.0, 0.0), (0.0, 10.0), (3.0, 10.0), (3.0, 3.0), (7.0, 3.0),
            (7.0, 10.0), (10.0, 10.0), (10.0, 5.0) };
        var clean = new Program();
        clean.MoveTo(10, 5);
        foreach (var (x, y) in outline)
            clean.LineTo(x, y);
        var placed = new Program();
        placed.MoveTo(10.25, 5);
        placed.Codes.Add(new LinearMove(10, 5) { Layer = LayerType.Leadin });
        foreach (var (x, y) in outline)
            placed.LineTo(x, y);
        placed.Codes.Add(new LinearMove(10.25, 5) { Layer = LayerType.Leadout });
        var part = new Part(new Drawing("same", clean));
        Assert.True(part.RestoreLeadInProgram(placed, false));
        return part;
    }

    // Fixed-program rectangle at (x, y), contour starting at its right-edge midpoint with a
    // 0.25 straight lead-in and lead-out to the right.
    private static Part Rectangle(double x, double y, double width, double height)
    {
        var clean = new Program();
        clean.MoveTo(width, height / 2);
        Contour(clean, width, height);
        var part = new Part(new Drawing("same", clean), new Vector(x, y));
        var placed = new Program();
        placed.MoveTo(width + 0.25, height / 2);
        placed.Codes.Add(new LinearMove(width, height / 2) { Layer = LayerType.Leadin });
        Contour(placed, width, height);
        placed.Codes.Add(new LinearMove(width + 0.25, height / 2) { Layer = LayerType.Leadout });
        Assert.True(part.RestoreLeadInProgram(placed, false));
        return part;
    }

    private static void Contour(Program program, double width, double height)
    {
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, height / 2);
    }

    private static string Describe(CuttingPlanResult r) =>
        $"{r.Status}, expanded {r.Expansions}: " + string.Join("; ", r.Findings.Select(f => f.Message));
}
