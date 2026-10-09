using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

[Collection("FrontEndRegistry")]
public class McpMixReportingTests
{
    private static Drawing Rectangle(string name, double width, double length)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(width, 0)));
        shape.Entities.Add(new Line(new Vector(width, 0), new Vector(width, length)));
        shape.Entities.Add(new Line(new Vector(width, length), new Vector(0, length)));
        shape.Entities.Add(new Line(new Vector(0, length), new Vector(0, 0)));
        return new Drawing(name, OpenNest.Converters.ConvertGeometry.ToProgram(shape));
    }

    private static NestSession Session()
    {
        var session = new NestSession();
        session.Drawings.Add(Rectangle("A", 2, 2));
        session.Drawings.Add(Rectangle("B", 20, 20));
        session.Plates.Add(new Plate(10, 10));
        return session;
    }

    [Fact]
    public void PackReportsZeroForUnfitDrawingAndCountsOnlyNewParts()
    {
        var session = Session();
        var plate = session.GetPlate(0);
        plate.Parts.Add(new Part(session.Drawings[0]));
        var text = new NestingTools(session).PackPlate(0, "A,B", "10,10");
        Assert.Contains("A: requested=10", text);
        Assert.Contains("B: requested=10, placed=0, remaining=10", text);
        Assert.Contains("remaining demand", text);
        Assert.DoesNotContain("ratio not met", text);
        Assert.Contains($"A: requested=10, placed={plate.Parts.Count - 1}, remaining={11 - plate.Parts.Count}", text);
        Assert.True(plate.Parts.Count > 1);
    }

    [Fact]
    public void AutoNestReportsSkewedAndZeroProgressByRequirement()
    {
        var session = Session();
        var engine = "MixStub-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "test", () => new Stub());
        var tool = new NestingTools(session);
        var text = tool.AutoNestPlate(0, "A,B", "2,2", engine);
        Assert.Contains("A: requested=2, placed=1, remaining=1", text);
        Assert.Contains("B: requested=2, placed=0, remaining=2", text);
        Assert.Contains("remaining demand", text);
        Assert.DoesNotContain("ratio not met", text);

        // Duplicate tokens must be refused before the engine can mutate a fresh target.
        var fresh = Session();
        Assert.Contains("duplicate", new NestingTools(fresh).AutoNestPlate(0, "A,A", "2,2", engine), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fresh.GetPlate(0).Parts);
        Assert.Single(session.GetPlate(0).Parts);
    }

    [Fact]
    public void PackZeroProgressAndAmbiguousNamesAreExplicit()
    {
        var session = Session();
        session.GetPlate(0).Size = new Size(1, 1);
        var text = new NestingTools(session).PackPlate(0, "A,B", "2,2");
        Assert.Contains("A: requested=2, placed=0, remaining=2", text);
        Assert.Contains("B: requested=2, placed=0, remaining=2", text);
        Assert.Contains("zero progress", text);
        Assert.Empty(session.GetPlate(0).Parts);

        session.Drawings.Add(Rectangle("A", 1, 1));
        Assert.Contains("ambiguous drawing name", new NestingTools(session).PackPlate(0, "A,B", "2,2"));
        Assert.Empty(session.GetPlate(0).Parts);
    }

    [Fact]
    public void ZeroDemandHasZeroRemainingAndNegativeDemandIsRejected()
    {
        var session = Session();
        var tools = new NestingTools(session);
        var text = tools.PackPlate(0, "A", "0");
        Assert.Contains("A: requested=0, placed=0, remaining=0", text);
        Assert.Contains("zero progress", text);
        Assert.Contains("negative", tools.PackPlate(0, "A", "-1"), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.GetPlate(0).Parts);
    }

    private sealed class Stub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var placed = new[] { new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0) };
            return new NestJobResult(NestJobStatus.Incomplete, NestJobStopReason.Completed,
                [new NestJobPlateResult(0, job.Plates[0], placed)],
                [], []);
        }
    }
}
