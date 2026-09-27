using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.CincinnatiCIFiber;

/// <summary>
/// Output-contract tests for the Cincinnati CI Fiber post, checked against the
/// structure of the machine sample NC (12992-4SS_NEST.nc): header block,
/// restart jump, N-labelled contour blocks with /L macro lines, G41/G42
/// selection, trimmed coordinate format and the M50/M30/% tail.
/// </summary>
public class CIFiberPostProcessorTests
{
    private static CIFiberPostConfig MakeConfig() =>
        new() { ConfigurationName = "CI FIBER 8K" };

    private static string Post(Nest nest, CIFiberPostConfig? config = null)
    {
        var post = new CIFiberPostProcessor(config ?? MakeConfig());
        using var ms = new MemoryStream();
        post.Post(nest, ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// 10x10 CW square perimeter (closed loop, no leading rapid) at the plate
    /// location, with one CCW full-circle hole posted as an incremental
    /// SubProgramCall at (5,5) — the exact shape OpenNest nests produce.
    /// </summary>
    private static Nest MakeSquareWithHoleNest(Vector? partLocation = null)
    {
        var location = partLocation ?? new Vector(1.0, 2.0);

        var pgm = new Program(Mode.Absolute);

        // Hole sub-program: CCW full circle r=0.5 centred on the call offset,
        // linear lead-in from below, left absolute then flipped to
        // incremental — exactly what ContourCuttingStrategy emits for a hole.
        var hole = new Program(Mode.Absolute);
        hole.Codes.Add(new RapidMove(new Vector(0, -0.6)));
        hole.Codes.Add(new LinearMove(new Vector(0, -0.5)) { Layer = LayerType.Leadin });
        hole.Codes.Add(
            new ArcMove(new Vector(0, -0.5), new Vector(0, 0), RotationType.CCW)
            {
                Layer = LayerType.Cut,
            }
        );
        hole.Mode = Mode.Incremental;
        pgm.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(5, 5) });

        // Perimeter: linear lead-in into a CW loop starting at (0,0).
        pgm.Codes.Add(new RapidMove(new Vector(0, -0.5)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)) { Layer = LayerType.Leadin });
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));

        var drawing = new Drawing("square-hole", pgm);
        var part = new Part(drawing, location);

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);

        var nest = new Nest("Test Nest") { Thickness = 0.06, Material = new Material("Mild Steel") };
        nest.Plates.Add(plate);
        return nest;
    }

    private static List<string> Lines(string output) =>
        output.Replace("\r\n", "\n").Split('\n').ToList();

    [Fact]
    public void Post_Structure_GoldenSquareWithHole()
    {
        var output = Post(MakeSquareWithHoleNest());
        var lines = Lines(output);

        // File ends with % and uses CRLF throughout, no BOM.
        Assert.StartsWith("(", output);
        Assert.EndsWith("%\r\n", output);
        foreach (var raw in output.Split('\n').SkipLast(1))
            Assert.EndsWith("\r", raw);

        // Header block
        Assert.Equal("( Test Nest )", lines[0]);
        Assert.Equal("( CONFIGURATION - CI FIBER 8K )", lines[1]);
        Assert.Matches(@"^\( \w+ \d+, \d{4}   \d\d:\d\d (AM|PM) \)$", lines[2]);
        Assert.Equal("( Material = Mild Steel .060 )", lines[3]);
        Assert.Equal("V.E.MATERIAL = \"MSN\"", lines[4]);
        Assert.Equal("V.E.THICKNESS = 0.060", lines[5]);
        Assert.Equal("V.E.X_SIZE = 120.000", lines[6]);
        Assert.Equal("V.E.Y_SIZE = 60.000", lines[7]);
        Assert.Equal("V.E.UNIT = 1", lines[8]);
        Assert.DoesNotContain("V.E.SHEET_WEIGHT", output);

        // Restart preamble
        Assert.Equal("G90", lines[9]);
        Assert.Equal("L PROGRAMSTART.NC", lines[10]);
        Assert.Equal("P3=V.E.R3", lines[11]);
        Assert.Equal("$GOTO NP3:", lines[12]);
        Assert.Equal("N0:", lines[13]);
        Assert.Equal("( Sheet number - 1 )", lines[14]);

        // Part block
        Assert.Equal("( Part #1 )", lines[15]);
        Assert.Equal("( PART:square-hole )", lines[16]);
        Assert.Equal("V.E.R4=1", lines[17]);

        // Contour 1 = the flattened hole (part program order).
        Assert.Equal("N1:", lines[18]);
        Assert.Equal("/L \"L0\"", lines[19]);
        Assert.Equal("V.E.R3=1", lines[20]);
        Assert.Equal("G0X6Y6.4", lines[21]); // pierce = part(1,2) + hole(5,5) + (0,-0.6)
        Assert.Equal("/L \"L2\"", lines[22]);
        Assert.Equal("G41", lines[23]);
        Assert.Equal("G1X6Y6.5", lines[24]); // contour start = hole centre + (0,-0.5)
        Assert.Equal("/L \"L6\"", lines[25]);
        Assert.Equal("G3X6Y6.5I0J0.5", lines[26]); // I/J incremental from arc start
        Assert.Equal("/L \"ZHSOFF\"", lines[27]);

        // Contour 2 = perimeter.
        Assert.Equal("N2:", lines[28]);
        Assert.Equal("/L \"L0\"", lines[29]);
        Assert.Equal("V.E.R3=2", lines[30]);
        Assert.Equal("G0X1Y1.5", lines[31]);
        Assert.Equal("/L \"L4\"", lines[32]);
        Assert.Equal("G42", lines[33]);
        Assert.Equal("G1X1Y2", lines[34]);
        Assert.Equal("/L \"L6\"", lines[35]);
        Assert.Equal("G1X1Y12", lines[36]);
        Assert.Equal("G1X11Y12", lines[37]);
        Assert.Equal("G1X11Y2", lines[38]);
        Assert.Equal("G1X1Y2", lines[39]);
        Assert.Equal("/L \"ZHSOFF\"", lines[40]);

        // Part end and tail
        Assert.Equal("( PART END )", lines[41]);
        Assert.Equal("/L \"L0\"", lines[42]);
        Assert.Equal("L PROGRAMEND.NC", lines[43]);
        Assert.Equal("M50", lines[44]);
        Assert.Equal("M30", lines[45]);
        Assert.Equal("%", lines[46]);
    }

    [Fact]
    public void Post_FlattensRotatedPartHole_ToSheetCoords()
    {
        // Same nest, but the part is rotated 90 degrees CCW about the origin:
        // (x,y) -> (-y,x). Location (1,2) -> (-2,1); hole offset (5,5) -> (-5,5).
        var nest = MakeSquareWithHoleNest();
        var part = nest.Plates[0].Parts[0];
        part.Rotate(OpenNest.Math.Angle.ToRadians(90));

        var output = Post(nest);
        var lines = Lines(output);

        // Hole pierce local (0,-0.6) -> (0.6,0): (-2,1)+(-5,5)+(0.6,0) = (-6.4,6).
        Assert.Equal("G0X-6.4Y6", lines[21]);
        // Contour start local (0,-0.5) -> (0.5,0) = (-6.5,6).
        Assert.Equal("G1X-6.5Y6", lines[24]);
        // Full CCW circle, centre local (0,0.5) from the start -> (-0.5,0):
        // centre at (-7,6), so I=-0.5 J=0.
        Assert.Equal("G3X-6.5Y6I-0.5J0", lines[26]);
        // Perimeter corner local (0,10) -> (-10,0): pierce (-2,1)+(0.5,0)...
        // pierce local (0,-0.5)->(0.5,0) = (-1.5,1); corner (-10,1)... check
        // first cut G1 target = lead-in end = (-2,1).
        Assert.Equal("G0X-1.5Y1", lines[31]);
        Assert.Equal("G1X-2Y1", lines[34]);
        Assert.Equal("G1X-12Y1", lines[36]);
    }

    [Fact]
    public void Post_CoordinateFormat_TrimsZerosAndNeverNegativeZero()
    {
        // Part placed so a coordinate lands on -0.0004 (rounds to -0) and on
        // values whose 3-decimal representation keeps trailing digits.
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(new Vector(-0.0004, 3.5009)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 3.5009)) { Layer = LayerType.Leadin });
        pgm.Codes.Add(new LinearMove(new Vector(5, 4)) { Layer = LayerType.Cut });
        pgm.Codes.Add(new LinearMove(new Vector(-0.0004, 4)) { Layer = LayerType.Cut });
        pgm.Codes.Add(new LinearMove(new Vector(-0.0004, 3.5009)) { Layer = LayerType.Cut });

        var drawing = new Drawing("fmt", pgm);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, Vector.Zero));
        var nest = new Nest("fmt") { Thickness = 0.06, Material = new Material("Mild Steel") };
        nest.Plates.Add(plate);

        var output = Post(nest);

        Assert.Contains("G0X0Y3.501", output); // -0.0004 -> "0" not "-0"; 3.5009 -> 3.501
        Assert.Contains("G1X5Y3.501", output);
        Assert.DoesNotContain("-0Y", output);
        Assert.DoesNotContain("-0\r", output);
    }

    [Fact]
    public void Post_RejectsArcLeadIn_PerTf5200LinearFirstRule()
    {
        // A contour whose first move after the comp selection is an arc must
        // be rejected (TF5200 13.2.4.1: first motion block after selection
        // must be LINEAR).
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new ArcMove(0, 0, 0, 0.5, RotationType.CCW) { Layer = LayerType.Cut });

        var drawing = new Drawing("arcdirect", pgm);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, Vector.Zero));
        var nest = new Nest("arc") { Thickness = 0.06, Material = new Material("Mild Steel") };
        nest.Plates.Add(plate);

        var ex = Assert.Throws<InvalidOperationException>(() => Post(nest));
        Assert.Contains("13.2.4.1", ex.Message);
    }

    [Fact]
    public void Post_ValidatesTableSize()
    {
        var nest = MakeSquareWithHoleNest();

        var tight = MakeConfig();
        tight.MaxTableX = 100; // plate length is 120
        Assert.Contains("exceeds maximum table X", Assert.Throws<InvalidOperationException>(() => Post(nest, tight)).Message);

        var wide = MakeConfig();
        wide.MaxTableX = 160.25;
        wide.MaxTableY = 81.25;
        Post(nest, wide); // does not throw
    }

    [Fact]
    public void Post_SkipsSuppressedAndScribeMoves()
    {
        var pgm = new Program(Mode.Absolute);

        // Suppressed contour (every move suppressed, as suppression marks a
        // whole feature) must not appear.
        pgm.Codes.Add(new RapidMove(new Vector(30, 30)) { Suppressed = true });
        pgm.Codes.Add(
            new LinearMove(new Vector(31, 30)) { Layer = LayerType.Leadin, Suppressed = true }
        );
        pgm.Codes.Add(
            new LinearMove(new Vector(31, 31)) { Layer = LayerType.Cut, Suppressed = true }
        );

        // Scribe contour skipped by default.
        pgm.Codes.Add(new RapidMove(new Vector(40, 40)));
        pgm.Codes.Add(new LinearMove(new Vector(41, 40)) { Layer = LayerType.Leadin });
        pgm.Codes.Add(new LinearMove(new Vector(41, 41)) { Layer = LayerType.Scribe });

        // Cutting contour that must appear.
        pgm.Codes.Add(new RapidMove(new Vector(50, 50)));
        pgm.Codes.Add(new LinearMove(new Vector(51, 50)) { Layer = LayerType.Leadin });
        pgm.Codes.Add(new LinearMove(new Vector(51, 51)) { Layer = LayerType.Cut });
        pgm.Codes.Add(new LinearMove(new Vector(50, 51)) { Layer = LayerType.Cut });
        pgm.Codes.Add(new LinearMove(new Vector(50, 50)) { Layer = LayerType.Cut });

        var drawing = new Drawing("layers", pgm);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, Vector.Zero));
        var nest = new Nest("layers") { Thickness = 0.06, Material = new Material("Mild Steel") };
        nest.Plates.Add(plate);

        var output = Post(nest);
        var lines = Lines(output);

        // Exactly one contour survived (N0: is the restart-jump label).
        Assert.Single(lines.Where(l => l.StartsWith("N") && l != "N0:" && l.EndsWith(":")));
        Assert.DoesNotContain("X30Y30", output);
        Assert.DoesNotContain("X40Y40", output);
        Assert.Contains("G0X50Y50", output);
        Assert.Contains("V.E.R3=1", output);
    }
}
