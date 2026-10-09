using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

[Collection("FrontEndRegistry")]
public class McpWholeJobTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-mcp-job-" + Guid.NewGuid());
    public McpWholeJobTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static Drawing Square(string name)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        return new Drawing(name, OpenNest.Converters.ConvertGeometry.ToProgram(shape));
    }

    private static NestSession Session()
    {
        var session = new NestSession { Nest = new Nest("job") };
        session.Nest.Drawings.Add(Square("A"));
        session.Nest.Drawings.Add(Square("B"));
        session.Nest.Plates.Add(new Plate(10, 10) { PartSpacing = 0.25, Quantity = 1 });
        return session;
    }

    private static string Call(NestingTools tools, int plateIndex, string names, string quantities,
        string engine, bool noNew = false)
    {
        var method = typeof(NestingTools).GetMethod("AutoNestJob");
        Assert.NotNull(method);
        var args = method.GetParameters().Select(p => p.Name switch
        {
            "plateIndex" => (object)plateIndex,
            "drawingNames" => names,
            "quantities" => quantities,
            "engine" => engine,
            "no_new_plates" => noNew,
            _ => p.DefaultValue,
        }).ToArray();
        return (string)method.Invoke(tools, args)!;
    }

    [Fact]
    public void CreatesSheetsForCompleteMixedJobThenSavesAndReloads()
    {
        var session = Session();
        var original = session.GetPlate(0);
        var engine = "JobTwoSheets-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "two safe sheets", () => new TwoSheetStub());
        var text = Call(new NestingTools(session), 0, "A,B", "2,2", engine);
        Assert.Contains("complete", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, session.AllPlates().Count);
        Assert.Same(original, session.GetPlate(0));
        Assert.Single(session.Plates);
        Assert.All(session.AllPlates(), plate =>
        {
            Assert.Equal(2, plate.Parts.Count);
            Assert.Equal(0.25, plate.PartSpacing);
            Assert.Equal(1, plate.Quantity);
        });
        Assert.Equal(2, session.Nest.Drawings.Single(d => d.Name == "A").Quantity.Nested);
        Assert.Equal(2, session.Nest.Drawings.Single(d => d.Name == "B").Quantity.Nested);
        var path = Path.Combine(directory, "complete.nest");
        Assert.Contains("Saved nest", new InputTools(session).SaveNest(path));
        var loaded = new NestReader(path).Read();
        Assert.Equal(2, loaded.Plates.Count);
        Assert.Equal(2, loaded.Drawings.Single(d => d.Name == "A").Quantity.Nested);
        Assert.Equal(2, loaded.Drawings.Single(d => d.Name == "B").Quantity.Nested);
    }

    [Fact]
    public void NoNewPlateFlagRefusesIncompleteProposalWithoutMutation()
    {
        var session = Session();
        var original = session.GetPlate(0);
        var engine = "JobLimited-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "one sheet when capped", () => new TwoSheetStub());
        var text = Call(new NestingTools(session), 0, "A,B", "2,2", engine, noNew: true);
        Assert.Contains("incomplete", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing committed", text);
        Assert.Single(session.AllPlates());
        Assert.Same(original, session.GetPlate(0));
        Assert.Empty(original.Parts);
        Assert.All(session.Nest.Drawings, d => Assert.Equal(0, d.Quantity.Nested));
    }

    [Fact]
    public void ExistingQuantityAndPrecreatedSheetsCompleteWithRealEngine()
    {
        var session = Session();
        var occupied = session.GetPlate(0);
        occupied.Quantity = 2;
        occupied.Parts.Add(new Part(session.Nest.Drawings.Single(d => d.Name == "A")));
        var first = new Plate(3, 3) { PartSpacing = 0.25, Quantity = 1 };
        var second = new Plate(3, 3) { PartSpacing = 0.25, Quantity = 1 };
        session.Plates.Add(first);
        session.Plates.Add(second);
        var text = Call(new NestingTools(session), 1, "A,B", "3,1", "Rectangles", noNew: true);
        Assert.Contains("Whole job complete", text);
        Assert.Contains("0 new sheet(s)", text);
        Assert.Same(first, session.GetPlate(1));
        Assert.Same(second, session.GetPlate(2));
        Assert.Equal(3, session.Nest.Drawings.Single(d => d.Name == "A").Quantity.Nested);
        Assert.Equal(1, session.Nest.Drawings.Single(d => d.Name == "B").Quantity.Nested);
        Assert.Equal(3, session.AllPlates().Count);
        var path = Path.Combine(directory, "existing.nest");
        Assert.Contains("Saved nest", new InputTools(session).SaveNest(path));
        var loaded = new NestReader(path).Read();
        Assert.Equal(3, loaded.Plates.Count);
        Assert.Equal(3, loaded.Drawings.Single(d => d.Name == "A").Quantity.Nested);
        Assert.Equal(1, loaded.Drawings.Single(d => d.Name == "B").Quantity.Nested);
    }

    [Fact]
    public void RealEngineAddsSheetsWhenOnlyTemplateExists()
    {
        var session = Session();
        session.GetPlate(0).Size = new Size(3, 3);
        var text = Call(new NestingTools(session), 0, "A,B", "2,2", "Rectangles");
        Assert.Contains("Whole job complete", text);
        Assert.Equal(4, session.AllPlates().Count);
        Assert.All(session.AllPlates(), p => Assert.Single(p.Parts));
        Assert.Equal(2, session.Nest.Drawings.Single(d => d.Name == "A").Quantity.Nested);
        Assert.Equal(2, session.Nest.Drawings.Single(d => d.Name == "B").Quantity.Nested);
    }

    [Fact]
    public void InvalidPluginLayoutCannotCreateOrOccupySheets()
    {
        var session = Session();
        var original = session.GetPlate(0);
        var engine = "JobOverlap-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "invalid layout", () => new TwoSheetStub(overlap: true));
        var text = Call(new NestingTools(session), 0, "A,B", "2,2", engine);
        Assert.Contains("invalid", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing committed", text);
        Assert.Single(session.AllPlates());
        Assert.Same(original, session.GetPlate(0));
        Assert.Empty(original.Parts);
        Assert.All(session.Nest.Drawings, d => Assert.Equal(0, d.Quantity.Nested));
    }

    private sealed class TwoSheetStub(bool overlap = false) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var stock = Assert.Single(job.Plates);
            var sheets = new List<NestJobPlateResult>();
            for (var i = 0; i < System.Math.Min(2, stock.Quantity ?? 2); i++)
                sheets.Add(new NestJobPlateResult(i, stock,
                [new NestJobPlacement(job.Parts[0].Id, i, 1, 1, 0),
                 new NestJobPlacement(job.Parts[1].Id, i, overlap ? 1 : 5, 1, 0)]));
            return new NestJobResult(sheets.Count == 2 ? NestJobStatus.Complete : NestJobStatus.Incomplete,
                sheets.Count == 2 ? NestJobStopReason.Completed : NestJobStopReason.StockExhausted,
                sheets, [], []);
        }
    }
}
