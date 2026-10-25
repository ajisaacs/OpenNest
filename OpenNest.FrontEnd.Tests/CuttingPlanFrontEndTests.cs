using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

public class CuttingPlanFrontEndTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-cut-plan-" + Guid.NewGuid());

    public CuttingPlanFrontEndTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Console_PlanCutting_AppliesTheReadyPlanAndSavesItsSettings()
    {
        var input = Write(Fixture(Square(), new Vector(1, 1), new Vector(20, 1)));
        var output = Path.Combine(directory, "output.nest");

        Assert.Equal(0, RunConsole(input, "--no-fill", "--plan-cutting", "--cutting-params", Settings(),
            "--output", output));

        var plate = new NestReader(output).Read().Plates.Single();
        Assert.NotNull(plate.CuttingParameters);
        Assert.Equal(2, plate.Parts.Count);
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
    }

    [Fact]
    public void Console_PlanCutting_UsesThePlatesSavedSettings()
    {
        var nest = Fixture(Square(), new Vector(1, 1));
        nest.Plates[0].CuttingParameters = Parameters();
        var output = Path.Combine(directory, "output.nest");

        Assert.Equal(0, RunConsole(Write(nest), "--no-fill", "--plan-cutting", "--output", output));

        Assert.True(new NestReader(output).Read().Plates.Single().Parts.Single().HasManualLeadIns);
    }

    [Theory]
    [InlineData("no settings", 2)]
    [InlineData("overlap", 2)]
    [InlineData("null settings", 1)]
    public void Console_PlanCutting_SavesNothingWithoutSettingsOrWhenBlocked(string mode, int expected)
    {
        var locations = mode == "overlap" ? new[] { new Vector(1, 1), new Vector(6, 6) } : new[] { new Vector(1, 1) };
        var input = Write(Fixture(Square(), locations));
        var output = Path.Combine(directory, "output.nest");
        File.WriteAllText(output, "must survive rejection");
        var args = new List<string> { input, "--no-fill", "--plan-cutting", "--output", output };
        if (mode != "no settings")
            args.AddRange(["--cutting-params", Settings(mode == "null settings" ? "null" : null)]);

        Assert.Equal(expected, RunConsole(args.ToArray()));

        Assert.Equal("must survive rejection", File.ReadAllText(output));
    }

    [Fact]
    public void Console_UnverifiedPlan_AppliesOnlyWithExplicitAcceptance()
    {
        var input = Write(Fixture(TouchingHoles(), new Vector(1, 1)));
        var output = Path.Combine(directory, "output.nest");
        File.WriteAllText(output, "must survive rejection");
        string[] args = [input, "--no-fill", "--plan-cutting", "--cutting-params", Settings(), "--output", output];

        Assert.Equal(2, RunConsole(args));
        Assert.Equal("must survive rejection", File.ReadAllText(output));

        Assert.Equal(0, RunConsole([.. args, "--accept-unverified-plan"]));
        Assert.True(new NestReader(output).Read().Plates.Single().Parts.Single().HasManualLeadIns);
    }

    [Theory]
    [InlineData("--keep-part-order")]
    [InlineData("--all-plates")]
    [InlineData("--accept-unverified-plan")]
    [InlineData("--no-fill", "--plan-cutting", "--quantity", "2")]
    [InlineData("--no-fill", "--autonest")]
    public void Console_PlanningOptions_AreRefusedOutOfContext(params string[] extra)
    {
        var input = Write(Fixture(Square(), new Vector(1, 1)));
        var output = Path.Combine(directory, "output.nest");

        Assert.Equal(1, RunConsole([input, .. extra, "--output", output]));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Mcp_PlanThenApply_ChangesNothingUntilApplied()
    {
        var session = new NestSession { Nest = Fixture(Square(), new Vector(1, 1), new Vector(20, 1)) };
        var tools = new CuttingTools(session);
        var plate = session.Nest.Plates[0];
        var programs = plate.Parts.Select(p => p.Program).ToArray();

        var report = tools.PlanCutting(0, cuttingParametersPath: Settings());

        Assert.Contains("Ready to apply", report);
        Assert.Contains("Call apply_cutting_plan", report);
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.Null(plate.CuttingParameters);

        Assert.StartsWith("Applied cutting plan to plate(s) 0", tools.ApplyCuttingPlan());
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
        Assert.NotNull(plate.CuttingParameters);
        Assert.StartsWith("Error: no cutting plan", tools.ApplyCuttingPlan()); // Used at most once.
    }

    [Fact]
    public void Mcp_PlateChangedSincePlanning_IsStaleAndChangesNothing()
    {
        var session = new NestSession { Nest = Fixture(Square(), new Vector(1, 1)) };
        var tools = new CuttingTools(session);
        var plate = session.Nest.Plates[0];
        Assert.Contains("Ready to apply", tools.PlanCutting(-1, cuttingParametersPath: Settings()));

        plate.Parts.Add(new Part(plate.Parts[0].BaseDrawing, new Vector(40, 40)));

        Assert.Contains("changed since planning", tools.ApplyCuttingPlan());
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));
        Assert.Null(plate.CuttingParameters);
    }

    [Fact]
    public void Mcp_UnverifiedPlan_AppliesOnlyWithAcceptWarnings()
    {
        var session = new NestSession { Nest = Fixture(TouchingHoles(), new Vector(1, 1)) };
        var tools = new CuttingTools(session);
        var part = session.Nest.Plates[0].Parts[0];

        Assert.Contains("acceptWarnings=true", tools.PlanCutting(cuttingParametersPath: Settings()));
        Assert.StartsWith("Error: this plan is unverified", tools.ApplyCuttingPlan());
        Assert.False(part.HasManualLeadIns);

        Assert.StartsWith("Applied", tools.ApplyCuttingPlan(acceptWarnings: true));
        Assert.True(part.HasManualLeadIns);
    }

    [Fact]
    public void Mcp_PlanCutting_NeedsConfirmedSettings()
    {
        var session = new NestSession { Nest = Fixture(Square(), new Vector(1, 1)) };
        var tools = new CuttingTools(session);

        Assert.StartsWith("Error: no confirmed cutting settings", tools.PlanCutting());
        Assert.StartsWith("Error: cannot read cutting settings", tools.PlanCutting(cuttingParametersPath: Settings("null")));
        Assert.StartsWith("Error: no cutting plan", tools.ApplyCuttingPlan());

        session.Nest.Plates[0].CuttingParameters = Parameters();
        Assert.Contains("saved with the plates", tools.PlanCutting());
    }

    [Fact]
    public void Mcp_ReplacedNest_DropsTheProposal()
    {
        var session = new NestSession { Nest = Fixture(Square(), new Vector(1, 1)) };
        var tools = new CuttingTools(session);
        var part = session.Nest.Plates[0].Parts[0];
        Assert.Contains("Ready to apply", tools.PlanCutting(cuttingParametersPath: Settings()));

        session.Nest = Fixture(Square(), new Vector(1, 1));

        Assert.Contains("no longer in the session", tools.ApplyCuttingPlan());
        Assert.False(part.HasManualLeadIns);
    }

    private static int RunConsole(params string[] args)
    {
        var method = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
        return (int)method.Invoke(null, new object[] { args })!;
    }

    private static Nest Fixture(Program program, params Vector[] locations)
    {
        var nest = new Nest("fixture");
        var drawing = new Drawing("square", program);
        nest.Drawings.Add(drawing);
        var plate = new Plate(100, 100);
        foreach (var location in locations)
            plate.Parts.Add(new Part(drawing, location));
        nest.Plates.Add(plate);
        return nest;
    }

    private static Program Square()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    // Two holes sharing an edge: closed contours with ambiguous material, planned only best-effort.
    private static Program TouchingHoles()
    {
        var program = Square();
        program.MoveTo(2, 2); program.LineTo(4, 2); program.LineTo(4, 4);
        program.LineTo(2, 4); program.LineTo(2, 2);
        program.MoveTo(4, 2); program.LineTo(6, 2); program.LineTo(6, 4);
        program.LineTo(4, 4); program.LineTo(4, 2);
        return program;
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        InternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0.05,
    };

    private string Write(Nest nest)
    {
        var path = Path.Combine(directory, "input.nest");
        Assert.True(new NestWriter(nest).Write(path));
        return path;
    }

    private string Settings(string? json = null)
    {
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, json ?? CuttingParametersSerializer.Serialize(Parameters()));
        return path;
    }
}
