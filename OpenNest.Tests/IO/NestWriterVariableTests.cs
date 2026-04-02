using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class NestWriterVariableTests
{
    [Fact]
    public void RoundTrip_VariableDefinitions_Preserved()
    {
        var nest = CreateNestWithVariableProgram(
            "width = 48.0 global\ndiameter = 0.3\nG90\nG01X$widthY$diameter");

        var loaded = RoundTrip(nest);
        var pgm = loaded.Drawings.First().Program;

        Assert.Equal(2, pgm.Variables.Count);
        Assert.Equal(48.0, pgm.Variables["width"].Value);
        Assert.True(pgm.Variables["width"].Global);
        Assert.Equal(0.3, pgm.Variables["diameter"].Value);
        Assert.False(pgm.Variables["diameter"].Global);
    }

    [Fact]
    public void RoundTrip_VariableRefs_Preserved()
    {
        var nest = CreateNestWithVariableProgram(
            "width = 48.0\nG90\nG01X$widthY0");

        var loaded = RoundTrip(nest);
        var pgm = loaded.Drawings.First().Program;

        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(48.0, linear.EndPoint.X);
        Assert.NotNull(linear.VariableRefs);
        Assert.Equal("width", linear.VariableRefs["X"]);
    }

    [Fact]
    public void RoundTrip_InlineFlag_Preserved()
    {
        var nest = CreateNestWithVariableProgram(
            "kerf = 0.06 inline\nG90\nG01X1Y0");

        var loaded = RoundTrip(nest);
        var pgm = loaded.Drawings.First().Program;

        Assert.True(pgm.Variables["kerf"].Inline);
    }

    [Fact]
    public void RoundTrip_NoVariables_WorksAsNormal()
    {
        var nest = CreateNestWithVariableProgram("G90\nG01X1Y2");

        var loaded = RoundTrip(nest);
        var pgm = loaded.Drawings.First().Program;

        Assert.Empty(pgm.Variables);
        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(1.0, linear.EndPoint.X);
    }

    private static Nest CreateNestWithVariableProgram(string gcode)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(gcode));
        var reader = new ProgramReader(stream);
        var program = reader.Read();
        reader.Close();

        var drawing = new Drawing("TestPart", program);
        var nest = new Nest { Name = "Test" };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static Nest RoundTrip(Nest nest)
    {
        var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        ms.Position = 0;
        return new NestReader(ms).Read();
    }
}
