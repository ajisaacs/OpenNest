using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using static OpenNest.Tests.Cincinnati.CincinnatiReplay;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Posts off-origin parts of several drawings and replays the G-code to check what the
/// machine actually does: where it cuts, what it leaves uncut and where it raises the head.
/// </summary>
public class CincinnatiPostRegressionTests
{
    [Fact]
    public void PartSubprograms_CutEveryPartWhereInlinePostingDoes()
    {
        // Outer square first, so each part's toolpath ends away from its corner.
        var nest = SquaresNest(
            OuterFirst,
            ("A", new Vector(5, 5)),
            ("B", new Vector(30, 20)),
            ("A", new Vector(50, 60))
        );

        var inline = Run(Post(nest, partSubprograms: false));
        var grouped = Run(Post(nest, partSubprograms: true));

        var expected = nest.Plates[0].Parts
            .SelectMany(p => new[] { p.Location, p.Location + new Vector(2, 2) })
            .ToList();
        AssertPoints(expected, inline.PierceLog.Select(p => p.Point).ToList());
        AssertSamePath(inline, grouped);
    }

    [Fact]
    public void PartSubprograms_PartsWithOwnLeadIns_PostTheirOwnToolpath()
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        nest.Plates.Add(plate);
        var drawing = new Drawing("A") { Program = HoledSquare() };
        AddLeadInPart(plate, drawing, 0.25, new Vector(5, 5));
        AddLeadInPart(plate, drawing, 0.75, new Vector(20, 5));
        AddLeadInPart(plate, new Drawing("B") { Program = HoledSquare() }, 0.25, new Vector(5, 30));
        AddLeadInPart(plate, drawing, 0.25, new Vector(20, 30));

        var output = Post(nest, partSubprograms: true);

