using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class CincinnatiFeatureWriterTests
{
    private static CincinnatiPostConfig DefaultConfig() => new()
    {
        UseLineNumbers = true,
        FeatureLineNumberStart = 1,
        UseAntiDive = true,
        KerfCompensation = KerfMode.ControllerSide,
        DefaultKerfSide = KerfSide.Left,
        ProcessParameterMode = G89Mode.LibraryFile,
        InteriorM47 = M47Mode.Always,
        ExteriorM47 = M47Mode.Always,
        UseSpeedGas = false,
        PostedAccuracy = 4,
        SafetyHeadraiseDistance = 2000
    };

    private static FeatureContext SimpleContext(List<ICode>? codes = null) => new()
    {
        Codes = codes ?? new List<ICode>
        {
            new RapidMove(13.401, 57.4895),
            new LinearMove(14.0, 57.5) { Layer = LayerType.Leadin },
            new LinearMove(20.0, 57.5) { Layer = LayerType.Cut }
        },
        FeatureNumber = 1,
        PartName = "BRACKET",
        IsFirstFeatureOfPart = true,
        IsLastFeatureOnSheet = false,
        IsSafetyHeadraise = false,
        IsExteriorFeature = false,
        LibraryFile = "MILD10",
        CutDistance = 18.0,
        SheetDiagonal = 30.0
    };

    private static string WriteFeature(CincinnatiPostConfig config, FeatureContext ctx)
    {
        var writer = new CincinnatiFeatureWriter(config);
        using var sw = new StringWriter();
        writer.Write(sw, ctx);
        return sw.ToString();
    }

    [Fact]
    public void RapidToPiercePoint_WithLineNumber()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("N1 G0 X13.401 Y57.4895", lines[0]);
    }

    [Fact]
    public void RapidToPiercePoint_WithoutLineNumber()
    {
        var config = DefaultConfig();
        config.UseLineNumbers = false;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("G0 X13.401 Y57.4895", lines[0]);
    }

    [Fact]
    public void G84_PierceEmitted()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("G84", output);
    }

    [Fact]
    public void AntiDive_M130M131_EmittedWhenEnabled()
    {
        var config = DefaultConfig();
        config.UseAntiDive = true;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("M130 (ANTI DIVE OFF)", output);
        Assert.Contains("M131 (ANTI DIVE ON)", output);
    }

    [Fact]
    public void AntiDive_NotEmittedWhenDisabled()
    {
        var config = DefaultConfig();
        config.UseAntiDive = false;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.DoesNotContain("M130", output);
        Assert.DoesNotContain("M131", output);
    }

    [Fact]
    public void KerfCompensation_G41G40_EmittedWhenControllerSide()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.ControllerSide;
        config.DefaultKerfSide = KerfSide.Left;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("G41", output);
        Assert.Contains("G40", output);
    }

    [Fact]
    public void KerfCompensation_G42_EmittedForRightSide()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.ControllerSide;
        config.DefaultKerfSide = KerfSide.Right;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("G42", output);
    }

    [Fact]
    public void KerfCompensation_NotEmittedWhenPreApplied()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.PreApplied;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.DoesNotContain("G41", output);
        Assert.DoesNotContain("G42", output);
        Assert.DoesNotContain("G40", output);
    }

    [Fact]
    public void M35_BeamOffEmitted()
    {
        var config = DefaultConfig();
        config.UseSpeedGas = false;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("M35", output);
    }

    [Fact]
    public void M135_BeamOffEmittedWhenSpeedGas()
    {
        var config = DefaultConfig();
        config.UseSpeedGas = true;
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);

        Assert.Contains("M135", output);
    }

    [Fact]
    public void M47_EmittedWhenNotLastFeature()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        ctx.IsLastFeatureOnSheet = false;
        var output = WriteFeature(config, ctx);

        Assert.Contains("M47", output);
    }

    [Fact]
    public void M47_OmittedWhenLastFeatureOnSheet()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        ctx.IsLastFeatureOnSheet = true;
        var output = WriteFeature(config, ctx);

        // M47 should not appear, but M35 should still be there
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(lines, l => l.Trim() == "M47" || l.Trim() == "/M47");
    }

    [Fact]
    public void M47_BlockDeleteMode_EmitsSlashM47()
    {
        var config = DefaultConfig();
        config.InteriorM47 = M47Mode.BlockDelete;
        var ctx = SimpleContext();
        ctx.IsExteriorFeature = false;
        ctx.IsLastFeatureOnSheet = false;
        var output = WriteFeature(config, ctx);

        Assert.Contains("/M47", output);
    }

    [Fact]
    public void M47_NoneMode_NoM47Emitted()
    {
        var config = DefaultConfig();
        config.InteriorM47 = M47Mode.None;
        var ctx = SimpleContext();
        ctx.IsExteriorFeature = false;
        ctx.IsLastFeatureOnSheet = false;
        var output = WriteFeature(config, ctx);

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(lines, l => l.Trim() == "M47" || l.Trim() == "/M47");
    }

    [Fact]
    public void ArcIJ_ConvertedFromAbsoluteToIncremental()
    {
        var config = DefaultConfig();
        // Arc starts at rapid endpoint (10, 20), center at (15, 20) absolute
        // So incremental I = 15 - 10 = 5, J = 20 - 20 = 0
        var codes = new List<ICode>
        {
            new RapidMove(10.0, 20.0),
            new ArcMove(
                endPoint: new Vector(10.0, 20.0),
                centerPoint: new Vector(15.0, 20.0),
                rotation: RotationType.CW
            ) { Layer = LayerType.Cut }
        };

        var ctx = SimpleContext(codes);
        var output = WriteFeature(config, ctx);

        // Should contain incremental I=5, J=0
        Assert.Contains("I5", output);
        Assert.Contains("J0", output);
    }

    [Fact]
    public void ArcMove_G2ForCW_G3ForCCW()
    {
        var config = DefaultConfig();
        var cwCodes = new List<ICode>
        {
            new RapidMove(10.0, 20.0),
            new ArcMove(new Vector(20.0, 20.0), new Vector(15.0, 20.0), RotationType.CW) { Layer = LayerType.Cut }
        };
        var ccwCodes = new List<ICode>
        {
            new RapidMove(10.0, 20.0),
            new ArcMove(new Vector(20.0, 20.0), new Vector(15.0, 20.0), RotationType.CCW) { Layer = LayerType.Cut }
        };

        var cwOutput = WriteFeature(config, SimpleContext(cwCodes));
        var ccwOutput = WriteFeature(config, SimpleContext(ccwCodes));

        Assert.Contains("G2 X", cwOutput);
        Assert.Contains("G3 X", ccwOutput);
    }

    [Fact]
    public void PartNameComment_EmittedOnFirstFeature()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        ctx.IsFirstFeatureOfPart = true;
        ctx.PartName = "FLANGE";
        var output = WriteFeature(config, ctx);

        Assert.Contains("( PART: FLANGE )", output);
    }

    [Fact]
    public void PartNameComment_NotEmittedOnSubsequentFeatures()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        ctx.IsFirstFeatureOfPart = false;
        ctx.PartName = "FLANGE";
        var output = WriteFeature(config, ctx);

        Assert.DoesNotContain("PART:", output);
    }

    [Fact]
    public void G89_EmittedWithLibraryFile()
    {
        var config = DefaultConfig();
        config.ProcessParameterMode = G89Mode.LibraryFile;
        var ctx = SimpleContext();
        ctx.LibraryFile = "MILD10";
        ctx.CutDistance = 18.0;
        ctx.SheetDiagonal = 30.0;
        var output = WriteFeature(config, ctx);

        Assert.Contains("G89 P MILD10", output);
    }

    [Fact]
    public void G89_WarningEmittedWhenNoLibrary()
    {
        var config = DefaultConfig();
        config.ProcessParameterMode = G89Mode.LibraryFile;
        var ctx = SimpleContext();
        ctx.LibraryFile = "";
        var output = WriteFeature(config, ctx);

        Assert.Contains("WARNING: No library found", output);
        Assert.DoesNotContain("G89 P", output);
    }

    [Fact]
    public void Etch_UsesG85InsteadOfG84()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        ctx.IsEtch = true;
        ctx.LibraryFile = "EtchN2.lib";
        var output = WriteFeature(config, ctx);

        Assert.Contains("G85", output);
        Assert.DoesNotContain("G84", output);
    }

    [Fact]
    public void Etch_SkipsKerfCompensation()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.ControllerSide;
        var ctx = SimpleContext();
        ctx.IsEtch = true;
        ctx.LibraryFile = "EtchN2.lib";
        var output = WriteFeature(config, ctx);

        Assert.DoesNotContain("G41", output);
        Assert.DoesNotContain("G42", output);
        Assert.DoesNotContain("G40", output);
    }

    [Fact]
    public void Etch_AllMovesUseProcessFeedrate()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.PreApplied;
        var codes = new List<ICode>
        {
            new RapidMove(1.0, 1.0),
            new LinearMove(2.0, 1.0) { Layer = LayerType.Leadin },
            new LinearMove(3.0, 1.0) { Layer = LayerType.Cut }
        };
        var ctx = SimpleContext(codes);
        ctx.IsEtch = true;
        ctx.LibraryFile = "EtchN2.lib";
        var output = WriteFeature(config, ctx);

        // Should use #148 for all moves, not #126 for lead-in
        Assert.DoesNotContain("F#126", output);
        Assert.Contains("F#148", output);
    }

    [Fact]
    public void FeedrateModalSuppression_OnlyEmitsOnChange()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.PreApplied; // simplify output
        var codes = new List<ICode>
        {
            new RapidMove(1.0, 1.0),
            new LinearMove(2.0, 1.0) { Layer = LayerType.Cut },
            new LinearMove(3.0, 1.0) { Layer = LayerType.Cut },
            new LinearMove(4.0, 1.0) { Layer = LayerType.Cut }
        };
        var ctx = SimpleContext(codes);
        var output = WriteFeature(config, ctx);

        // F#148 should appear only once (on the first cut move)
        var count = CountOccurrences(output, "F#148");
        Assert.Equal(1, count);
    }

    [Fact]
    public void LeadinFeedrate_UsesVariable126()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.PreApplied; // simplify output
        var codes = new List<ICode>
        {
            new RapidMove(1.0, 1.0),
            new LinearMove(2.0, 1.0) { Layer = LayerType.Leadin }
        };
        var ctx = SimpleContext(codes);
        var output = WriteFeature(config, ctx);

        Assert.Contains("F#126", output);
    }

    [Fact]
    public void FullCircleArc_UsesMultipliedFeedrate()
    {
        var config = DefaultConfig();
        config.KerfCompensation = KerfMode.PreApplied;
        // Full circle: start == end
        var codes = new List<ICode>
        {
            new RapidMove(10.0, 20.0),
            new ArcMove(new Vector(10.0, 20.0), new Vector(15.0, 20.0), RotationType.CW) { Layer = LayerType.Cut }
        };
        var ctx = SimpleContext(codes);
        var output = WriteFeature(config, ctx);

        Assert.Contains("F[#148*#128]", output);
    }

    [Fact]
    public void SafetyHeadraise_EmitsM47WithDistance()
    {
        var config = DefaultConfig();
        config.SafetyHeadraiseDistance = 2000;
        var ctx = SimpleContext();
        ctx.IsSafetyHeadraise = true;
        ctx.IsLastFeatureOnSheet = false;
        var output = WriteFeature(config, ctx);

        Assert.Contains("M47 P2000 (Safety Headraise)", output);
    }

    [Fact]
    public void ExteriorM47Mode_UsesExteriorConfig()
    {
        var config = DefaultConfig();
        config.ExteriorM47 = M47Mode.BlockDelete;
        config.InteriorM47 = M47Mode.Always;
        var ctx = SimpleContext();
        ctx.IsExteriorFeature = true;
        ctx.IsLastFeatureOnSheet = false;
        var output = WriteFeature(config, ctx);

        Assert.Contains("/M47", output);
    }

    [Fact]
    public void OutputSequence_CorrectOrder()
    {
        var config = DefaultConfig();
        var ctx = SimpleContext();
        var output = WriteFeature(config, ctx);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // Find indices of key lines
        var rapidIdx = Array.FindIndex(lines, l => l.Contains("G0 X"));
        var partIdx = Array.FindIndex(lines, l => l.Contains("PART:"));
        var g89Idx = Array.FindIndex(lines, l => l.Contains("G89"));
        var g84Idx = Array.FindIndex(lines, l => l.Contains("G84"));
        var m130Idx = Array.FindIndex(lines, l => l.Contains("M130"));
        var g40Idx = Array.FindIndex(lines, l => l.Contains("G40"));
        var m35Idx = Array.FindIndex(lines, l => l.Contains("M35"));
        var m131Idx = Array.FindIndex(lines, l => l.Contains("M131"));
        var m47Idx = Array.FindIndex(lines, l => l.Trim() == "M47");

        Assert.True(rapidIdx < partIdx, "Rapid should come before part comment");
        Assert.True(partIdx < g89Idx, "Part comment should come before G89");
        Assert.True(g89Idx < g84Idx, "G89 should come before G84");
        Assert.True(g84Idx < m130Idx, "G84 should come before M130");
        Assert.True(g40Idx < m35Idx, "G40 should come before M35");
        Assert.True(m35Idx < m131Idx, "M35 should come before M131");
        Assert.True(m131Idx < m47Idx, "M131 should come before M47");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
