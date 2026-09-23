using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The candidate validator flattens each placed contour once and reuses it across pairs. These
/// tests compare its accept/reject decision with a reference that rebuilds every contour and
/// checks every edge pair, on arc-heavy parts placed right around the spacing.
/// </summary>
public class NestJobSpacingValidationTests
{
    private const double Spacing = 0.25;
    private const double Epsilon = 0.0000001;
    private const double Origin = 50;

    public static IEnumerable<object[]> Shapes() =>
        new[]
        {
            new object[] { "rounded", new RoundedRectangleShape { Length = 12, Width = 6, Radius = 1.5 }.GetDrawing().Program },
            new object[] { "ring", new RingShape { OuterDiameter = 8, InnerDiameter = 3 }.GetDrawing().Program },
        };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void SpacingDecisionMatchesBruteForceNearTheLimit(string name, OpenNest.CNC.Program program)
    {
        var geometry = PartGeometrySnapshot.FromProgram(program);
        var part = new NestJobPart("part", geometry, 2);
        var job = new NestJob(
            new[] { part },
            new[] { new NestPlateStock("stock", new Size(200, 200), 1, Spacing) }
        );

        var bounds = program.BoundingBox();
        var random = new Random(name.GetHashCode(StringComparison.Ordinal) & 0x7fff);
        var accepted = 0;
        var rejected = 0;

        for (var sample = 0; sample < 60; sample++)
        {
            // Second part beside or above the first with a gap near the spacing, shifted
            // sideways so corners and arcs meet at varied angles, and sometimes turned.
            var gap = Spacing + (random.NextDouble() - 0.5) * 0.06;
            var rotation = sample % 3 == 0 ? System.Math.PI : sample % 3 == 1 ? 0.0 : 0.05;
            var shift = (random.NextDouble() - 0.5) * 0.5 * bounds.Width;
            var beside = sample % 2 == 0;
            var second = beside
                ? new NestJobPlacement("part", 1, Origin + bounds.Length + gap, Origin + shift, rotation)
                : new NestJobPlacement("part", 1, Origin + shift, Origin + bounds.Width + gap, rotation);
            var first = new NestJobPlacement("part", 0, Origin, Origin, 0);

            var expectedValid = !ReferenceViolates(geometry, first, second);
            var actualValid = IsValid(job, first, second);

            Assert.True(
                expectedValid == actualValid,
                $"{name} sample {sample}: gap {gap:F5}, shift {shift:F4}, rotation {rotation}: "
                    + $"brute force {(expectedValid ? "accepts" : "rejects")}, validator {(actualValid ? "accepts" : "rejects")}"
            );

            if (actualValid)
                accepted++;
            else
                rejected++;
        }

        // The sampling has to exercise both outcomes to mean anything.
        Assert.True(accepted > 5, $"only {accepted} accepted");
        Assert.True(rejected > 5, $"only {rejected} rejected");
    }

    [Fact]
    public void RingsPlacedExactlyAtSpacingPass()
    {
        var program = new RingShape { OuterDiameter = 8, InnerDiameter = 3 }.GetDrawing().Program;
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 2);
        var job = new NestJob(
            new[] { part },
            new[] { new NestPlateStock("stock", new Size(200, 200), 1, Spacing) }
        );

        // Inscribed chords never bring arcs closer, so an exact-spacing layout stays valid.
        Assert.True(IsValid(
            job,
            new NestJobPlacement("part", 0, Origin, Origin, 0),
            new NestJobPlacement("part", 1, Origin + 8 + Spacing, Origin, 0)
        ));
        Assert.False(IsValid(
            job,
            new NestJobPlacement("part", 0, Origin, Origin, 0),
            new NestJobPlacement("part", 1, Origin + 8 + Spacing - 0.01, Origin, 0)
        ));
    }

    private static bool IsValid(NestJob job, params NestJobPlacement[] placements)
    {
        try
        {
            new NestJobRunner(_ => new CandidateNester(placements)).Solve(job);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ReferenceViolates(
        PartGeometrySnapshot geometry,
        NestJobPlacement first,
        NestJobPlacement second
    )
    {
        var a = Contours(geometry, first);
        var b = Contours(geometry, second);

        if (Collision.HasOverlap(a[0], b[0], a.Skip(1).ToList(), b.Skip(1).ToList()))
            return true;

        var limit = Spacing - Epsilon;
        foreach (var left in a)
        {
            var leftLines = left.ToLines();
            foreach (var right in b)
            {
                var rightLines = right.ToLines();
                foreach (var l in leftLines)
                foreach (var r in rightLines)
                {
                    if (l.Intersects(r))
                        return true;
                    var d = System.Math.Min(
                        System.Math.Min(
                            l.ClosestPointTo(r.StartPoint).DistanceTo(r.StartPoint),
                            l.ClosestPointTo(r.EndPoint).DistanceTo(r.EndPoint)
                        ),
                        System.Math.Min(
                            r.ClosestPointTo(l.StartPoint).DistanceTo(l.StartPoint),
                            r.ClosestPointTo(l.EndPoint).DistanceTo(l.EndPoint)
                        )
                    );
                    if (d < limit)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>Perimeter polygon first, then cutouts, placed the way the validator places them.</summary>
    private static List<Polygon> Contours(PartGeometrySnapshot geometry, NestJobPlacement placement)
    {
        var entities = ConvertProgram
            .ToGeometry(DrawingJobMapper.ToProgram(geometry))
            .Where(e => !ReferenceEquals(e.Layer, SpecialLayers.Rapid))
            .ToList();
        var profile = new ShapeProfile(entities);
        profile.NormalizeWinding();

        return new[] { profile.Perimeter }
            .Concat(profile.Cutouts)
            .Select(shape =>
            {
                var contour = (Shape)shape.Clone();
                contour.Rotate(placement.Rotation);
                contour.Offset(placement.X, placement.Y);
                return contour.ToPolygonWithTolerance(0.001);
            })
            .ToList();
    }

    private sealed class CandidateNester(IEnumerable<NestJobPlacement> placements) : IPlateNester
    {
        public PlateCandidate Place(
            PlatePlacementRequest request,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) => new(placements);
    }
}
