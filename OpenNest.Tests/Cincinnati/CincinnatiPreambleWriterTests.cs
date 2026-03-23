using System.IO;
using System.Text;
using OpenNest.CNC;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class CincinnatiPreambleWriterTests
{
    [Fact]
    public void WriteMainProgram_EmitsHeader()
    {
        var config = new CincinnatiPostConfig
        {
            ConfigurationName = "CL940",
            PostedUnits = Units.Inches,
            DefaultLibraryFile = "MS135N2PANEL.lib"
        };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "TestNest", "Mild Steel, 10GA", 2);

        var output = sb.ToString();
        Assert.Contains("( NEST TestNest )", output);
        Assert.Contains("( CONFIGURATION - CL940 )", output);
        Assert.Contains("G20", output);
        Assert.Contains("M42", output);
        Assert.Contains("G89 P MS135N2PANEL.lib", output);
        Assert.Contains("M98 P100 (Variable Declaration)", output);
        Assert.Contains("GOTO1 (GOTO SHEET NUMBER)", output);
        Assert.Contains("N1M98 P101 (SHEET 1)", output);
        Assert.Contains("N2M98 P102 (SHEET 2)", output);
        Assert.Contains("M30 (END OF MAIN)", output);
    }

    [Fact]
    public void WriteMainProgram_EmitsG21ForMetric()
    {
        var config = new CincinnatiPostConfig { PostedUnits = Units.Millimeters };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", 1);

        Assert.Contains("G21", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsG61_WhenExactStop()
    {
        var config = new CincinnatiPostConfig { UseExactStopMode = true };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", 1);

        Assert.Contains("G61", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_OmitsG61_WhenNotExactStop()
    {
        var config = new CincinnatiPostConfig { UseExactStopMode = false };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", 1);

        Assert.DoesNotContain("G61", sb.ToString());
    }

    [Fact]
    public void WriteVariableDeclaration_EmitsSubprogram()
    {
        var config = new CincinnatiPostConfig();
        var vars = new ProgramVariableManager();
        vars.GetOrCreate("LeadInFeedrate", 126, "[#148*0.5]");
        vars.GetOrCreate("CircleFeedrate", 128, ".8");

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteVariableDeclaration(sw, vars);

        var output = sb.ToString();
        Assert.Contains(":100", output);
        Assert.Contains("(Variable Declaration Start)", output);
        Assert.Contains("#126=", output);
        Assert.Contains("#128=", output);
        Assert.Contains("M99 (Variable Declaration End)", output);
    }
}
