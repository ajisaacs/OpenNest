using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
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

    private static Drawing Square(string name, double size = 2)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(size, 0)));
        shape.Entities.Add(new Line(new Vector(size, 0), new Vector(size, size)));
        shape.Entities.Add(new Line(new Vector(size, size), new Vector(0, size)));
        shape.Entities.Add(new Line(new Vector(0, size), new Vector(0, 0)));
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
        string engine, bool noNew = false, CancellationToken token = default) =>
        tools.AutoNestJob(plateIndex, names, quantities, engine, noNew, token);

    [Fact]
    public void FiniteSheetInputsReachEngineWithTemplateSettings()
    {
        var session = Session();
        var template = session.GetPlate(0);
        template.Size = new Size(60, 120);
        template.EdgeSpacing = new Spacing(1, 2, 3, 4);
        template.Quadrant = 3;
        var engine = "StockMap-" + Guid.NewGuid();
        var called = false;
        NestingEngineRegistry.Register(engine, "inspect finite stock", () => new InspectStub(job =>
        {
            called = true;
            Assert.Equal(2, job.Plates.Count);
            Assert.Equal(["stock-0", "stock-1"], job.Plates.Select(s => s.Id));
            Assert.Equal([new Size(60, 120), new Size(48, 96)], job.Plates.Select(s => s.Size));
            Assert.Equal([1, 2], job.Plates.Select(s => s.Quantity));
            Assert.All(job.Plates, stock =>
            {
                Assert.Equal(0.25, stock.PartSpacing);
                Assert.Equal(template.EdgeSpacing, stock.EdgeSpacing);
                Assert.Equal(3, stock.Quadrant);
            });
            Assert.Equal(2, job.Options.MaxPlates); // capped by demand, not plate.Quantity
            return new NestJobResult(NestJobStatus.Incomplete, NestJobStopReason.StockExhausted, [], [], []);
        }));
        var rows = new[] { new SheetStockInput { Width = 60, Length = 120, Quantity = 1 },
            new SheetStockInput { Width = 48, Length = 96, Quantity = 2 } };
        var tool = new NestingTools(session);
        Assert.Contains("incomplete", tool.AutoNestJob(0, "A,B", "1,1", engine, sheets: rows));
        Assert.True(called);
        called = false;
        rows[1].Width = double.NaN;
        Assert.Contains("sheets[1]", tool.AutoNestJob(0, "A,B", "1,1", engine, sheets: rows));
        Assert.False(called);
        Assert.Single(session.AllPlates());
        Assert.Empty(template.Parts);
        Assert.Equal(1, template.Quantity); // no inventory drawn from the template quantity
    }

    private sealed class InspectStub(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => solve(job);
    }

    [Fact]
    public void FiniteMixedSheetsCommitByStockIdAndRoundTrip()
    {
        var session = Session();
        var template = session.GetPlate(0);
        template.Size = new Size(6, 6);
        template.Quantity = 9; // repeat count is not inventory
        var small = session.Nest.Drawings.Single(d => d.Name == "B");
        var large = Square("large", 4); // four-by-four cannot fit a three-by-three sheet
        session.Nest.Drawings.Add(large);
        var rows = new[] { new SheetStockInput { Width = 6, Length = 6, Quantity = 1 },
            new SheetStockInput { Width = 3, Length = 3, Quantity = 2 } };
        var engine = "MixedStock-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "two different sheets", () => new InspectStub(job =>
        {
            Assert.Equal(2, job.Options.MaxPlates);
            Assert.Equal([1, 2], job.Plates.Select(s => s.Quantity));
            // Small stock first: result ordinal must not resize/reuse the large template.
            return new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
                [new NestJobPlateResult(0, job.Plates[1], [new NestJobPlacement(job.Parts[1].Id, 0, 0.5, 0.5, 0)]),
                 new NestJobPlateResult(1, job.Plates[0], [new NestJobPlacement(job.Parts[0].Id, 1, 0.5, 0.5, 0)])], [], []);
        }));
        var text = new NestingTools(session).AutoNestJob(0, "large,B", "1,1", engine, sheets: rows);
        Assert.Contains("Whole job complete", text);
        Assert.Contains("stock-0 (6x6): used=1, remaining=0, reused=1, created=0", text);
        Assert.Contains("stock-1 (3x3): used=1, remaining=1, reused=0, created=1", text);
        Assert.Contains("plate 0 -> stock-0", text);
        Assert.Contains("plate 1 -> stock-1", text);
        Assert.Same(template, session.GetPlate(0));
        Assert.Equal(new Size(6, 6), template.Size);
        Assert.Same(large, Assert.Single(template.Parts).BaseDrawing);
        Assert.Same(small, Assert.Single(session.GetPlate(1).Parts).BaseDrawing);
        Assert.Equal(1, template.Quantity);
        var path = Path.Combine(directory, "mixed.nest");
        Assert.Contains("Saved nest", new InputTools(session).SaveNest(path));
        var reloaded = new NestReader(path).Read();
        Assert.Equal([new Size(6, 6), new Size(3, 3)], reloaded.Plates.Select(p => p.Size));
        Assert.All(reloaded.Plates, p => Assert.Equal(1, p.Quantity));
        Assert.Equal(1, reloaded.Drawings.Single(d => d.Name == "large").Quantity.Nested);
        Assert.Equal(1, reloaded.Drawings.Single(d => d.Name == "B").Quantity.Nested);
    }

    [Fact]
    public void FiniteStockOverdrawIsRejectedBeforeAnySessionMutation()
    {
        var session = Session();
        var plate = session.GetPlate(0);
        plate.Size = new Size(3, 3);
        var engine = "Overdraw-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "overdraw stock", () => new InspectStub(job =>
            new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
                [new NestJobPlateResult(0, job.Plates[0], [new NestJobPlacement(job.Parts[0].Id, 0, 0.5, 0.5, 0)]),
                 new NestJobPlateResult(1, job.Plates[0], [new NestJobPlacement(job.Parts[1].Id, 1, 0.5, 0.5, 0)])], [], [])));
        var text = new NestingTools(session).AutoNestJob(0, "A,B", "1,1", engine,
            sheets: [new SheetStockInput { Width = 3, Length = 3, Quantity = 1 },
                new SheetStockInput { Width = 6, Length = 6, Quantity = 2 }]);
        Assert.Contains("stock-0", text);
        Assert.Contains("invalid", text, StringComparison.OrdinalIgnoreCase);
        Assert.Single(session.AllPlates());
        Assert.Empty(plate.Parts);
        Assert.All(session.Nest.Drawings, d => Assert.Equal(0, d.Quantity.Nested));
    }

    [Fact]
    public void FiniteStockRejectsInvalidInventoryBeforeSolving()
    {
        var session = Session();
        var plate = session.GetPlate(0);
        var engine = "NeverSolve-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "should not be called", () => new InspectStub(_ =>
            throw new Xunit.Sdk.XunitException("invalid input reached solver")));
        var tools = new NestingTools(session);
        var valid = new SheetStockInput { Width = 10, Length = 10, Quantity = 1 };
        Assert.Contains("at least one", tools.AutoNestJob(0, "A", "1", engine, sheets: []));
        Assert.Contains("sheets[0]", tools.AutoNestJob(0, "A", "1", engine,
            sheets: [new SheetStockInput { Width = 10, Length = 10, Quantity = 0 }]));
        Assert.Contains("sheets[0]", tools.AutoNestJob(0, "A", "1", engine,
            sheets: [new SheetStockInput { Width = double.PositiveInfinity, Length = 10, Quantity = 1 }]));
        Assert.Contains("sheets[0]", tools.AutoNestJob(0, "A", "1", engine,
            sheets: [new SheetStockInput { Width = 0, Length = 10, Quantity = 1 }]));
        Assert.Contains("sheets[1] duplicates sheets[0]", tools.AutoNestJob(0, "A", "1", engine,
            sheets: [valid, new SheetStockInput { Width = 10, Length = 10, Quantity = 2 }]));
        Assert.Contains("sheet inventory exceeds", tools.AutoNestJob(0, "A", "1", engine,
            sheets: [new SheetStockInput { Width = 10, Length = 10, Quantity = int.MaxValue },
                new SheetStockInput { Width = 11, Length = 11, Quantity = 1 }]));
        Assert.Contains("cannot be combined", tools.AutoNestJob(0, "A", "1", engine,
            no_new_plates: true, sheets: [valid]));
        Assert.Single(session.AllPlates());
        Assert.Empty(plate.Parts);
        Assert.Equal(1, plate.Quantity);
    }

    [Fact]
    public void FiniteInventoryUsesOnlyOneOfTwoMatchingEmptySheets()
    {
        var session = Session();
        var original = session.GetPlate(0);
        session.Plates.Add(new Plate(10, 10) { PartSpacing = 0.25, Quantity = 5 });
        var text = new NestingTools(session).AutoNestJob(0, "A", "1", "Rectangles",
            sheets: [new SheetStockInput { Width = 10, Length = 10, Quantity = 1 }]);
        Assert.Contains("Whole job complete", text);
        Assert.Contains("stock-0 (10x10): used=1, remaining=0", text);
        Assert.Single(original.Parts);
        Assert.Empty(session.GetPlate(1).Parts);
        Assert.Equal(5, session.GetPlate(1).Quantity);
    }

    [Fact]
    public void LaterSelectedTemplateIsReusedBeforeOtherCompatibleEmptySheet()
    {
        var session = Session();
        var earlier = session.GetPlate(0);
        var selected = new Plate(10, 10) { PartSpacing = 0.25, Quantity = 3 };
        session.Plates.Add(selected);
        var text = new NestingTools(session).AutoNestJob(1, "A", "1", "Rectangles");
        Assert.Contains("Whole job complete", text);
        Assert.Empty(earlier.Parts);
        Assert.Single(selected.Parts);
        Assert.Equal(1, selected.Quantity);
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

    [Fact]
    public void RestrictedDrawingRejectsPluginRotationWithoutMutationAfterReload()
    {
        var session = Session();
        var drawing = session.Nest.Drawings.Single(d => d.Name == "A");
        drawing.Constraints = new NestConstraints
        {
            StepAngle = OpenNest.Math.Angle.TwoPI,
            StartAngle = 0,
            EndAngle = 0,
        };
        session.GetPlate(0).Parts.Add(new Part(session.Nest.Drawings.Single(d => d.Name == "B")));
        var path = Path.Combine(directory, "rotation.nest");
        Assert.True(new NestWriter(session.Nest).Write(path));
        session = new NestSession { Nest = new NestReader(path).Read() };
        Assert.Equal(OpenNest.Math.Angle.TwoPI, session.Nest.Drawings.Single(d => d.Name == "A").Constraints.StepAngle);
        session.Plates.Add(new Plate(10, 10) { PartSpacing = 0.25, Quantity = 1 });
        var engine = "JobForbiddenRotation-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "rotated proposal", () => new RotatedStub());
        var plateTool = new NestingTools(session).AutoNestPlate(1, "A", "1", engine);
        Assert.Contains("Violation", plateTool);
        Assert.Contains("rotation", plateTool, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.GetPlate(1).Parts);
        var text = Call(new NestingTools(session), 1, "A", "1", engine);
        Assert.True(text.Contains("rotation", StringComparison.OrdinalIgnoreCase), text);
        Assert.Contains("Nothing committed", text);
        Assert.Equal(2, session.AllPlates().Count);
        Assert.Empty(session.GetPlate(1).Parts);
        Assert.Single(session.GetPlate(0).Parts);
        Assert.Equal(0, session.Nest.Drawings.Single(d => d.Name == "A").Quantity.Nested);
    }

    [Fact]
    public void EquivalentDeserializedCuttingSettingsReuseExistingSheets()
    {
        var session = Session();
        var first = session.GetPlate(0);
        first.Size = new Size(3, 3);
        first.CuttingParameters = new CuttingParameters { MachineName = "Same" };
        var second = new Plate(3, 3)
        {
            Quantity = 1,
            PartSpacing = 0.25,
            CuttingParameters = new CuttingParameters { MachineName = "Same" }
        };
        session.Plates.Add(second);
        Assert.NotSame(first.CuttingParameters, second.CuttingParameters);
        var text = Call(new NestingTools(session), 0, "A", "2", "Rectangles", noNew: true);
        Assert.Contains("Whole job complete", text);
        Assert.Equal(2, session.AllPlates().Count);
        Assert.Single(first.Parts);
        Assert.Single(second.Parts);
    }

    [Fact]
    public void DifferentCuttingSettingsAreNotReusedAsStock()
    {
        var session = Session();
        var first = session.GetPlate(0);
        first.Size = new Size(3, 3);
        first.CuttingParameters = new CuttingParameters { MachineName = "First" };
        var second = new Plate(3, 3)
        {
            Quantity = 1,
            PartSpacing = 0.25,
            CuttingParameters = new CuttingParameters { MachineName = "Other" }
        };
        session.Plates.Add(second);
        var text = Call(new NestingTools(session), 0, "A", "2", "Rectangles", noNew: true);
        Assert.Contains("incomplete", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing committed", text);
        Assert.Empty(first.Parts);
        Assert.Empty(second.Parts);
        Assert.Equal(2, session.AllPlates().Count);
    }

    [Fact]
    public void SolverNotSupportedErrorNamesActualCause()
    {
        var session = Session();
        var engine = "JobUnsupported-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "unsupported solver", () => new UnsupportedStub());
        var text = Call(new NestingTools(session), 0, "A", "1", engine);
        Assert.Contains("unsupported fixture", text);
        Assert.DoesNotContain("Use autonest_plate", text);
        Assert.Contains("Nothing committed", text);
        Assert.Empty(session.GetPlate(0).Parts);
    }

    [Fact]
    public void CancellationAfterPluginIgnoresTokenDoesNotCommit()
    {
        var session = Session();
        using var cts = new CancellationTokenSource();
        var engine = "JobCancel-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engine, "cancels after solving", () => new CancelStub(cts));
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Call(new NestingTools(session), 0, "A,B", "2,2", engine, token: cts.Token));
        Assert.Single(session.AllPlates());
        Assert.Empty(session.GetPlate(0).Parts);
        Assert.All(session.Nest.Drawings, d => Assert.Equal(0, d.Quantity.Nested));
    }

    private sealed class CancelStub(CancellationTokenSource source) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var result = new TwoSheetStub().Solve(job, progress, token);
            source.Cancel();
            return result;
        }
    }

    private sealed class RotatedStub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var stock = Assert.Single(job.Plates);
            return new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
                [new NestJobPlateResult(0, stock,
                    [new NestJobPlacement(job.Parts[0].Id, 0, 4, 4, System.Math.PI / 2)])], [], []);
        }
    }

    private sealed class UnsupportedStub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => throw new NotSupportedException("unsupported fixture");
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
