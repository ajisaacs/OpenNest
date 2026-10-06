using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

/// <summary>Front ends that are not told an engine use Default; fill tools use Fill.</summary>
[Collection("FrontEndRegistry")]
public class DefaultEngineSelectionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-default-engine-" + Guid.NewGuid());

    public DefaultEngineSelectionTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static NestSession Session(bool occupied = false)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        var session = new NestSession { Nest = new Nest("fixture") };
        session.Nest.Drawings.Add(new Drawing("square", OpenNest.Converters.ConvertGeometry.ToProgram(shape)));
        var plate = session.Nest.CreatePlate();
        plate.Size = new Size(20, 30);
        plate.PartSpacing = 0.2;
        plate.EdgeSpacing = new Spacing(0.5, 0.5, 0.5, 0.5);
        if (occupied) // The nest writer keeps only plates that hold parts; autonest replaces them.
            plate.Parts.Add(new Part(session.Nest.Drawings.Single()));
        return session;
    }

    [Fact]
    public void McpAutonestWithoutAnEngineUsesDefault()
    {
        var session = Session();

        var response = new NestingTools(session).AutoNestPlate(0, "square", "2");

        Assert.Contains("(Default engine): success", response);
        Assert.Equal(2, session.GetPlate(0).Parts.Count);
    }

    [Fact]
    public void McpFillToolsWithoutAnEngineUseFill()
    {
        var session = Session();
        session.DefaultEngineName = "Rectangles"; // The session default is for autonest only.

        var response = new NestingTools(session).FillPlate(0, "square", 2);

        Assert.Contains("(Fill): success", response);
        Assert.Equal(2, session.GetPlate(0).Parts.Count);
    }

    [Fact]
    public void McpAutonestEngineDescriptionNamesEveryBuiltInEngine()
    {
        var engine = typeof(NestingTools).GetMethod(nameof(NestingTools.AutoNestPlate))!
            .GetParameters().Single(p => p.Name == "engine");
        var description = engine.GetCustomAttribute<DescriptionAttribute>()!.Description;
        var builtIns = NestingEngineRegistry.AvailableEngines
            .Where(e => e.Factory().GetType().Assembly == typeof(NestingEngineRegistry).Assembly)
            .Select(e => e.Name)
            .ToList();

        Assert.Contains("Default", builtIns);
        Assert.All(builtIns, name => Assert.Matches(@"\b" + Regex.Escape(name) + @"\b", description));
        Assert.StartsWith("Whole-job engine. Default (used when omitted)", description);
    }

    [Fact]
    public void ConsoleAutonestWithoutAnEngineUsesDefault()
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(occupied: true).Nest).Write(input));
        var method = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
        var (originalOut, originalError) = (Console.Out, Console.Error);
        using var captured = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            exit = (int)method.Invoke(null, new object[] { new[] { input, "--autonest", "--quantity", "2", "--output", output } })!;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.True(exit == 0, captured.ToString());
        Assert.Contains("Engine: Default", captured.ToString());
        Assert.Equal(2, Assert.Single(new NestReader(output).Read().Plates).Parts.Count);
    }
}
