using OpenNest;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Shapes;
using CncProgram = OpenNest.CNC.Program;
using M = System.Math;

namespace OpenNest.Engine.Astra.Benchmarks;

internal static class GeneratedCases
{
    internal static IEnumerable<(string Name, NestJob Job)> Create()
    {
        var library = new ShapeDefinition[] {
            new RoundedRectangleShape { Length = 11, Width = 5, Radius = 1.8 },
            new TShape { Width = 10, Height = 9, StemWidth = 2, BarHeight = 2 },
            new TrapezoidShape { BottomWidth = 10, TopWidth = 3, Height = 6 },
            new NgonShape { Sides = 5, Width = 6 },
            new NgonShape { Sides = 6, Width = 6 },
            new RingShape { OuterDiameter = 10, InnerDiameter = 7 },
            new PipeFlangeShape { OD = 7.5, HoleDiameter = 0.875, HolePatternDiameter = 5.5,
                HoleCount = 8, PipeSize = "2", PipeClearance = 0.0625 }
        };
        for (var i = 0; i < library.Length; i++)
            yield return ($"generated-library-{i}-{library[i].Name}", Job(new[] {
                Part("main", library[i].GetDrawing().Program, i == 6 ? 12 : 24)
            }, i));
        yield return ("generated-ring-inserts", Job(new[] {
            Part("ring", library[5].GetDrawing().Program, 8),
            Part("insert", new CircleShape { Diameter = 6 }.GetDrawing().Program, 8)
        }, 1));
        var curvedC = new CncProgram();
        curvedC.MoveTo(6, 0); curvedC.ArcTo(0, -6, 0, 0, RotationType.CCW);
        curvedC.LineTo(0, -4); curvedC.ArcTo(4, 0, 0, 0, RotationType.CW); curvedC.LineTo(6, 0);
        yield return ("generated-curved-C", Job(new[] { Part("C", curvedC, 16) }, 2));
        yield return ("generated-narrow-U", Job(new[] { Part("U", Poly(0, 0, 10, 0, 10, 10,
            8.5, 10, 8.5, 1.5, 1.5, 1.5, 1.5, 10, 0, 10), 24) }, 3));
        yield return ("generated-stars", Job(new[] { Part("star", Star(7, 6, 2.5), 20) }, 0));
        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(19073 + seed);
            var parts = new List<NestJobPart>();
            for (var p = 0; p < 4; p++)
            {
                CncProgram program;
                if (p == 0) program = new RoundedRectangleShape { Length = 5 + random.NextDouble() * 7,
                    Width = 3 + random.NextDouble() * 3, Radius = 0.7 }.GetDrawing().Program;
                else if (p == 1) program = Star(5 + seed % 3, 3 + random.NextDouble() * 2, 1.5 + random.NextDouble());
                else if (p == 2) program = new TrapezoidShape { BottomWidth = 5 + random.NextDouble() * 5,
                    TopWidth = 2 + random.NextDouble() * 2, Height = 3 + random.NextDouble() * 4 }.GetDrawing().Program;
                else program = new NgonShape { Sides = 3 + seed % 5, Width = 3 + random.NextDouble() * 3 }.GetDrawing().Program;
                // Nonzero source origins exercise pose reconstruction as well as shape packing.
                program.Offset(new Vector(seed * 1.37 - 5, p * 2.13 - 3));
                var rotation = p == 2 ? RotationPolicy.Fixed((seed % 4) * M.PI / 7) :
                    p == 3 ? RotationPolicy.BoundedSweep(-M.PI / 3, M.PI / 2, M.PI / 6, true) : RotationPolicy.Automatic;
                parts.Add(Part($"p{p}", program, random.Next(3, 9), rotation));
            }
            yield return ($"generated-seed-{seed:00}", Job(parts.ToArray(), seed));
        }
    }

    private static NestJob Job(NestJobPart[] parts, int seed) => new(parts, new[] {
        new NestPlateStock("small", new Size(23 + seed % 3, 41 + seed % 5), 2,
            seed % 4 == 0 ? 0 : 0.1 + seed % 3 * 0.075, new Spacing(0.2, 0.3, 0.4, 0.5), seed % 4 + 1),
        new NestPlateStock("large", new Size(47, 83), partSpacing: 0.2,
            edgeSpacing: new Spacing(0.3, 0.2, 0.5, 0.4), quadrant: seed % 4 + 1)
    });

    private static NestJobPart Part(string name, CncProgram p, int quantity, RotationPolicy rotation = null) =>
        new(name, PartGeometrySnapshot.FromProgram(p), quantity, rotation: rotation);

    private static CncProgram Star(int arms, double outer, double inner)
    {
        var coordinates = Enumerable.Range(0, arms * 2).SelectMany(i => {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = i * M.PI / arms;
            return new[] { radius * M.Cos(angle), radius * M.Sin(angle) };
        }).ToArray();
        return Poly(coordinates);
    }

    private static CncProgram Poly(params double[] points)
    {
        var p = new CncProgram(); p.MoveTo(points[0], points[1]);
        for (var i = 2; i < points.Length; i += 2) p.LineTo(points[i], points[i + 1]);
        p.LineTo(points[0], points[1]); return p;
    }
}
