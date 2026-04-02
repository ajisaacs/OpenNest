using System.Collections.Generic;
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
            PostedUnits = Units.Inches
        };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        var plates = new List<Plate> { new(48, 96), new(48, 96) };
        writer.WriteMainProgram(sw, "TestNest", "Mild Steel, 10GA", plates, "MS135N2PANEL.lib");

        var output = sb.ToString();
        Assert.Contains("( NEST TestNest )", output);
        Assert.Contains("( CONFIGURATION - CL940 )", output);
        Assert.Contains("G20 G90", output);
        Assert.Contains("M42", output);
        Assert.Contains("G89 PMS135N2PANEL.lib", output);
        Assert.Contains("M98 P100 (Variable Declaration)", output);
        Assert.Contains("GOTO1 (GOTO SHEET NUMBER)", output);
        Assert.Contains("N1 M98 P101 (LAYOUT 1)", output);
        Assert.Contains("N2 M98 P102 (LAYOUT 2)", output);
        Assert.Contains("M30 (END OF MAIN)", output);
    }

    [Fact]
    public void WriteMainProgram_EmitsG21ForMetric()
    {
        var config = new CincinnatiPostConfig { PostedUnits = Units.Millimeters };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.Contains("G21 G90", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsG90WithUnits()
    {
        var config = new CincinnatiPostConfig { PostedUnits = Units.Inches };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.Contains("G20 G90", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsG121_WhenSmartRapidsEnabled()
    {
        var config = new CincinnatiPostConfig { UseSmartRapids = true };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.Contains("G121 (SMART RAPIDS)", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_OmitsG121_WhenSmartRapidsDisabled()
    {
        var config = new CincinnatiPostConfig { UseSmartRapids = false };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.DoesNotContain("G121", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsM50_WhenStartAndEnd()
    {
        var config = new CincinnatiPostConfig { PalletExchange = PalletMode.StartAndEnd };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.Contains("M50", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_OmitsM50_WhenEndOfSheet()
    {
        var config = new CincinnatiPostConfig { PalletExchange = PalletMode.EndOfSheet };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.DoesNotContain("M50", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsG61_WhenExactStop()
    {
        var config = new CincinnatiPostConfig { UseExactStopMode = true };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.Contains("G61", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_OmitsG61_WhenNotExactStop()
    {
        var config = new CincinnatiPostConfig { UseExactStopMode = false };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        writer.WriteMainProgram(sw, "Test", "", new List<Plate> { new(48, 96) }, "");

        Assert.DoesNotContain("G61", sb.ToString());
    }

    [Fact]
    public void WriteMainProgram_EmitsLCount_WhenQuantityGreaterThanOne()
    {
        var config = new CincinnatiPostConfig { PostedUnits = Units.Inches };
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var writer = new CincinnatiPreambleWriter(config);

        var plates = new List<Plate>
        {
            new(48, 96) { Quantity = 5 },
            new(72, 48) { Quantity = 2 },
            new(36, 48) { Quantity = 1 }
        };
        writer.WriteMainProgram(sw, "Test", "", plates, "");

        var output = sb.ToString();
        Assert.Contains("N1 M98 P101 L5 (LAYOUT 1 - 5 SHEETS)", output);
        Assert.Contains("N2 M98 P102 L2 (LAYOUT 2 - 2 SHEETS)", output);
        Assert.Contains("N3 M98 P103 (LAYOUT 3)", output);
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
