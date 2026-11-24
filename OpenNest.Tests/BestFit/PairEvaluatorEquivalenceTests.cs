using System.Collections.Concurrent;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

/// <summary>
/// <see cref="PairEvaluator"/> no longer builds two parts and their geometry for every
/// candidate. These tests compare it bit for bit against the frozen pre-change
/// <see cref="LegacyPairEvaluator"/> (commit 24f70fc3): the same results in the same order,
/// read from the same overlap polygons.
/// </summary>
public class PairEvaluatorEquivalenceTests
{
    public static IEnumerable<object[]> Shapes => Drawings.Keys.Select(name => new object[] { name });

    private static readonly Dictionary<string, Func<Drawing>> Drawings = new()
    {
        // Arcs at every corner.
        ["rounded rectangle"] = () => new RoundedRectangleShape { Length = 6, Width = 3.5, Radius = 0.75 }.GetDrawing(),

        // Concave outlines whose pairs interlock.
        ["tee"] = () => new TShape { Width = 10, Height = 8 }.GetDrawing(),
        ["ell"] = () => new LShape { Width = 7, Height = 9, LegWidth = 2, LegHeight = 2.5 }.GetDrawing(),
        ["notched arcs"] = NotchedArcs,

        // Holes, which the perimeter leaves out.
        ["ring"] = () => new RingShape { OuterDiameter = 6, InnerDiameter = 3 }.GetDrawing(),
        ["flange"] = () => new PipeFlangeShape
        {
            OD = 8,
            HoleDiameter = 0.75,
            HolePatternDiameter = 6,
            HoleCount = 6,
            PipeClearance = 3,
        }.GetDrawing(),

        // A full circle and a bolt-circle outline: tangent contacts at every angle.
        ["disc"] = () => new CircleShape { Diameter = 5 }.GetDrawing(),
        ["hexagon"] = () => new NgonShape { Sides = 6, Width = 4 }.GetDrawing(),

        // Segments shorter than the chain tolerance, where chaining is ambiguous.
        ["tiny segments"] = TinySegments,

        // An outline that is not axis aligned and starts away from the origin.
        ["turned tee"] = () =>
        {
            var drawing = new TShape { Width = 10, Height = 8 }.GetDrawing();
            drawing.Program.Rotate(Angle.ToRadians(23), new Vector(4, 1));
            drawing.ProgramChanged();
            return drawing;
        },

        // An incremental source program: the perimeter is rebuilt from its geometry.
        ["incremental"] = () =>
        {
            var drawing = NotchedArcs();
            drawing.Program.Mode = Mode.Incremental;
            drawing.ProgramChanged();
            return drawing;
        },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EvaluateAll_MatchesLegacyBitwise(string shape)
    {
        var drawing = CanonicalFrame.AsCanonicalCopy(Drawings[shape]());
        var candidates = Candidates(drawing);

        var polygons = new ConcurrentDictionary<PairCandidate, (Polygon First, Polygon Second)>();
        var legacy = new LegacyPairEvaluator
        {
            Polygons = (candidate, first, second) => polygons[candidate] = (first, second),
        };

        var expected = legacy.EvaluateAll(candidates);
        var evaluator = new PairEvaluator();
        var actual = evaluator.EvaluateAll(candidates);

        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(candidates[i], actual[i].Candidate);
            Assert.True(ResultBits(expected[i]).SequenceEqual(ResultBits(actual[i])), $"{shape}: result {i}");
            Assert.Equal(expected[i].Reason, actual[i].Reason);

            var (first, second) = evaluator.OverlapPolygons(candidates[i]);
            Assert.True(PolygonBits(polygons[candidates[i]].First).SequenceEqual(PolygonBits(first)), $"{shape}: first polygon {i}");
            Assert.True(PolygonBits(polygons[candidates[i]].Second).SequenceEqual(PolygonBits(second)), $"{shape}: second polygon {i}");

            // The single-candidate entry point takes the same path.
            if (i % 7 == 0)
                Assert.True(ResultBits(expected[i]).SequenceEqual(ResultBits(evaluator.Evaluate(candidates[i]))), $"{shape}: single {i}");
        }

        // The comparison is only worth something when it sees both verdicts and both the
        // turned and the unturned overlap check.
        Assert.Contains(expected, r => r.Keep);
        Assert.Contains(expected, r => !r.Keep);
        Assert.Contains(expected, r => r.OptimalRotation.IsEqualTo(0));
        Assert.Contains(expected, r => !r.OptimalRotation.IsEqualTo(0));
    }

