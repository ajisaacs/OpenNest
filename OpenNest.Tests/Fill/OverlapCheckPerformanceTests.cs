using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

/// <summary>
/// Opt-in measurements of the fill overlap checks. Uses only APIs that predate the overlap-check
/// work (82feb78), so the file can be byte-copied into a before tree for same-harness pairs.
/// </summary>
[Collection(nameof(FillCacheCollection))]
[Trait("Category", "FillPerformance")]
public class OverlapCheckPerformanceTests
{
    private readonly ITestOutputHelper output;

    public OverlapCheckPerformanceTests(ITestOutputHelper output) => this.output = output;

    [SkippableFact]
    public void OverlapCheck_ReportsPolygonPairsAndGridChecks()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("OPENNEST_RUN_FILL_PERF") == "1",
            "Set OPENNEST_RUN_FILL_PERF=1 to run opt-in fill microbenchmarks.");
        var area = new Box(3.1, 5.3, 96, 48);
        var filler = new FillLinear(area, 0.5);
        var arcGrid = filler.Fill(FillExtentsTests.MakeFixture("arc"), 0.37, NestDirection.Horizontal);
        var first = Part.CreateAtOrigin(FillExtentsTests.MakeFixture("concave"), 0);
        var second = Part.CreateAtOrigin(FillExtentsTests.MakeFixture("concave"), System.Math.PI);
        second.Offset(new Vector(first.Right + 0.5, first.Bottom));
        var pairGrid = filler.Fill(FillHelpers.BuildRotatedPattern(new List<Part> { first, second }, 0.37),
            NestDirection.Horizontal);
        Assert.False(FillHelpers.HasOverlappingParts(arcGrid));
        Assert.False(FillHelpers.HasOverlappingParts(pairGrid));
        var pairs = BoxOverlappingPairs(arcGrid).Concat(BoxOverlappingPairs(pairGrid)).ToArray();
        Assert.NotEmpty(pairs);
        Assert.All(pairs, p => Assert.False(Collision.HasOverlap(p.A, p.B)));

        var modes = new Func<long>[]
        {
            () =>
            {
                var hits = 0L;
                foreach (var (a, b) in pairs)
                    hits += Collision.HasOverlap(a, b) ? 1 : 0;
                return hits;
            },
            () => FillHelpers.HasOverlappingParts(arcGrid) ? 1 : 0,
            () => FillHelpers.HasOverlappingParts(pairGrid) ? 1 : 0,
        };
        var names = new[] { "polygon-pairs", "grid-single", "grid-pair" };
        var warmupCalls = 20;
        var callsPerBatch = 50;
        var repetitions = 7;
#if DEBUG
        output.WriteLine("Configuration=Debug (diagnostic only; use Release for measurements).");
#else
        output.WriteLine("Configuration=Release.");
#endif
        output.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; "
            + $"architecture={RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; "
            + $"Stopwatch.Frequency={Stopwatch.Frequency} ticks/s.");
        output.WriteLine(
            $"overlap-check: area=(3.1,5.3,96,48), spacing=0.5, Horizontal. grid-single = FillLinear.Fill(arc fixture, 0.37) "
            + $"({arcGrid.Count} parts, one shared Program); grid-pair = FillLinear.Fill(BuildRotatedPattern(concave 0/PI pair, 0.37)) "
            + $"({pairGrid.Count} parts); polygon-pairs = Collision.HasOverlap over the {pairs.Length} box-overlapping neighbour "
            + "pairs of both grids (timed per full sweep), each polygon prepared once up front with Part.Intersects' recipe "
            + "(material entities, chord 0.001, world offset); Part.Intersects itself prepares per call. All verdicts clear. "
            + $"warmup=2 x {warmupCalls} calls/mode; measured={repetitions} x {callsPerBatch}; mode order rotates per batch. "
            + "Setup/assertions/output excluded. Synchronous current-thread allocation bytes. Not a timing gate or whole-job estimate.");
        for (var batch = 0; batch < 2; batch++)
            for (var slot = 0; slot < modes.Length; slot++)
                Measure(modes[(batch + slot) % modes.Length], warmupCalls);
        var samples = names.Select(_ => new (double Ms, long Bytes)[repetitions]).ToArray();
        for (var batch = 0; batch < repetitions; batch++)
        {
            for (var slot = 0; slot < modes.Length; slot++)
            {
                var mode = (batch + slot) % modes.Length;
                samples[mode][batch] = Measure(modes[mode], callsPerBatch);
            }
            for (var mode = 0; mode < modes.Length; mode++)
            {
                var (ms, bytes) = samples[mode][batch];
                output.WriteLine(FormattableString.Invariant(
                    $"overlap-check mode={names[mode]} batch={batch + 1}: us/call={ms * 1000 / callsPerBatch:F3}; B/call={(double)bytes / callsPerBatch:F1}; ms={ms:F6}; bytes={bytes}."));
            }
        }
        for (var mode = 0; mode < modes.Length; mode++)
        {
            var times = samples[mode].Select(s => s.Ms * 1000 / callsPerBatch).OrderBy(x => x).ToArray();
            var bytes = samples[mode].Select(s => (double)s.Bytes / callsPerBatch).OrderBy(x => x).ToArray();
            output.WriteLine(FormattableString.Invariant(
                $"overlap-check {names[mode]}: us/call min/median/max={times[0]:F3}/{times[repetitions / 2]:F3}/{times[^1]:F3}; B/call min/median/max={bytes[0]:F1}/{bytes[repetitions / 2]:F1}/{bytes[^1]:F1}."));
        }
    }

    private static (double Ms, long Bytes) Measure(Func<long> run, int calls)
    {
        var hits = 0L;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < calls; i++)
            hits += run();
        var elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0, hits);
        return (elapsed * 1000.0 / Stopwatch.Frequency, allocated);
    }

    private static List<(Polygon A, Polygon B)> BoxOverlappingPairs(List<Part> parts)
    {
        var polygons = parts.Select(PreparedPolygon).ToArray();
        var pairs = new List<(Polygon, Polygon)>();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var b1 = parts[i].BoundingBox;
                var b2 = parts[j].BoundingBox;
                var overlapX = System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                var overlapY = System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);
                if (overlapX > OpenNest.Math.Tolerance.Epsilon && overlapY > OpenNest.Math.Tolerance.Epsilon)
                    pairs.Add((polygons[i], polygons[j]));
            }
        return pairs;
    }

    private static Polygon PreparedPolygon(Part part)
    {
        var entities = ConvertProgram.ToGeometry(part.Program).Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();
        var polygon = new ShapeProfile(entities).Perimeter.ToPolygonWithTolerance(0.001);
        polygon.Offset(part.Location);
        return polygon;
    }
}
