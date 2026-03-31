using System.Collections.Generic;
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
            PostedAccuracy = 4
        };
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("TestPart", CreateSimpleProgram())));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "MS135N2PANEL.lib", "EtchN2.lib");

        var output = sb.ToString();
        Assert.Contains(":101", output);
        Assert.Contains("( Sheet 1 )", output);
        Assert.Contains("#110=", output);
        Assert.Contains("#111=", output);
        Assert.Contains("G92 X#5021 Y#5022", output);
        Assert.Contains("G89 PMS135N2PANEL.lib", output);
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

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "");

        var output = sb.ToString();
        Assert.Contains("M42", output);
        Assert.Contains("G0 X0 Y0", output);
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

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "");

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

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "");

        var output = sb.ToString();
        // Should have two G84 pierce commands (one per contour)
        var g84Count = output.Split('\n').Count(l => l.Trim() == "G84");
        Assert.Equal(2, g84Count);
    }

    [Fact]
    public void WriteSheet_EtchFeaturesOrderedBeforeCut()
    {
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var pgm = new Program();
        // Cut contour first in program
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(5, 0) { Layer = LayerType.Cut });
        pgm.Codes.Add(new LinearMove(5, 5) { Layer = LayerType.Cut });
        // Etch contour second in program
        pgm.Codes.Add(new RapidMove(1, 1));
        pgm.Codes.Add(new LinearMove(2, 1) { Layer = LayerType.Scribe });
        pgm.Codes.Add(new LinearMove(2, 2) { Layer = LayerType.Scribe });

        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("MixedPart", pgm)));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "MS250O2.lib", "EtchN2.lib");

        var output = sb.ToString();
        // Etch (G85) should appear before cut (G84)
        var g85Idx = output.IndexOf("G85");
        var g84Idx = output.IndexOf("G84");
        Assert.True(g85Idx >= 0, "G85 should be present for etch");
        Assert.True(g84Idx >= 0, "G84 should be present for cut");
        Assert.True(g85Idx < g84Idx, "G85 (etch) should come before G84 (cut)");

        // Etch uses etch library
        Assert.Contains("G89 PEtchN2.lib", output);
        // Cut uses cut library
        Assert.Contains("G89 PMS250O2.lib", output);
    }

    [Fact]
    public void WriteSheet_StartAndEnd_NoM50OnNonLastSheet()
    {
        var config = new CincinnatiPostConfig
        {
            PalletExchange = PalletMode.StartAndEnd,
            PostedAccuracy = 4
        };
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("TestPart", CreateSimpleProgram())));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "", isLastSheet: false);

        var output = sb.ToString();
        Assert.DoesNotContain("M50", output);
    }

    [Fact]
    public void WriteSheet_StartAndEnd_M50OnLastSheet()
    {
        var config = new CincinnatiPostConfig
        {
            PalletExchange = PalletMode.StartAndEnd,
            PostedAccuracy = 4
        };
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(new Drawing("TestPart", CreateSimpleProgram())));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "", isLastSheet: true);

        var output = sb.ToString();
        Assert.Contains("M50", output);
    }

    [Fact]
    public void WriteSheet_EndOfSheet_AlwaysEmitsM50()
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

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "", isLastSheet: false);

        var output = sb.ToString();
        Assert.Contains("M50", output);
    }

    [Fact]
    public void ComputeArcLength_FullCircle_Returns2PiR()
    {
        var start = new Vector(10.0, 20.0);
        var arc = new ArcMove(new Vector(10.0, 20.0), new Vector(15.0, 20.0), RotationType.CW);
        var length = FeatureUtils.ComputeArcLength(start, arc);

        // Radius = 5, full circle = 2 * PI * 5 ≈ 31.416
        Assert.Equal(2.0 * System.Math.PI * 5.0, length, 4);
    }

    [Fact]
    public void ComputeArcLength_Semicircle_ReturnsPiR()
    {
        // Semicircle from (0,0) to (10,0) with center at (5,0), CCW → goes through (5,5)
        var start = new Vector(0.0, 0.0);
        var arc = new ArcMove(new Vector(10.0, 0.0), new Vector(5.0, 0.0), RotationType.CCW);
        var length = FeatureUtils.ComputeArcLength(start, arc);

        // Radius = 5, semicircle = PI * 5 ≈ 15.708
        Assert.Equal(System.Math.PI * 5.0, length, 4);
    }

    [Fact]
    public void ComputeCutDistance_WithArcs_UsesArcLengthNotChord()
    {
        // Full circle: chord = 0 but arc length = 2πr
        var codes = new List<ICode>
        {
            new RapidMove(10.0, 20.0),
            new ArcMove(new Vector(10.0, 20.0), new Vector(15.0, 20.0), RotationType.CW) { Layer = LayerType.Cut }
        };
        var distance = FeatureUtils.ComputeCutDistance(codes);

        // Full circle with R=5 → 2πr ≈ 31.416
        Assert.True(distance > 30.0, $"Expected arc length > 30 but got {distance}");
    }

    [Fact]
    public void IsFeatureEtch_ReturnsTrueForScribeLayer()
    {
        var codes = new List<ICode>
        {
            new RapidMove(0, 0),
            new LinearMove(1, 0) { Layer = LayerType.Scribe },
            new LinearMove(1, 1) { Layer = LayerType.Scribe }
        };

        Assert.True(FeatureUtils.IsEtch(codes));
    }

    [Fact]
    public void IsFeatureEtch_ReturnsFalseForCutLayer()
    {
        var codes = new List<ICode>
        {
            new RapidMove(0, 0),
            new LinearMove(1, 0) { Layer = LayerType.Cut },
            new LinearMove(1, 1) { Layer = LayerType.Cut }
        };

        Assert.False(FeatureUtils.IsEtch(codes));
    }

    [Fact]
    public void IsFeatureEtch_ReturnsFalseForRapidsOnly()
    {
        var codes = new List<ICode>
        {
            new RapidMove(0, 0)
        };

        Assert.False(FeatureUtils.IsEtch(codes));
    }

    [Fact]
    public void WriteSheet_InlineCoordinates_AreAbsoluteOnPlate()
    {
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        // Part program is at origin: (0,0) to (2,0) to (2,2) to (0,2) to (0,0)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(2, 0));
        pgm.Codes.Add(new LinearMove(2, 2));
        pgm.Codes.Add(new LinearMove(0, 2));
        pgm.Codes.Add(new LinearMove(0, 0));

        var plate = new Plate(48.0, 96.0);
        // Place part at (10.5, 5.25) on the plate to produce non-integer coordinates
        plate.Parts.Add(new Part(new Drawing("Square", pgm), new Vector(10.5, 5.25)));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "");

        var output = sb.ToString();
        // Under G90, coordinates must be plate-absolute (part coords + part location)
        Assert.Contains("G0 X10.5 Y5.25", output);      // rapid to pierce
        Assert.Contains("G1 X12.5 Y5.25", output);       // (2,0) + (10.5,5.25)
        Assert.Contains("G1 X12.5 Y7.25", output);       // (2,2) + (10.5,5.25)
        Assert.Contains("G1 X10.5 Y7.25", output);       // (0,2) + (10.5,5.25)
        Assert.Contains("G1 X10.5 Y5.25", output);       // (0,0) + (10.5,5.25)
    }

    [Fact]
    public void WriteSheet_TwoPartsAtDifferentLocations_HaveDistinctAbsoluteCoords()
    {
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(1, 0));
        pgm.Codes.Add(new LinearMove(1, 1));
        pgm.Codes.Add(new LinearMove(0, 0));

        var drawing = new Drawing("Tri", pgm);
        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(drawing, new Vector(5.5, 3.25)));
        plate.Parts.Add(new Part(drawing, new Vector(20.5, 10.25)));

        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        var sheetWriter = new CincinnatiSheetWriter(config, new ProgramVariableManager());

        sheetWriter.Write(sw, plate, "TestNest", 1, 101, "", "");

        var output = sb.ToString();
        // First part at (5.5, 3.25)
        Assert.Contains("G0 X5.5 Y3.25", output);
        Assert.Contains("G1 X6.5 Y3.25", output);
        // Second part at (20.5, 10.25)
        Assert.Contains("G0 X20.5 Y10.25", output);
        Assert.Contains("G1 X21.5 Y10.25", output);
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