    [Fact]
    public void EvaluateAll_NoCandidates_IsEmpty()
    {
        Assert.Empty(new PairEvaluator().EvaluateAll(new List<PairCandidate>()));
    }

    /// <summary>
    /// The candidates the best-fit search slides into contact, touching (no spacing) and
    /// spaced, plus pairs it would never propose: coincident, barely overlapping, exactly
    /// tangent along a straight side, and far apart.
    /// </summary>
    private static List<PairCandidate> Candidates(Drawing drawing)
    {
        var candidates = new List<PairCandidate>();
        var index = 1;

        foreach (var spacing in new[] { 0.0, 0.25 })
        {
            foreach (var degrees in new[] { 0.0, 90, 180, 270, 37, 211.5 })
            {
                var strategy = new RotationSlideStrategy(
                    Angle.ToRadians(degrees),
                    index++,
                    string.Empty,
                    new CpuDistanceComputer()
                );
                candidates.AddRange(strategy.GenerateCandidates(drawing, spacing, 0.5));
            }
        }

        var box = Part.CreateAtOrigin(drawing).BoundingBox;
        var offsets = new[]
        {
            new Vector(0, 0),
            new Vector(box.Length * 0.5, box.Width * 0.25),
            new Vector(box.Length - 1e-4, 0),
            new Vector(box.Length, 0),
            new Vector(0, box.Width),
            new Vector(box.Length + 1e-7, 0.125),
            new Vector(box.Length * 3, box.Width * 2),
            new Vector(-box.Length * 0.25, box.Width * 0.9),
        };

        foreach (var rotation in new[] { 0, System.Math.PI, Angle.HalfPI, Angle.ToRadians(12.5) })
        {
            foreach (var offset in offsets)
            {
                candidates.Add(new PairCandidate
                {
                    Drawing = drawing,
                    Part1Rotation = 0,
                    Part2Rotation = rotation,
                    Part2Offset = offset,
                    StrategyIndex = index,
                    Spacing = 0,
                });
            }

            index++;
        }

        return candidates;
    }

    /// <summary>A plate with a square notch, a round notch cut clockwise and two rounded corners.</summary>
    private static Drawing NotchedArcs()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 0);
        program.LineTo(3, 1.5);
        program.LineTo(4.25, 1.5);
        program.LineTo(4.25, 0);
        program.LineTo(8, 0);
        program.LineTo(8, 4);
        program.ArcTo(7, 5, 7, 4, RotationType.CCW);
        program.LineTo(5.5, 5);
        program.ArcTo(3.5, 5, 4.5, 5, RotationType.CW);
        program.LineTo(1, 5);
        program.ArcTo(0, 4, 1, 4, RotationType.CCW);
        program.LineTo(0, 0);
        return new Drawing("notched arcs", program);
    }

    /// <summary>
    /// A rectangle whose top edge is a run of segments shorter than the chain tolerance, as a
    /// curve exported as a dense polyline has.
    /// </summary>
    private static Drawing TinySegments()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 2);

        for (var i = 1; i <= 40; i++)
            program.LineTo(4 - i * 0.00004, 2 + System.Math.Sin(i * 0.3) * 0.00002);

        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return new Drawing("tiny segments", program);
    }

    private static long[] ResultBits(BestFitResult result)
    {
        var values = new List<double>
        {
            result.RotatedArea,
            result.BoundingWidth,
            result.BoundingHeight,
            result.OptimalRotation,
            result.TrueArea,
            result.Keep ? 1 : 0,
            result.HullAngles == null ? -1 : result.HullAngles.Count,
        };

        if (result.HullAngles != null)
            values.AddRange(result.HullAngles);

        return values.Select(BitConverter.DoubleToInt64Bits).ToArray();
    }

    private static long[] PolygonBits(Polygon polygon)
    {
        if (polygon == null)
            return new long[] { -1 };

        var box = polygon.BoundingBox;

        return polygon.Vertices
            .SelectMany(v => new[] { v.X, v.Y })
            .Concat(new[] { box.X, box.Y, box.Length, box.Width })
            .Select(BitConverter.DoubleToInt64Bits)
            .ToArray();
    }
}
