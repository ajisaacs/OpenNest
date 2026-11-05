using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

internal static class TestConfig
{
    private static readonly Lazy<Dictionary<string, string>> Config = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var path = Path.Combine(dir, "test-config.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        return new();
    });

    public static string? Get(string key) =>
        Config.Value.TryGetValue(key, out var val) ? val : null;

    public static string? GetExistingPath(string key)
    {
        var path = Get(key);
        return path != null && File.Exists(path) ? path : null;
    }
}

internal static class TestHelpers
{
    /// <summary>
    /// The report without its sheet-bound findings, for routing replays whose fixtures sit at the
    /// sheet origin with leads hanging off it. PlateEdgeReviewTests covers those checks.
    /// </summary>
    public static OpenNest.Diagnostics.PostVerificationReport RouteFindings(
        OpenNest.Diagnostics.PostVerificationReport report) =>
        new(report.Findings.Where(f => f.Kind is not (OpenNest.Diagnostics.PostVerificationKind.OffPlate
            or OpenNest.Diagnostics.PostVerificationKind.EdgeSpacing)));

    public static Part MakePartAt(double x, double y, double size = 1)
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    public static Plate MakePlate(double width = 60, double length = 120, params Part[] parts)
    {
        var plate = new Plate(width, length);
        foreach (var p in parts)
            plate.Parts.Add(p);
        return plate;
    }

    /// <summary>A cut-off's segment parts in cut-sequence order: each trimmed segment is its own part.</summary>
    public static Part[] CutOffParts(Plate plate, CutOff cutOff) =>
        plate.Parts.Where(p => ReferenceEquals(p.BaseDrawing, cutOff.Drawing)).ToArray();

    /// <summary>A cut-off's materialized cutting moves: its segment parts' programs joined in cut-sequence order.</summary>
    public static Program CutOffProgram(Plate plate, CutOff cutOff)
    {
        var program = new Program();
        foreach (var part in CutOffParts(plate, cutOff))
        {
            Assert.Equal(Vector.Zero, part.Location);
            program.Codes.AddRange(part.Program.Codes);
        }
        return program;
    }

    /// <summary>The number of parts the plate's cut-offs materialize, one per trimmed segment.</summary>
    public static int CutOffPartCount(Plate plate) => plate.CutOffs.Sum(c => c.SegmentPrograms().Count);

    /// <summary>A w x h rectangle from the origin, counterclockwise: +X first, then up.</summary>
    public static Drawing MakeRectDrawing(double w, double h, string name = "rect")
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing(name, pgm);
    }

    public static Drawing MakeSquareDrawing(double size = 10)
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("square", pgm);
    }

    /// <summary>A full circle of the given radius around (x, y), starting at its right.</summary>
    public static Program MakeCircleProgram(double x, double y, double radius)
    {
        var p = new Program();
        p.MoveTo(x + radius, y);
        p.ArcTo(x + radius, y, x, y, RotationType.CCW);
        return p;
    }

    /// <summary>A counterclockwise size x size square, lower-left corner at (x, y).</summary>
    public static Program MakeSquareProgram(double x, double y, double size)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.LineTo(x + size, y);
        program.LineTo(x + size, y + size);
        program.LineTo(x, y + size);
        program.LineTo(x, y);
        return program;
    }

    /// <summary>A width x height rectangle from the origin, clockwise: up first, then +X.</summary>
    public static Program MakeClockwiseRectProgram(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>A closed contour through the vertices in order, back to the first one.</summary>
    public static Program MakeClosedProgram(IEnumerable<Vector> vertices)
    {
        var p = new Program();
        p.MoveTo(vertices.First().X, vertices.First().Y);
        foreach (var v in vertices.Skip(1).Append(vertices.First()))
            p.LineTo(v.X, v.Y);
        return p;
    }

    /// <summary>
    /// Appends a clockwise width x height rectangle that starts and ends at the midpoint of its
    /// right edge, where <paramref name="program"/> must already be.
    /// </summary>
    public static void TraceRectangleFromRightMidpoint(Program program, double width, double height)
    {
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, height / 2);
    }

    /// <summary>A part of a 4 x 4 absolute-mode square drawing with the given name.</summary>
    public static Part MakeSquarePart(string name)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing(name, program));
    }

    /// <summary>A w x h outline whose two right-hand corners are arcs of radius r.</summary>
    public static Drawing MakeRoundedRectDrawing(double w, double h, double r)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w - r, 0)));
        pgm.Codes.Add(new ArcMove(new Vector(w, r), new Vector(w - r, r), RotationType.CW));
        pgm.Codes.Add(new LinearMove(new Vector(w, h - r)));
        pgm.Codes.Add(new ArcMove(new Vector(w - r, h), new Vector(w - r, h - r), RotationType.CW));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rounded-rect", pgm);
    }

    public static Drawing MakeFrameDrawing(double w = 20, double h = 6, double t = 0.5)
    {
        // Thin-walled hollow frame: outer CW, inner cutout CCW (CNC convention).
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        pgm.Codes.Add(new RapidMove(new Vector(t, t)));
        pgm.Codes.Add(new LinearMove(new Vector(w - t, t)));
        pgm.Codes.Add(new LinearMove(new Vector(w - t, h - t)));
        pgm.Codes.Add(new LinearMove(new Vector(t, h - t)));
        pgm.Codes.Add(new LinearMove(new Vector(t, t)));
        return new Drawing("frame", pgm);
    }

    public static Drawing MakeLShapeDrawing()
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("lshape", pgm);
    }
}
