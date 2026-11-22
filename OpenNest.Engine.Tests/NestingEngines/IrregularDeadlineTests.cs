using System.Diagnostics;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Irregular under <see cref="DeadlineNestingEngine"/>: it stops soon after the limit. A dense
/// sheet of detailed parts once held it in the free-rectangle search for Lattice blocks, which
/// neither checked the token nor scaled, for many minutes past a 30 s limit.
/// </summary>
public class IrregularDeadlineTests
{
    /// <summary>A gear: <paramref name="teeth"/> square teeth on a disc, with four square holes.</summary>
    private static Program Gear(double radius, int teeth)
    {
        var program = new Program();
        var outline = new List<(double X, double Y)>();
        var root = radius * 0.85;
        for (var i = 0; i < teeth; i++)
        {
            var a = 2 * System.Math.PI * i / teeth;
            var step = 2 * System.Math.PI / teeth / 4;
            outline.Add(Polar(root, a));
            outline.Add(Polar(radius, a + step));
            outline.Add(Polar(radius, a + 2 * step));
            outline.Add(Polar(root, a + 3 * step));
        }
        Contour(program, outline);
        var hole = radius * 0.15;
        foreach (var (cx, cy) in new[] { (0.4, 0.0), (-0.4, 0.0), (0.0, 0.4), (0.0, -0.4) })
        {
            var x = radius + cx * radius;
            var y = radius + cy * radius;
            Contour(program, new[] { (x - hole, y - hole), (x - hole, y + hole), (x + hole, y + hole), (x + hole, y - hole) });
        }
        return program;

        (double X, double Y) Polar(double r, double angle) =>
            (radius + r * System.Math.Cos(angle), radius + r * System.Math.Sin(angle));
    }

    private static void Contour(Program program, IReadOnlyList<(double X, double Y)> points)
    {
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1))
            program.Codes.Add(new LinearMove(x, y));
        program.Codes.Add(new LinearMove(points[0].X, points[0].Y));
    }

    /// <summary>A bar with a serrated long edge (<paramref name="teeth"/> teeth) and four square holes.</summary>
    private static Program Serrated(double length, double width, int teeth)
    {
        var program = new Program();
        var outline = new List<(double X, double Y)> { (0, 0), (length, 0), (length, width) };
        var pitch = length / teeth;
        for (var i = teeth - 1; i >= 0; i--)
        {
            outline.Add((i * pitch + pitch * 0.75, width));
            outline.Add((i * pitch + pitch * 0.5, width - pitch * 0.4));
            outline.Add((i * pitch + pitch * 0.25, width));
        }
        outline.Add((0, width));
        Contour(program, outline);
        var hole = width * 0.12;
        foreach (var fx in new[] { 0.15, 0.4, 0.6, 0.85 })
        {
            var x = fx * length;
            var y = width * 0.4;
            Contour(program, new[] { (x - hole, y - hole), (x - hole, y + hole), (x + hole, y + hole), (x + hole, y - hole) });
        }
        return program;
    }

    [Fact]
    public void StopsSoonAfterTheLimitOnADenseJobOfDetailedParts()
    {
        // About 450 bars a sheet; a sheet and a half of them.
        var part = Part("bar", Serrated(5, 1.75, 24), 680, RotationPolicy.Automatic);
        var stock = Stock("sheet", 60, 120, spacing: 0.25, edge: new Spacing(0.25, 0.75, 0.25, 0.25));
        var job = Job(new[] { part }, new[] { stock });

        var limit = TimeSpan.FromSeconds(3);
        var engine = new DeadlineNestingEngine(new IrregularNestingEngine(), limit);
        var watch = Stopwatch.StartNew();
        NestJobResult? result = null;
        try
        {
            result = engine.Solve(job);
        }
        catch (TimeoutException)
        {
            // No valid plan by the limit is an answer too; the point is how soon it comes.
        }
        watch.Stop();

        Assert.True(watch.Elapsed < limit + TimeSpan.FromSeconds(3),
            $"The solve took {watch.Elapsed.TotalSeconds:0.0} s against a {limit.TotalSeconds:0} s limit.");
        if (result != null)
            LayoutAssert.Valid(job, result);
    }

    /// <summary>
    /// The frame that held a 30 s solve for over 1,000 s in the flag sweep: a sheet fill offering
    /// Lattice blocks looks for the free rectangles beside hundreds of detailed parts already on
    /// the sheet. Uncancelled, the free region alone takes about 20 s here.
    /// </summary>
    [Fact]
    public void BlockPreparationBesideACrowdedSheetStopsWhenCancelled()
    {
        var stock = Stock("sheet", 60, 120, spacing: 0.25);
        var job = Job(new[] { Part("gear", Gear(1.25, 24), 400, RotationPolicy.Fixed(0)) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var orientation = types[0].Orientations[0];
        var seated = new List<Placed>();
        for (var x = 0.0; x + 2.75 <= 120; x += 2.75)
            for (var y = 0.0; y + 2.75 <= 50; y += 2.75)
                seated.Add(new Placed(orientation, x, y));
        var pairs = new Dictionary<int, IReadOnlyList<PairPose>>();
        using var blocks = new BlockCatalog(0.25, types, pairs, new WorkCounter(), long.MaxValue);
        var packer = new FrontierPacker(types, new NoFitCache(0.25), pairs, stock, PackAxis.X, 1,
            new WorkCounter(), blocks);
        // Seating the parts takes under a second; the limit falls in the block preparation after it.
        var limit = TimeSpan.FromSeconds(2);
        using var cancel = new CancellationTokenSource(limit);
        var watch = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => packer.Fill(new[] { 400 }, cancel.Token, seated));
        Assert.True(watch.Elapsed < limit + TimeSpan.FromSeconds(2), $"Took {watch.Elapsed.TotalSeconds:0.0} s.");
    }
}
