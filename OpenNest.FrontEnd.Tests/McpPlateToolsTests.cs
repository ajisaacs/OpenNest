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
}
