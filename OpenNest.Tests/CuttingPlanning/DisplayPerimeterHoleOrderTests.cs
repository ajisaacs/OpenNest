using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

// A supplied blank's holes are its whole program. On an instrument panel, mounting holes either
// side of a cutout become unreachable from each other once the cutout is cut: a straight rapid
// between them would cross it. The planner has to find a hole order that avoids that for a door
// (no cut perimeter) as it does for an ordinary part.
public class DisplayPerimeterHoleOrderTests
{
    private const double Width = 36.5, Height = 75;

    internal static Program Door(bool mirrored)
    {
        double X(double x) => mirrored ? Width - x : x;
        var p = new Program();
        var turn = mirrored ? RotationType.CW : RotationType.CCW;
        // A rounded rectangle, corner radius 0.5, traced counterclockwise before mirroring.
        void Cutout(double x0, double y0, double x1, double y1)
        {
            const double r = 0.5;
            Vector V(double x, double y) => new(X(x), y);
            p.MoveTo(X(x0 + r), y0);
            p.LineTo(V(x1 - r, y0)); p.ArcTo(V(x1, y0 + r), V(x1 - r, y0 + r), turn);
            p.LineTo(V(x1, y1 - r)); p.ArcTo(V(x1 - r, y1), V(x1 - r, y1 - r), turn);
            p.LineTo(V(x0 + r, y1)); p.ArcTo(V(x0, y1 - r), V(x0 + r, y1 - r), turn);
            p.LineTo(V(x0, y0 + r)); p.ArcTo(V(x0 + r, y0), V(x0 + r, y0 + r), turn);
        }
        void Hole(double x, double y)
        {
            var c = new Vector(X(x), y);
            p.MoveTo(c.X + 0.1385, y);
            p.ArcTo(new Vector(c.X - 0.1385, y), c, RotationType.CCW);
            p.ArcTo(new Vector(c.X + 0.1385, y), c, RotationType.CCW);
        }
        // Two columns of 0.277 mounting holes either side of two stacked rounded cutouts.
        Hole(13.4, 19.75); Hole(23, 19.8);
        Hole(13.4, 22.92); Hole(23, 22.87);
        Hole(13.4, 26.07); Hole(23, 26.02);
        Hole(13.4, 32.03); Hole(23, 32.03);
        Hole(13.4, 38.45); Hole(23, 38.45);
        Cutout(14.64, 16, 20.48, 25.5);
        Cutout(14.92, 26.66, 21.65, 35.35);
        p.MoveTo(0, 0);
        foreach (var (x, y) in new[] { (0.0, Height), (Width, Height), (Width, 0.0), (0.0, 0.0) })
            p.Codes.Add(new LinearMove(x, y) { Layer = LayerType.DisplayPerimeter });
        return p;
    }

    // Control: with the default budget the plan is Ready and applies, either way round.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelDoor_PlansAReadyRouteAndApplies(bool mirrored)
    {
        var plate = new Plate(new Size(Width, Height));
        plate.Parts.Add(new Part(new Drawing("door", Door(mirrored)), Vector.Zero));

        var proposal = CuttingPlanBatch.Capture([plate], ImportedArcHoleTests.SweepParameters(), false).Plan();

        var result = Assert.Single(proposal.Plates).Result;
        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: false).Status);
    }

    // The contour solver learns "cut this hole first" from a hop that crosses holes already cut,
    // so a door alone on its sheet finds its hole order well within a budget the hole-by-hole
    // search alone exhausts (a display perimeter is never cut: the holes are the whole part).
    [Fact]
    public void DoorHoleOrder_IsLearnedWithinABudgetTheHoleSearchAloneExhausts()
    {
        var result = PlanAlone(LayerType.DisplayPerimeter, 1000);

        Assert.True(result.Status == CuttingPlanStatus.Ready, $"{result.Status} after {result.Expansions} expansions");
        Assert.True(result.IndependentlyReplayed);
    }

    // Any other part alone on its plate keeps the plain hole search, which plans this panel in a
    // small budget with a shorter route than contour recovery would.
    [Fact]
    public void OrdinaryPanelAlone_KeepsThePlainHoleSearch()
    {
        var result = PlanAlone(LayerType.Cut, 200);

        Assert.True(result.Status == CuttingPlanStatus.Ready, $"{result.Status} after {result.Expansions} expansions");
        Assert.True(result.IndependentlyReplayed);
    }

    private static CuttingPlanResult PlanAlone(LayerType outline, int budget)
    {
        var program = Door(false);
        foreach (var code in program.Codes.OfType<LinearMove>().Where(m => m.Layer == LayerType.DisplayPerimeter))
            code.Layer = outline;
        var plate = new Plate(new Size(Width, Height));
        plate.Parts.Add(new Part(new Drawing("panel", program), Vector.Zero));
        return CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, expansionBudget: budget,
            confirmedParameters: ImportedArcHoleTests.SweepParameters()));
    }
}
