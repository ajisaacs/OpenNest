using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit.Abstractions;

namespace OpenNest.Tests.BestFit;

/// <summary>
/// A crescent written the way a converted drawing is: coordinates rounded to 1e-6, the disc
/// split into two arcs away from a quadrant point, and the program origin outside the part,
/// so the program opens with a rapid across free space to the contour. That rapid is not
/// material. A spacing oracle that flattens every program entity measures from it and reports
/// pairs up to its length (0.2) too close, which is how this shape was once reported as a
/// slide defect. Measured on material alone, every pair the search builds keeps the spacing.
/// </summary>
public class CrescentPairSpacingTests
{
    private const double Spacing = 0.313;
    private const double HalfSpacing = Spacing / 2;
    private const double DiscRadius = 5.9687;
    private const double BiteRadius = 3.3437;
    private const double BiteDrop = 2.9245;

    // Disc center in program coordinates: the origin is 0.2 outside the disc.
    private const double CenterX = 4.81058;
    private const double CenterY = 3.861579;

    // Where the disc is split, as an angle from its center.
    private const double SplitDegrees = 218.7549;

    private const double ChordTolerance = 0.0001;

    private readonly ITestOutputHelper _output;

    public CrescentPairSpacingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Drawing Crescent()
    {
        var tipY =
            (BiteRadius * BiteRadius - DiscRadius * DiscRadius - BiteDrop * BiteDrop)
            / (2 * BiteDrop);
        var tipX = System.Math.Sqrt(DiscRadius * DiscRadius - tipY * tipY);
        var split = Angle.ToRadians(SplitDegrees);

        var start = Rounded(
            CenterX + DiscRadius * System.Math.Cos(split),
            CenterY + DiscRadius * System.Math.Sin(split)
        );
        var rightTip = Rounded(CenterX + tipX, CenterY + tipY);
        var leftTip = Rounded(CenterX - tipX, CenterY + tipY);
        var center = new Vector(CenterX, CenterY);
        var biteCenter = Rounded(CenterX, CenterY - BiteDrop);

        var program = new Program();
        program.MoveTo(start);
        program.ArcTo(rightTip, center, RotationType.CW);
        program.ArcTo(leftTip, biteCenter, RotationType.CCW);
        program.ArcTo(start, center, RotationType.CW);
        return new Drawing("crescent", program);
    }

    private static Vector Rounded(double x, double y) =>
        new Vector(System.Math.Round(x, 6), System.Math.Round(y, 6));

