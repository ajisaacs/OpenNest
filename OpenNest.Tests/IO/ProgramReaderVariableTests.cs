using System.IO;
using System.Text;
using OpenNest.CNC;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class ProgramReaderVariableTests
{
    private Program Parse(string gcode)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(gcode));
        var reader = new ProgramReader(stream);
        var program = reader.Read();
        reader.Close();
        return program;
    }

    [Fact]
    public void Parse_SimpleVariable_StoredInVariables()
    {
        var pgm = Parse("diameter = 0.3\nG90\nG01X1Y0");
        Assert.True(pgm.Variables.ContainsKey("diameter"));
        Assert.Equal(0.3, pgm.Variables["diameter"].Value);
        Assert.Equal("0.3", pgm.Variables["diameter"].Expression);
    }

    [Fact]
    public void Parse_VariableWithInlineFlag()
    {
        var pgm = Parse("kerf = 0.06 inline\nG90\nG01X1Y0");
        Assert.True(pgm.Variables["kerf"].Inline);
        Assert.False(pgm.Variables["kerf"].Global);
    }

    [Fact]
    public void Parse_VariableWithGlobalFlag()
    {
        var pgm = Parse("sheet_width = 48.0 global\nG90\nG01X1Y0");
        Assert.True(pgm.Variables["sheet_width"].Global);
        Assert.False(pgm.Variables["sheet_width"].Inline);
    }

    [Fact]
    public void Parse_VariableWithBothFlags()
    {
        var pgm = Parse("speed = 200 global inline\nG90\nG01X1Y0");
        Assert.True(pgm.Variables["speed"].Global);
        Assert.True(pgm.Variables["speed"].Inline);
    }

    [Fact]
    public void Parse_VariableReference_SubstitutedInCoordinate()
    {
        var pgm = Parse("width = 48.0\nG90\nG01X$widthY0");
        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(48.0, linear.EndPoint.X);
        Assert.Equal(0.0, linear.EndPoint.Y);
    }

    [Fact]
    public void Parse_VariableReference_TrackedInVariableRefs()
    {
        var pgm = Parse("width = 48.0\nG90\nG01X$widthY0");
        var linear = (LinearMove)pgm.Codes[0];
        Assert.NotNull(linear.VariableRefs);
        Assert.Equal("width", linear.VariableRefs["X"]);
        Assert.False(linear.VariableRefs.ContainsKey("Y"));
    }

    [Fact]
    public void Parse_VariableExpression_WithReference()
    {
        var pgm = Parse("diameter = 0.6\nradius = $diameter / 2\nG90\nG02X1Y0I$radiusJ0");
        Assert.Equal(0.3, pgm.Variables["radius"].Value, 10);
        var arc = (ArcMove)pgm.Codes[0];
        Assert.Equal(0.3, arc.CenterPoint.X, 10);
        Assert.Equal("radius", arc.VariableRefs["I"]);
    }

    [Fact]
    public void Parse_FeedVariable_TrackedOnFeedrate()
    {
        var pgm = Parse("speed = 100\nG90\nF$speed\nG01X1Y0");
        var feedrate = (Feedrate)pgm.Codes[0];
        Assert.Equal(100.0, feedrate.Value);
        Assert.Equal("speed", feedrate.VariableRef);
    }

    [Fact]
    public void Parse_VariablesCollectedInPrepass_OrderIndependent()
    {
        var pgm = Parse("radius = $diameter / 2\ndiameter = 0.6\nG90\nG01X$radiusY0");
        Assert.Equal(0.3, pgm.Variables["radius"].Value, 10);
        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(0.3, linear.EndPoint.X, 10);
    }

    [Fact]
    public void Parse_NoVariables_WorksAsNormal()
    {
        var pgm = Parse("G90\nG01X1.5Y2.5");
        Assert.Empty(pgm.Variables);
        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(1.5, linear.EndPoint.X);
        Assert.Null(linear.VariableRefs);
    }

    [Fact]
    public void Parse_RapidMove_WithVariableRef()
    {
        var pgm = Parse("start_x = 5.0\nG90\nG00X$start_xY0");
        var rapid = (RapidMove)pgm.Codes[0];
        Assert.Equal(5.0, rapid.EndPoint.X);
        Assert.Equal("start_x", rapid.VariableRefs["X"]);
    }

    [Fact]
    public void Parse_ArcMove_VariableOnMultipleAxes()
    {
        var pgm = Parse("r = 0.5\nG90\nG03X1Y0I$rJ$r");
        var arc = (ArcMove)pgm.Codes[0];
        Assert.Equal(0.5, arc.CenterPoint.X);
        Assert.Equal(0.5, arc.CenterPoint.Y);
        Assert.Equal("r", arc.VariableRefs["I"]);
        Assert.Equal("r", arc.VariableRefs["J"]);
    }

    [Fact]
    public void Parse_CaseInsensitive_VariableReference()
    {
        var pgm = Parse("Diameter = 0.3\nG90\nG01X$diameterY0");
        var linear = (LinearMove)pgm.Codes[0];
        Assert.Equal(0.3, linear.EndPoint.X);
    }
}
