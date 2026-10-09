using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

public class McpPlateToolsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-mcp-plates-" + Guid.NewGuid());

    public McpPlateToolsTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void NegativeIndexOnLoadedNestIsNotAPlate()
    {
        var session = new NestSession { Nest = new Nest() };
        var loaded = new Plate(10, 20);
        session.Nest.Plates.Add(loaded);
        var appended = new Plate(30, 40);
        session.Plates.Add(appended);

        Assert.Null(session.GetPlate(-1));
        Assert.Equal("Error: plate -1 not found", new SetupTools(session).ClearPlate(-1));
        Assert.Same(loaded, session.GetPlate(0));
        Assert.Same(appended, session.GetPlate(1));
        Assert.Null(session.GetPlate(2));
        Assert.Single(session.Nest.Plates);
        Assert.Single(session.Plates);

        var sessionOnly = new NestSession();
        sessionOnly.Plates.Add(appended);
        Assert.Null(sessionOnly.GetPlate(-1));
        Assert.Same(appended, sessionOnly.GetPlate(0));
        Assert.Null(sessionOnly.GetPlate(1));
    }

    private static string Delete(SetupTools tools, int index) => tools.DeletePlate(index);

    [Fact]
    public void DeleteEmptyLoadedMiddlePlateRenumbersAppendedAndPersists()
    {
        var session = new NestSession { Nest = new Nest("fixture") };
        var drawing = new Drawing("square", new Program());
        session.Nest.Drawings.Add(drawing);
        var occupied = new Plate(20, 30) { Quantity = 2 };
        occupied.Parts.Add(new Part(drawing));
        var unused = new Plate(40, 50);
        var appended = new Plate(60, 70) { PartSpacing = 1.25, Quantity = 3 };
        session.Nest.Plates.Add(occupied);
        session.Nest.Plates.Add(unused);
        session.Plates.Add(appended);
        var tools = new SetupTools(session);

        var result = Delete(tools, 1);
        Assert.Contains("Deleted plate 1", result);
        Assert.Contains("2 -> 1", result);
        Assert.Single(session.Nest.Plates);
        Assert.Same(appended, session.GetPlate(1));
        Assert.Null(session.GetPlate(2));
        Assert.Contains("60.0 x 70.0", new InspectionTools(session).GetPlateInfo(1));
        Assert.Equal(2, drawing.Quantity.Nested);

        var path = Path.Combine(directory, "cleaned.nest");
        Assert.Contains("Saved nest", new InputTools(session).SaveNest(path));
        var reloaded = new NestReader(path).Read();
        // NestWriter already omits empty plates; the deletion is session cleanup,
        // and the saved file must still retain the occupied plate and its counts.
        var saved = Assert.Single(reloaded.Plates);
        Assert.Single(saved.Parts);
        Assert.Equal(20, saved.Size.Width);
        Assert.Equal(2, saved.Quantity);
        Assert.Equal(2, reloaded.Drawings.Single().Quantity.Nested);
    }

    [Fact]
    public void DeleteRefusesOccupiedCutoffAndInvalidIndicesWithoutMutation()
    {
        var session = new NestSession { Nest = new Nest("fixture") };
        var drawing = new Drawing("square", new Program());
        session.Nest.Drawings.Add(drawing);
        var occupied = new Plate(20, 30) { Quantity = 4 };
        var part = new Part(drawing);
        occupied.Parts.Add(part);
        var cutoff = new Plate(40, 50);
        var definition = new CutOff(new Vector(10, 10), CutOffAxis.Vertical);
        cutoff.CutOffs.Add(definition);
        session.Nest.Plates.Add(occupied);
        session.Plates.Add(cutoff);
        var tools = new SetupTools(session);

        Assert.Contains("occupied", Delete(tools, 0));
        Assert.Contains("cutoff", Delete(tools, 1), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not found", Delete(tools, -1));
        Assert.Contains("not found", Delete(tools, 2));
        Assert.Same(occupied, session.GetPlate(0));
        Assert.Same(cutoff, session.GetPlate(1));
        Assert.Same(part, Assert.Single(occupied.Parts));
        Assert.Same(definition, Assert.Single(cutoff.CutOffs));
        Assert.Equal(4, drawing.Quantity.Nested);
        Assert.Single(session.Nest.Plates);
        Assert.Single(session.Plates);

        // The session-only collection must also delete without requiring a loaded nest.
        var standalone = new NestSession();
        standalone.Plates.Add(new Plate(12, 13));
        Assert.Contains("Deleted plate 0", Delete(new SetupTools(standalone), 0));
        Assert.Empty(standalone.Plates);
    }
}
