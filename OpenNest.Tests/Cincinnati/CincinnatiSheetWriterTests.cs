using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class CincinnatiSheetWriterTests
{
    [Fact]
    public void WriteSheet_EmitsSheetHeader()
    {
        var config = new CincinnatiPostConfig
        {
            DefaultLibraryFile = "MS135N2PANEL.lib",
            PostedAccuracy = 4
        };
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("TestPart", CreateSimpleProgram())));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101);

        var output = sb.ToString();
        Assert.Contains(":101", output);
        Assert.Contains("( Sheet 1 )", output);
        Assert.Contains("#110=", output);
        Assert.Contains("#111=", output);
        Assert.Contains("G92X#5021Y#5022", output);
        Assert.Contains("M99", output);
    }

    [Fact]
    public void WriteSheet_EmitsReturnToOriginAndPalletExchange()
    {
        var config = new CincinnatiPostConfig
        {
            PalletExchange = PalletMode.EndOfSheet,
            PostedAccuracy = 4
        };
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("TestPart", CreateSimpleProgram())));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101);

        var output = sb.ToString();
        Assert.Contains("M42", output);
        Assert.Contains("G0X0Y0", output);
        Assert.Contains("M50", output);
    }

    [Fact]
    public void WriteSheet_SkipsEmptyPlate()
    {
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var plate = new Plate(48.0, 96.0);

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101);

        Assert.Equal("", sb.ToString());
    }

    [Fact]
    public void WriteSheet_SplitsMultiContourParts()
    {
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var pgm = new Program();
        // First contour (hole)
        pgm.Codes.Add(new RapidMove(1, 1));
        pgm.Codes.Add(new LinearMove(2, 1));
        pgm.Codes.Add(new LinearMove(2, 2));
        pgm.Codes.Add(new LinearMove(1, 1));
        // Second contour (exterior)
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(5, 0));
        pgm.Codes.Add(new LinearMove(5, 5));
        pgm.Codes.Add(new LinearMove(0, 0));

        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("MultiContour", pgm)));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101);

        var output = sb.ToString();
        // Should have two G84 pierce commands (one per contour)
        var g84Count = output.Split('\n').Count(l => l.Trim() == "G84");
        Assert.Equal(2, g84Count);
    }

    private static Program CreateSimpleProgram()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(1, 0));
        pgm.Codes.Add(new LinearMove(1, 1));
        pgm.Codes.Add(new LinearMove(0, 1));
        pgm.Codes.Add(new LinearMove(0, 0));
        return pgm;
    }
}
