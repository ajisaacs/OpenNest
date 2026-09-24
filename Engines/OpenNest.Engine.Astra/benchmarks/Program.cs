using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using OpenNest;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using OpenNest.Benchmark;
using CncProgram = OpenNest.CNC.Program;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Contains("--diagnose-ring"))
{
    var ringPart = new NestJobPart("ring", PartGeometrySnapshot.FromProgram(
        new OpenNest.Shapes.RingShape { OuterDiameter = 10, InnerDiameter = 7 }.GetDrawing().Program), 1);
    var insertPart = new NestJobPart("insert", PartGeometrySnapshot.FromProgram(
        new OpenNest.Shapes.CircleShape { Diameter = 6 }.GetDrawing().Program), 1);
    foreach (var quadrant in new[] { 1, 2 })
    foreach (var offset in new[] { 0.0, 0.0003, 0.05, 0.1 })
    {
        var s = new NestPlateStock("s", new Size(24, 42), 1, 0.175,
            new Spacing(0.2, 0.3, 0.4, 0.5), quadrant);
        var j = new NestJob(new[] { ringPart, insertPart }, new[] { s });
        var x = (quadrant == 1 ? 0 : -42) + s.EdgeSpacing.Left + 5;
        var y = s.EdgeSpacing.Bottom + 5;
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, s, new[] { new NestJobPlacement("ring", 0, x, y, 0),
                new NestJobPlacement("insert", 0, x + offset, y, 0) }) },
            new[] { new PartFulfillment("ring", 1, 1, 0), new PartFulfillment("insert", 1, 1, 0) },
            new[] { new StockUsage("s", 1, 0) });
        var materialized = NestResultMaterializer.Materialize(j, result);
        var check = NestValidator.Validate(materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList(),
            j.Parts.ToDictionary(p => materialized.DrawingsByPartId[p.Id], p => (p.Id, p.Quantity)));
        Console.WriteLine($"q={quadrant} offset={offset} valid={check.Valid}: {string.Join(';', check.Violations)}");
    }
    return;
}
var assembly = args.Length > 0 && args[0].EndsWith(".dll")
    ? new AssemblyLoadContext("benchmark-plugin", isCollectible: true).LoadFromAssemblyPath(Path.GetFullPath(args[0])) : Assembly.Load("OpenNest.Engine.Astra");
var engine = (INestingEngine)Activator.CreateInstance(assembly.GetType("OpenNest.Engine.Astra.AstraNestingEngine")!);
var cases = new List<(string Name, NestJob Job)>();
var standard = new[] { new NestPlateStock("small", new Size(24, 48), partSpacing: 0.15),
    new NestPlateStock("large", new Size(48, 96), partSpacing: 0.15) };
NestJobPart Part(string name, CncProgram p, int count, RotationPolicy rotation = null) =>
    new(name, PartGeometrySnapshot.FromProgram(p), count, rotation: rotation);
CncProgram Polygon(params double[] xy)
{
    var p = new CncProgram(); p.MoveTo(xy[0], xy[1]);
    for (var i = 2; i < xy.Length; i += 2) p.LineTo(xy[i], xy[i + 1]);
    p.LineTo(xy[0], xy[1]); return p;
}
CncProgram Rect(double w, double h) => Polygon(0, 0, w, 0, w, h, 0, h);
CncProgram Circle(double r) { var p = new CncProgram(); p.MoveTo(r, 0); p.ArcTo(r, 0, 0, 0, RotationType.CCW); return p; }
void Add(string name, NestJobPart[] parts, NestPlateStock[] stocks = null, NestJobOptions options = null) =>
    cases.Add((name, new NestJob(parts, stocks ?? standard, options)));
Add("triangles", new[] { Part("triangle", Polygon(0, 0, 10, 0, 0, 10), 60) });
Add("circles", new[] { Part("circle", Circle(2.5), 90) });
Add("circles-dense", new[] { Part("circle", Circle(2.5), 80) });
Add("concave-L", new[] { Part("L", Polygon(0, 0, 8, 0, 8, 2, 2, 2, 2, 8, 0, 8), 60) });
Add("mixed", new[] { Part("rect", Rect(9, 4), 30), Part("triangle", Polygon(0, 0, 8, 0, 3, 6), 25),
    Part("circle", Circle(2), 30), Part("L", Polygon(0, 0, 7, 0, 7, 2, 2, 2, 2, 6, 0, 6), 20) });
var ring = Rect(10, 10); ring.MoveTo(1, 1); ring.LineTo(1, 9); ring.LineTo(9, 9); ring.LineTo(9, 1); ring.LineTo(1, 1);
Add("holes", new[] { Part("frame", ring, 8), Part("insert", Rect(7, 7), 8) });
Add("rectangles", Enumerable.Range(0, 8).Select(i => Part($"r{i}", Rect(2 + i, 3 + i % 3), 12)).ToArray());
Add("grain", new[] { Part("fixed", Rect(13, 3), 20, RotationPolicy.Fixed(System.Math.PI / 6)),
    Part("sweep", Rect(7, 2), 40, RotationPolicy.BoundedSweep(0, System.Math.PI / 2, System.Math.PI / 4)) });
Add("scarce-stock", new[] { Part("small", Rect(4, 4), 8), Part("large", Rect(10, 10), 1) },
    new[] { new NestPlateStock("scarce", new Size(10, 10), 1), new NestPlateStock("small-only", new Size(4, 8)) });
Add("tail", new[] { Part("rect", Rect(6, 4), 17) }, new[] {
    new NestPlateStock("small", new Size(8, 12), partSpacing: 0.1), new NestPlateStock("large", new Size(20, 30), partSpacing: 0.1) });
Add("plate-cap", new[] { Part("r", Rect(5, 5), 10) }, new[] {
    new NestPlateStock("small", new Size(10, 10)), new NestPlateStock("large", new Size(20, 20)) }, new NestJobOptions(maxPlates: 1));
cases.AddRange(OpenNest.Engine.Astra.Benchmarks.GeneratedCases.Create());
Console.WriteLine("case,valid,placed,requested,sheets,area,milliseconds");
foreach (var (name, job) in cases)
{
    if (args.Length > 1 && !name.Contains(args[1], StringComparison.OrdinalIgnoreCase)) continue;
    var sw = Stopwatch.StartNew();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    try
    {
        var result = engine.Solve(job, token: cts.Token); sw.Stop();
        var nest = NestResultMaterializer.Materialize(job, result);
        var validation = NestValidator.Validate(nest.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList(),
            job.Parts.ToDictionary(p => nest.DrawingsByPartId[p.Id], p => (p.Id, p.Quantity)));
        NestValidator.ValidateAgainstJob(job, result, job.Parts.ToDictionary(p => p.Id, p => p.Id), validation);
        if (!validation.Valid) Environment.ExitCode = 1;
        Console.WriteLine($"{name},{validation.Valid},{result.Fulfillment.Sum(f => f.Placed)},{job.Parts.Sum(p => p.Quantity)},{result.Plates.Count},{result.Plates.Sum(p => p.Stock.Size.Length * p.Stock.Size.Width)},{sw.ElapsedMilliseconds}");
        foreach (var violation in validation.Violations.Take(4)) Console.Error.WriteLine($"{name}: {violation}");
    }
    catch (Exception ex) { Environment.ExitCode = 1; Console.WriteLine($"{name},ERROR,,,,,{sw.ElapsedMilliseconds}"); Console.Error.WriteLine(ex); }
}
