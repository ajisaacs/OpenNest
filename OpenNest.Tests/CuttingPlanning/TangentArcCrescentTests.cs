using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

// N26-2JJ: a crescent imported from a DWG, an r 5 outer arc and an r 4.2819 inner arc whose
// circle is internally tangent to the outer one (their centers are the radii's difference apart),
// joined by two r 0.125 corner fillets. Rotated 180 degrees, roundoff let the circle-circle query
// pass its containment test with r^2 - a^2 slightly negative, so the tangent point came back NaN,
// material capture refused the part as "nonfinite program coordinates", and Plan Cutting fell back
// to a best-effort plan. The same drawing saved and reopened (its program rounded to 10 decimals)
// planned Ready, so only the live, unrounded drawing failed.
public class TangentArcCrescentTests
{
    // The incremental program CadImporter built from the DWG, bit for bit (G91; centers relative
    // to each arc's start). Saving rounds it to 10 decimals, which hides the failure.
    private static Drawing Crescent()
    {
        var program = new Program(Mode.Incremental);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new ArcMove(-0.1894982776402756, 0.16240295780501235,
            -0.08997074872414146, 0.08677709590679505, RotationType.CW));
        program.Codes.Add(new ArcMove(-6.818663342650833, 0,
            -3.4093316713254147, 2.5905761847873965, RotationType.CCW));
        program.Codes.Add(new ArcMove(-0.1894982776402756, -0.16240295780501235,
            -0.09952752891613414, -0.0756258618982173, RotationType.CW));
        program.Codes.Add(new ArcMove(7.197659897931384, -3.552713678800501E-15,
            3.598829948965694, 3.4710838362718306, RotationType.CW));
        return new Drawing("Crescent", program);
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 },
        ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 60 },
        InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 },
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0.0625
    };

    [Theory]
    [InlineData(0, 8.848829948965689, 2.699876137818065)]
    [InlineData(System.Math.PI, 6.776175051034306, 18.518801312399)]
    public void TangentInnerArc_PlansReadyAtEitherRotation(double rotation, double x, double y)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(60, 120);
        var part = new Part(Crescent());
        part.Rotate(rotation);
        part.Offset(new Vector(x, y));
        plate.Parts.Add(part);

        var proposal = CuttingPlanBatch.Capture([plate], Parameters(), preservePartOrder: false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.IsReady, string.Join("\n", proposal.Describe("in")));
    }

    // The rotated crescent's two supporting circles as material capture builds them. Queried with
    // the outer circle first, r^2 - a^2 is -1.07e-14: the tangent point, never NaN.
    [Fact]
    public void InternallyTangentCircles_GiveTheFiniteTangentPointEitherWay()
    {
        var outer = new Circle(new Vector(10.375004999999996, 15.047717476127165), 4.999999999999998);
        var inner = new Circle(new Vector(10.375004999999996, 15.765822169806588), 4.281895306320575);
        var tangent = new Vector(outer.Center.X, outer.Center.Y + outer.Radius);

        foreach (var (first, second) in new[] { (outer, inner), (inner, outer) })
        {
            Assert.True(first.Intersects(second, out var points));
            var point = Assert.Single(points);
            Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y));
            Assert.True(point.DistanceTo(tangent) < 1e-9, $"{point} is not the tangent point");
        }
    }
}