        // Lead-ins differ between the two (drawing, rotation) twins; the identical one shares.
        Assert.Equal(2, Count(output, "M99 (END OF A)"));
        Assert.Equal(1, Count(output, "M99 (END OF B)"));
        AssertSamePath(Run(Post(nest, partSubprograms: false)), Run(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedMoves_AreLeftUncut(bool partSubprograms)
    {
        var suppressed = SquaresProgram();
        ((Motion)suppressed.Codes[7]).Suppressed = true; // outer (10,0) -> (10,10)
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        nest.Plates.Add(plate);
        var a = AddPart(plate, "A", suppressed, new Vector(5, 5));
        AddPart(plate, "B", SquaresProgram(), new Vector(30, 20));

        var run = Run(Post(nest, partSubprograms));

        Assert.DoesNotContain(run.Cuts, c => Near(c.To, a.Location + new Vector(10, 10)) && Near(c.From, a.Location + new Vector(10, 0)));
        Assert.DoesNotContain(run.Cuts, c => Near(c.From, a.Location + new Vector(10, 0)) && !Near(c.To, a.Location + new Vector(10, 0)));
        Assert.Contains(run.PierceLog, p => Near(p.Point, a.Location + new Vector(10, 10)));
        Assert.Equal(15, run.Cuts.Count);
        Assert.Equal(0, run.EmptyPierces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SafetyHeadraise_IsTheOneRaiseBeforeEachLongRapid(bool partSubprograms)
    {
        // The second A is a long rapid from the first despite sharing its drawing.
        var nest = SquaresNest(("A", new Vector(5, 5)), ("A", new Vector(20, 5)), ("B", new Vector(40, 30)));
        var parts = nest.Plates[0].Parts;
        var config = Config(partSubprograms);
        config.SafetyHeadraiseRapidDistance = 6;

        var run = Run(Post(nest, config));

        Assert.Equal(0, run.DoubleRaises);
        Assert.Equal(0, run.HeadDownRapids);
        var safety = run.PierceLog.Where(p => p.Raise == "M47 P2000").ToList();
        Assert.Equal(2, safety.Count);
        Assert.True(Near(safety[0].Point, parts[1].Location + new Vector(2, 2)), $"Safety raise before {safety[0].Point}");
        Assert.True(Near(safety[1].Point, parts[2].Location + new Vector(2, 2)), $"Safety raise before {safety[1].Point}");
        Assert.All(run.PierceLog.Where(p => p.Raise != "M47 P2000"), p => Assert.Equal("M47", p.Raise));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SheetGoto_TargetsThePostedFirstFeatureNumber(bool partSubprograms)
    {
        var nest = SquaresNest(("A", new Vector(5, 5)), ("B", new Vector(30, 20)));
        var config = Config(partSubprograms);
        config.FeatureLineNumberStart = 5;

        var numbered = Sheet(Post(nest, config));
        config.UseLineNumbers = false;
        var unnumbered = Sheet(Post(nest, config));

        Assert.Contains("GOTO5( Goto Feature )", numbered);
        Assert.Contains(numbered, l => l.StartsWith("N5 ", System.StringComparison.Ordinal));
        Assert.DoesNotContain(unnumbered, l => l.StartsWith("GOTO", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoM47_RaisesFullyOnlyBeforeLongRapids(bool partSubprograms)
    {
        var nest = SquaresNest(("A", new Vector(5, 5)), ("B", new Vector(40, 30)));
        var config = Config(partSubprograms);
        config.InteriorM47 = M47Mode.Auto;
        config.ExteriorM47 = M47Mode.Auto;
        config.SafetyHeadraiseDistance = null;
        config.M47OverrideDistanceThreshold = 5;

        var run = Run(Post(nest, config));
        config.M47OverrideDistanceThreshold = null;
        var unlimited = Run(Post(nest, config));

        // Pierces: header raise, A outer (short hop), B inner (long rapid), B outer (short hop).
        Assert.Equal(new[] { "M47", "/M47", "M47", "/M47" }, run.PierceLog.Select(p => p.Raise));
        Assert.All(unlimited.PierceLog, p => Assert.Equal("M47", p.Raise));
        Assert.Equal(0, run.HeadDownRapids);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExteriorM47_AppliesAfterThePartExterior(bool partSubprograms)
    {
        var nest = SquaresNest(("A", new Vector(5, 5)), ("B", new Vector(40, 30)));
        var config = Config(partSubprograms);
        config.InteriorM47 = M47Mode.BlockDelete;
        config.ExteriorM47 = M47Mode.Always;
        config.SafetyHeadraiseDistance = null;

        var run = Run(Post(nest, config));

        Assert.Equal(new[] { "M47", "/M47", "M47", "/M47" }, run.PierceLog.Select(p => p.Raise));
    }

    [Theory]
    [InlineData("N2", 0.25, "MS250N2")]
    [InlineData("O2", 0.25, "MS250O2")]
    [InlineData("N2", 0.30, "")]
    [InlineData("O2", 0.188, "")]
    public void PrepareForNest_SelectsOnlyTheGasAndThicknessMatch(string gas, double thickness, string expected)
    {
        var nest = SquaresNest(("A", new Vector(5, 5)), ("B", new Vector(30, 20)));
        nest.Material = new Material("Carbon Steel");
        nest.Thickness = thickness;
        nest.AssistGas = gas;
        var config = Config(partSubprograms: false);
        config.MaterialLibraries = new()
        {
            new() { Material = "Carbon Steel", Thickness = 0.25, Gas = "O2", Library = "MS250O2" },
            new() { Material = "Carbon Steel", Thickness = 0.25, Gas = "N2", Library = "MS250N2" },
            new() { Material = "Carbon Steel", Thickness = 0.188, Gas = "N2", Library = "MS188N2" },
        };
        var post = new CincinnatiPostProcessor(config);

        post.PrepareForNest(nest);

        Assert.Equal(expected, config.SelectedLibrary);
        var libraries = Sheet(Post(nest, config))
            .Where(l => l.StartsWith("G89 P", System.StringComparison.Ordinal))
            .Select(l => l.Split(' ')[1])
            .Distinct()
            .ToList();
        if (expected == "")
            Assert.Empty(libraries);
        else
            Assert.Equal(new[] { $"P{expected}.lib" }, libraries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalPartPrograms_PostWithoutChangingMode(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        nest.Plates.Add(plate);
        var large = HolePlacementRegressionTests.AddPart(plate, "Large", 0.5, new Vector(10, 20));
        var small = HolePlacementRegressionTests.AddPart(plate, "Small", 0.25, new Vector(30, 40));
        large.Program.Mode = Mode.Incremental;
        small.Program.Mode = Mode.Incremental;

        var run = Run(Post(nest, partSubprograms));

        Assert.Equal(Mode.Incremental, large.Program.Mode);
        Assert.Equal(Mode.Incremental, small.Program.Mode);
        HolePlacementRegressionTests.AssertHole(run, large, 0.5);
        HolePlacementRegressionTests.AssertHole(run, small, 0.25);
    }

    [Fact]
    public void Config_WithRemovedSettings_StillLoads()
    {
        const string json = """
            {
              "UseSheetSubprograms": true,
              "CoordModeBetweenParts": "G92",
              "ProcessParameterMode": "Explicit",
              "InteriorM47": "Auto",
              "M47OverrideDistanceThreshold": 2.5
            }
            """;
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        var config = JsonSerializer.Deserialize<CincinnatiPostConfig>(json, options)!;

        Assert.Equal(M47Mode.Auto, config.InteriorM47);
        Assert.Equal(2.5, config.M47OverrideDistanceThreshold);
    }

    // Inner square cut first, then the outer square: pierces at (2,2) and (0,0).
    private static Program SquaresProgram()
    {
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(2, 2));
        pgm.Codes.Add(new LinearMove(4, 2));
        pgm.Codes.Add(new LinearMove(4, 4));
        pgm.Codes.Add(new LinearMove(2, 4));
        pgm.Codes.Add(new LinearMove(2, 2));
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        return pgm;
    }

    private static Program OuterFirst()
    {
        var pgm = SquaresProgram();
        var inner = pgm.Codes.GetRange(0, 5);
        pgm.Codes.RemoveRange(0, 5);
        pgm.Codes.AddRange(inner);
        return pgm;
    }

    private static Program HoledSquare()
    {
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        pgm.Codes.Add(new RapidMove(3.5, 3));
        pgm.Codes.Add(new ArcMove(new Vector(3.5, 3), new Vector(3, 3), RotationType.CW));
        return pgm;
    }

    private static Nest SquaresNest(params (string Name, Vector Location)[] parts) =>
        SquaresNest(SquaresProgram, parts);

    private static Nest SquaresNest(Func<Program> program, params (string Name, Vector Location)[] parts)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        nest.Plates.Add(plate);
        var drawings = new Dictionary<string, Drawing>();
        foreach (var (name, location) in parts)
        {
            if (!drawings.TryGetValue(name, out var drawing))
                drawings[name] = drawing = new Drawing(name) { Program = program() };
            plate.Parts.Add(new Part(drawing) { Location = location });
        }
        return nest;
    }

    private static Part AddPart(Plate plate, string name, Program program, Vector location)
    {
        var part = new Part(new Drawing(name) { Program = program }) { Location = location };
        plate.Parts.Add(part);
        return part;
    }

    private static void AddLeadInPart(Plate plate, Drawing drawing, double leadIn, Vector location)
    {
        var part = new Part(drawing);
        part.ApplyLeadIns(
            new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = leadIn, ApproachAngle = 90 },
                ExternalLeadOut = new NoLeadOut(),
            },
            new Vector(10, 10)
        );
        part.Location = location;
        plate.Parts.Add(part);
    }

    private static List<string> Sheet(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).ToList();
        var start = lines.IndexOf(":101");
        var end = lines.FindIndex(start, l => l.StartsWith("M99 (END OF", System.StringComparison.Ordinal));
        return lines.GetRange(start, end - start + 1);
    }

    private static int Count(string output, string line) =>
        output.Split('\n').Count(l => l.Trim() == line);

    private static void AssertPoints(IReadOnlyList<Vector> expected, IReadOnlyList<Vector> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(Near(expected[i], actual[i]), $"Point {i}: expected {expected[i]}, got {actual[i]}");
    }

    private static void AssertSamePath(Result expected, Result actual)
    {
        AssertPoints(expected.PierceLog.Select(p => p.Point).ToList(), actual.PierceLog.Select(p => p.Point).ToList());
        AssertPoints(expected.Cuts.Select(c => c.From).ToList(), actual.Cuts.Select(c => c.From).ToList());
        AssertPoints(expected.Cuts.Select(c => c.To).ToList(), actual.Cuts.Select(c => c.To).ToList());
        Assert.Equal(expected.FullCircles.Count, actual.FullCircles.Count);
    }
}