    /// <summary>
    /// Two crescents hooked together, each with a horn in the other's bite. The first contact
    /// is a horn's round spacing cap against the outside of the other disc: a small convex arc
    /// against a large convex one, away from every vertex.
    /// </summary>
    [Fact]
    public void HookedCrescents_StopWhereAHornCapMeetsTheOtherDisc()
    {
        var drawing = Crescent();
        var part1 = Part.CreateAtOrigin(drawing);
        var template = Part.CreateAtOrigin(drawing, System.Math.PI);
        var stationary = PartGeometry.GetOffsetPerimeterEntities(part1, HalfSpacing);
        var moving = PartGeometry.GetOffsetPerimeterEntities(template, HalfSpacing);
        var offset = new SlideOffset(-3, -24, 0, 1);

        var distance = new CpuDistanceComputer().ComputeDistances(
            stationary,
            moving,
            new[] { offset }
        )[0];

        // External tangency of the stationary part's right-hand cap and the moving disc:
        // their centers end one cap radius plus one disc radius apart.
        var cap = stationary
            .OfType<Arc>()
            .Where(a => System.Math.Abs(a.Radius - HalfSpacing) < 1e-9)
            .OrderByDescending(a => a.Center.X)
            .First();
        var disc = moving.OfType<Arc>().OrderByDescending(a => a.Radius).First();
        var reach = cap.Radius + disc.Radius;
        var across = cap.Center.X - (disc.Center.X + offset.Dx);
        var expected =
            cap.Center.Y
            - System.Math.Sqrt(reach * reach - across * across)
            - (disc.Center.Y + offset.Dy);

        _output.WriteLine($"slide {distance:R}, cap/disc tangency {expected:R}");
        Assert.Equal(expected, distance, 5);

        var part2 = Part.CreateAtOrigin(drawing, System.Math.PI);
        part2.Location = new Vector(
            template.Location.X + offset.Dx,
            template.Location.Y + offset.Dy + distance
        );

        var gap = Gap(MaterialOutline(part1), MaterialOutline(part2));
        Assert.InRange(gap, Spacing - SpacingOutline.SpacingSlack, Spacing + 2 * ChordTolerance);

        // The same pair measured with the opening rapid counted as outline: the reading that
        // was mistaken for a slide stopping inside the spacing.
        var withRapid = Gap(EveryEntityOutline(part1), EveryEntityOutline(part2));
        _output.WriteLine($"material gap {gap:F5}, gap with the rapid counted {withRapid:F5}");
        Assert.True(withRapid < Spacing - 0.1, $"the rapid no longer sticks out: {withRapid:F5}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(54.5)]
    [InlineData(109.38)]
    [InlineData(180)]
    [InlineData(234.5)]
    public void EveryCandidateKeepsTheSpacing(double part2Degrees)
    {
        var drawing = Crescent();
        var candidates = new RotationSlideStrategy(
            Angle.ToRadians(part2Degrees),
            1,
            "crescent",
            new CpuDistanceComputer()
        ).GenerateCandidates(drawing, Spacing, stepSize: 0.25);

        Assert.NotEmpty(candidates);

        var outline1 = MaterialOutline(Part.CreateAtOrigin(drawing));
        var worst = double.MaxValue;

        foreach (var candidate in candidates)
        {
            var part2 = Part.CreateAtOrigin(drawing, candidate.Part2Rotation);
            part2.Location = candidate.Part2Offset;
            worst = System.Math.Min(worst, Gap(outline1, MaterialOutline(part2)));
        }

        _output.WriteLine($"{candidates.Count} candidates, smallest gap {worst:F5}");
        Assert.True(
            worst >= Spacing - SpacingOutline.SpacingSlack,
            $"a pair is {worst:F5} apart, under the {Spacing} spacing"
        );
    }

    /// <summary>The part's material outline as fine chords, independent of the slide solver.</summary>
    private static List<Vector> MaterialOutline(Part part) =>
        Flatten(
            part,
            ConvertProgram.ToGeometry(part.Program).Where(e => SpecialLayers.IsMaterial(e.Layer))
        );

    private static List<Vector> EveryEntityOutline(Part part) =>
        Flatten(part, ConvertProgram.ToGeometry(part.Program));

    private static List<Vector> Flatten(Part part, IEnumerable<Entity> entities)
    {
        var shape = ShapeBuilder.GetShapes(entities.ToList())[0];
        var polygon = shape.ToPolygonWithTolerance(ChordTolerance);
        polygon.Offset(part.Location);
        return polygon.Vertices;
    }

    private static double Gap(List<Vector> a, List<Vector> b)
    {
        var gap = double.MaxValue;

        foreach (var point in a)
            for (var i = 0; i + 1 < b.Count; i++)
                gap = System.Math.Min(gap, Distance(point, b[i], b[i + 1]));

        foreach (var point in b)
            for (var i = 0; i + 1 < a.Count; i++)
                gap = System.Math.Min(gap, Distance(point, a[i], a[i + 1]));

        return gap;
    }

    private static double Distance(Vector point, Vector from, Vector to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t =
            lengthSquared < 1e-18
                ? 0
                : System.Math.Clamp(
                    ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared,
                    0,
                    1
                );
        var x = from.X + t * dx - point.X;
        var y = from.Y + t * dy - point.Y;
        return System.Math.Sqrt(x * x + y * y);
    }
}
