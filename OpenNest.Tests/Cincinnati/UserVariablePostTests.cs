using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class UserVariablePostTests
{
    [Fact]
    public void UserVariables_EmittedInDeclarationSubprogram()
    {
        var output = PostNestWithVariables("width = 48.0\nG90\nG01X$widthY0");

        Assert.Contains("#200=48", output);
        Assert.Contains("WIDTH", output.ToUpper());
    }

    [Fact]
    public void UserVariables_InlineVariable_NotEmittedAsNumbered()
    {
        var output = PostNestWithVariables("kerf = 0.06 inline\nG90\nG01X1Y0");

        Assert.DoesNotContain("#200", output);
    }

    [Fact]
    public void UserVariables_CoordinateUsesNumberedVariable()
    {
        var output = PostNestWithVariables("width = 48.0\nG90\nG01X$widthY0");

        Assert.Contains("X#200", output);
    }

    [Fact]
    public void UserVariables_InlineVariable_CoordinateUsesLiteral()
    {
        var output = PostNestWithVariables("kerf = 0.06 inline\nG90\nG01X$kerfY0");

        Assert.Contains("X0.06", output);
        // G1 coordinate lines should not use X#nnn variable references for inline vars
        var g1Lines = output.Split('\n').Where(l => l.TrimStart().StartsWith("G1 ")).ToList();
        Assert.All(g1Lines, line => Assert.DoesNotContain("X#", line));
    }

    [Fact]
    public void UserVariables_GlobalVariables_SharedAcrossDrawings()
    {
        var pgm1 = ParseProgram("sheet_width = 48.0 global\nG90\nG01X$sheet_widthY0");
        var pgm2 = ParseProgram("sheet_width = 48.0 global\nG90\nG01X$sheet_widthY0");

        var drawing1 = new Drawing("Part1", pgm1);
        var drawing2 = new Drawing("Part2", pgm2);
        var nest = new Nest { Name = "Test" };
        nest.Drawings.Add(drawing1);
        nest.Drawings.Add(drawing2);
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(drawing1, new Vector(0, 0)));
        plate.Parts.Add(new Part(drawing2, new Vector(50, 0)));
        nest.Plates.Add(plate);

        var config = new CincinnatiPostConfig { UserVariableStart = 200 };
        var post = new CincinnatiPostProcessor(config);
        var output = PostToString(post, nest);

        // Both should use the same #200 — only one declaration
        var declarationCount = output.Split('\n')
            .Count(l => l.Contains("#200=") && l.ToUpper().Contains("SHEET WIDTH"));
        Assert.Equal(1, declarationCount);
    }

    [Fact]
    public void UserVariables_LocalVariables_GetSeparateNumbers()
    {
        var pgm1 = ParseProgram("diameter = 0.3\nG90\nG01X$diameterY0");
        var pgm2 = ParseProgram("diameter = 0.5\nG90\nG01X$diameterY0");

        var drawing1 = new Drawing("TubeA", pgm1);
        var drawing2 = new Drawing("TubeB", pgm2);
        var nest = new Nest { Name = "Test" };
        nest.Drawings.Add(drawing1);
        nest.Drawings.Add(drawing2);
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(drawing1, new Vector(0, 0)));
        plate.Parts.Add(new Part(drawing2, new Vector(50, 0)));
        nest.Plates.Add(plate);

        var config = new CincinnatiPostConfig { UserVariableStart = 200 };
        var post = new CincinnatiPostProcessor(config);
        var output = PostToString(post, nest);

        // Two separate declarations with different numbers
        Assert.Contains("#200=0.3", output);
        Assert.Contains("#201=0.5", output);
        Assert.Contains("TUBE A", output.ToUpper());
        Assert.Contains("TUBE B", output.ToUpper());
    }

    [Fact]
    public void UserVariables_StartNumberConfigurable()
    {
        var config = new CincinnatiPostConfig { UserVariableStart = 300 };
        var output = PostNestWithVariables("width = 48.0\nG90\nG01X$widthY0", config);

        Assert.Contains("#300=48", output);
    }

    private static string PostNestWithVariables(string gcode, CincinnatiPostConfig config = null)
    {
        var program = ParseProgram(gcode);
        var drawing = new Drawing("TestPart", program);
        var nest = new Nest { Name = "Test" };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);

        config ??= new CincinnatiPostConfig { UserVariableStart = 200 };
        var post = new CincinnatiPostProcessor(config);
        return PostToString(post, nest);
    }

    private static string PostToString(CincinnatiPostProcessor post, Nest nest)
    {
        var ms = new MemoryStream();
        post.Post(nest, ms);
        ms.Position = 0;
        return new StreamReader(ms).ReadToEnd();
    }

    private static Program ParseProgram(string gcode)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(gcode));
        var reader = new ProgramReader(stream);
        var program = reader.Read();
        reader.Close();
        return program;
    }
}
