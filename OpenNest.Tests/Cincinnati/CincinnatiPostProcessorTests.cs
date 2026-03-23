using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class CincinnatiPostProcessorTests
{
    [Fact]
    public void Post_ProducesOutput_ForSinglePlateNest()
    {
        var nest = CreateTestNest();
        var config = new CincinnatiPostConfig
        {
            ConfigurationName = "CL940",
            DefaultLibraryFile = "MS135N2PANEL.lib",
            PostedAccuracy = 4
        };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Main program elements
        Assert.Contains("( NEST TestNest )", output);
        Assert.Contains("( CONFIGURATION - CL940 )", output);
        Assert.Contains("G20", output);
        Assert.Contains("M30 (END OF MAIN)", output);

        // Variable declaration
        Assert.Contains(":100", output);
        Assert.Contains("#126=", output);

        // Sheet subprogram
        Assert.Contains(":101", output);
        Assert.Contains("( Sheet 1 )", output);
        Assert.Contains("G84", output);
        Assert.Contains("M99", output);
    }

    [Fact]
    public void Post_ImplementsIPostProcessor()
    {
        var post = new CincinnatiPostProcessor(new CincinnatiPostConfig());
        IPostProcessor pp = post;

        Assert.Equal("Cincinnati CL-707", pp.Name);
        Assert.Equal("OpenNest", pp.Author);
    }

    [Fact]
    public void Post_SkipsEmptyPlates()
    {
        var nest = new Nest("TestNest");
        nest.Plates.Add(new Plate(48, 96)); // empty plate
        var plate2 = new Plate(48, 96);
        plate2.Parts.Add(new Part(new Drawing("Part1", CreateSquareProgram())));
        nest.Plates.Add(plate2);

        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Should only have one sheet subprogram call in main
        Assert.Contains("N1M98 P101 (SHEET 1)", output);
        Assert.DoesNotContain("SHEET 2", output);
    }

    [Fact]
    public void Post_ToFile_CreatesFile()
    {
        var nest = CreateTestNest();
        var config = new CincinnatiPostConfig { PostedAccuracy = 4 };
        var post = new CincinnatiPostProcessor(config);
        var tempFile = Path.GetTempFileName() + ".CNC";

        try
        {
            post.Post(nest, tempFile);
            Assert.True(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);
            Assert.Contains("M30", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Config_RoundTripsAsJson()
    {
        var config = new CincinnatiPostConfig
        {
            ConfigurationName = "CL940_CORONA",
            DefaultLibraryFile = "MS135N2PANEL.lib",
            PostedUnits = Units.Inches,
            KerfCompensation = KerfMode.ControllerSide,
            UseAntiDive = true
        };

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(config, opts);
        var deserialized = JsonSerializer.Deserialize<CincinnatiPostConfig>(json, opts);

        Assert.Equal("CL940_CORONA", deserialized.ConfigurationName);
        Assert.Equal("MS135N2PANEL.lib", deserialized.DefaultLibraryFile);
        Assert.Equal(Units.Inches, deserialized.PostedUnits);
        Assert.Equal(KerfMode.ControllerSide, deserialized.KerfCompensation);
        Assert.True(deserialized.UseAntiDive);

        // Enums serialize as strings
        Assert.Contains("\"Inches\"", json);
        Assert.Contains("\"ControllerSide\"", json);
    }

    [Fact]
    public void ParameterlessConstructor_LoadsOrCreatesConfig()
    {
        // The parameterless constructor reads from a .json file next to the assembly,
        // or creates defaults if none exists. Either way, Config should be non-null.
        var post = new CincinnatiPostProcessor();
        Assert.NotNull(post.Config);
        Assert.Equal("CL940", post.Config.ConfigurationName);
    }

    [Fact]
    public void Post_WithPartSubprograms_WritesM98Calls()
    {
        var nest = CreateTestNest();
        var config = new CincinnatiPostConfig
        {
            PostedAccuracy = 4,
            UsePartSubprograms = true,
            PartSubprogramStart = 200
        };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Sheet should contain M98 call to part sub-program
        Assert.Contains("M98P200", output);

        // Should have G92 for local coordinate positioning
        Assert.Contains("G92X0Y0", output);

        // Part sub-program definition
        Assert.Contains(":200", output);
        Assert.Contains("G84", output);

        // Sub-program ends with G0X0Y0 and M99
        Assert.Contains("G0X0Y0", output);
        Assert.Contains("M99(END OF Square)", output);

        // G92 restore after M98 call
        Assert.Contains("G92X", output);
    }

    [Fact]
    public void Post_WithPartSubprograms_ReusesSameSubprogram()
    {
        var nest = new Nest("TestNest");
        var drawing = new Drawing("Square", CreateSquareProgram());
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(drawing, new Vector(5, 5)));
        plate.Parts.Add(new Part(drawing, new Vector(20, 5)));
        nest.Plates.Add(plate);

        var config = new CincinnatiPostConfig
        {
            PostedAccuracy = 4,
            UsePartSubprograms = true,
            PartSubprogramStart = 200
        };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Both parts should call the same sub-program
        var m98Count = System.Text.RegularExpressions.Regex.Matches(output, "M98P200").Count;
        Assert.Equal(2, m98Count);

        // Only one sub-program definition
        var subDefCount = System.Text.RegularExpressions.Regex.Matches(output, ":200").Count;
        Assert.Equal(1, subDefCount);
    }

    [Fact]
    public void Post_WithPartSubprograms_DifferentRotationsGetSeparateSubprograms()
    {
        var nest = new Nest("TestNest");
        var drawing = new Drawing("Square", CreateSquareProgram());
        var plate = new Plate(48, 96);

        var part1 = new Part(drawing, new Vector(5, 5));
        plate.Parts.Add(part1);

        var part2 = new Part(drawing, new Vector(20, 5));
        part2.Rotate(System.Math.PI / 2); // 90 degrees
        plate.Parts.Add(part2);

        nest.Plates.Add(plate);

        var config = new CincinnatiPostConfig
        {
            PostedAccuracy = 4,
            UsePartSubprograms = true,
            PartSubprogramStart = 200
        };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Should have two different sub-programs
        Assert.Contains(":200", output);
        Assert.Contains(":201", output);
        Assert.Contains("M98P200", output);
        Assert.Contains("M98P201", output);
    }

    [Fact]
    public void Post_WithPartSubprograms_CutoffsAreInline()
    {
        var nest = new Nest("TestNest");
        var drawing = new Drawing("Square", CreateSquareProgram());
        var cutoffDrawing = new Drawing("CutOff", CreateSquareProgram()) { IsCutOff = true };

        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(drawing, new Vector(5, 5)));
        plate.Parts.Add(new Part(cutoffDrawing, new Vector(0, 30)));
        nest.Plates.Add(plate);

        var config = new CincinnatiPostConfig
        {
            PostedAccuracy = 4,
            UsePartSubprograms = true,
            PartSubprogramStart = 200
        };
        var post = new CincinnatiPostProcessor(config);

        using var ms = new MemoryStream();
        post.Post(nest, ms);

        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Regular part uses sub-program
        Assert.Contains("M98P200", output);
        Assert.Contains(":200", output);

        // Cutoff should NOT have its own sub-program
        Assert.DoesNotContain(":201", output);
    }

    [Fact]
    public void Post_WithPartSubprograms_ConfigRoundTrips()
    {
        var config = new CincinnatiPostConfig
        {
            UsePartSubprograms = true,
            PartSubprogramStart = 300
        };

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(config, opts);
        var deserialized = JsonSerializer.Deserialize<CincinnatiPostConfig>(json, opts);

        Assert.True(deserialized.UsePartSubprograms);
        Assert.Equal(300, deserialized.PartSubprogramStart);
    }

    private static Nest CreateTestNest()
    {
        var nest = new Nest("TestNest");
        var drawing = new Drawing("Square", CreateSquareProgram());
        nest.Drawings.Add(drawing);

        var plate = new Plate(48.0, 96.0);
        plate.Parts.Add(new Part(drawing, new Vector(10, 10)));
        nest.Plates.Add(plate);

        return nest;
    }

    private static Program CreateSquareProgram()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(2, 0));
        pgm.Codes.Add(new LinearMove(2, 2));
        pgm.Codes.Add(new LinearMove(0, 2));
        pgm.Codes.Add(new LinearMove(0, 0));
        return pgm;
    }
}
