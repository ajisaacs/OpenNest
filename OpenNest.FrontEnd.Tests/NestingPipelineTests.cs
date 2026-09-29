using System.Reflection;
using OpenNest.Api;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

[CollectionDefinition("FrontEndRegistry", DisableParallelization = true)]
public class FrontEndRegistryCollection { }

[Collection("FrontEndRegistry")]
public class NestingPipelineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-frontends-" + Guid.NewGuid());

    public NestingPipelineTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static Drawing Square()
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        return new Drawing("square", OpenNest.Converters.ConvertGeometry.ToProgram(shape));
    }

    private static NestSession Session(bool occupied = false)
    {
        var session = new NestSession { Nest = new Nest("fixture") };
        session.Nest.Drawings.Add(Square());
        var plate = session.Nest.CreatePlate();
        plate.Size = new Size(20, 30);
        plate.PartSpacing = 0.2;
        plate.EdgeSpacing = new Spacing(0.5, 0.5, 0.5, 0.5);
        plate.Quantity = 7;
        if (occupied)
            plate.Parts.Add(new Part(session.Nest.Drawings.Single()));
        return session;
    }

    private static string Engine(Func<NestJob, CancellationToken, NestJobResult> solve)
    {
        var name = "FrontEnd-" + Guid.NewGuid();
        NestingEngineRegistry.Register(name, "test", () => new Stub(solve));
        return name;
    }

    private sealed class Stub(Func<NestJob, CancellationToken, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => solve(job, token);
    }

    private static NestJobResult Result(NestJob job, string mode)
    {
        var poses = mode switch
        {
            "overlap" => new[] { new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0), new NestJobPlacement(job.Parts[0].Id, 1, 1, 1, 0) },
            "unknown" => new[] { new NestJobPlacement("ghost", 0, 1, 1, 0) },
            "nan" => new[] { new NestJobPlacement(job.Parts[0].Id, 0, double.NaN, 1, 0) },
            _ => new[] { new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0) },
        };
        var sheets = mode == "empty" ? Array.Empty<NestJobPlateResult>()
            : mode == "multi" ? new[] { new NestJobPlateResult(0, job.Plates[0], poses), new NestJobPlateResult(1, job.Plates[0], poses) }
            : new[] { new NestJobPlateResult(0, job.Plates[0], poses) };
        // Deliberately lying metadata: front ends must count actual poses.
        return new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed, sheets,
            job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, 999, 0)),
            new[] { new StockUsage(job.Plates[0].Id, 999, 0) });
    }

    private static int RunConsole(params string[] args)
    {
        var method = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
        return (int)method.Invoke(null, new object[] { args })!;
    }

    [Theory]
    [InlineData("overlap", false, 2)]
    [InlineData("overlap", true, 0)]
    [InlineData("multi", true, 2)]
    [InlineData("unknown", true, 2)]
    [InlineData("nan", true, 2)]
    public void Console_RefusesInvalidWithoutOverwritingUnlessFaithfullyKeepable(string mode, bool allow, int expected)
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        var original = File.ReadAllBytes(input);
        File.WriteAllText(output, "must survive rejection");
        var engine = Engine((job, _) => Result(job, mode));
        var args = new List<string> { input, "--autonest", "--engine", engine, "--quantity", "2", "--output", output };
        if (allow) args.Add("--allow-invalid");
        Assert.Equal(expected, RunConsole(args.ToArray()));
        Assert.Equal(original, File.ReadAllBytes(input));
        if (expected == 2)
            Assert.Equal("must survive rejection", File.ReadAllText(output));
        else
        {
            var nest = new NestReader(output).Read();
            Assert.Equal(2, nest.Plates[0].Parts.Count);
            Assert.Equal(1, nest.Plates[0].Quantity);
            Assert.Equal(2, nest.Drawings.Single().Quantity.Nested);
        }
    }

    [Fact]
    public void Console_EmptyResultDoesNotOverwriteOutputOrOriginal()
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        var original = File.ReadAllBytes(input);
        File.WriteAllText(output, "keep existing output");
        var engine = Engine((job, _) => Result(job, "empty"));
        Assert.Equal(2, RunConsole(input, "--autonest", "--engine", engine, "--output", output));
        Assert.Equal(original, File.ReadAllBytes(input));
        Assert.Equal("keep existing output", File.ReadAllText(output));
    }

    [Fact]
    public void Console_CancelledSolveDoesNotSave()
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        var engine = Engine((_, _) => throw new OperationCanceledException());
        Assert.Equal(2, RunConsole(input, "--autonest", "--engine", engine, "--output", output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Console_KeepPartsOnOccupiedSheetRejectsBeforeSolve()
    {
        var input = Path.Combine(directory, "input.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        var original = File.ReadAllBytes(input);
        var engine = Engine((_, _) => throw new Xunit.Sdk.XunitException("must not solve occupied stock"));
        Assert.Equal(2, RunConsole(input, "--autonest", "--keep-parts", "--engine", engine));
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Fact]
    public void Console_ValidCommitPreservesSettingsAndUsesOnePhysicalSheet()
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        var engine = Engine((job, _) =>
        {
            Assert.Equal(1, Assert.Single(job.Plates).Quantity);
            return Result(job, "valid");
        });
        Assert.Equal(0, RunConsole(input, "--autonest", "--engine", engine, "--output", output));
        var plate = Assert.Single(new NestReader(output).Read().Plates);
        Assert.Single(plate.Parts);
        Assert.Equal(1, plate.Quantity);
        Assert.Equal(0.2, plate.PartSpacing);
        Assert.Equal(new Spacing(0.5, 0.5, 0.5, 0.5), plate.EdgeSpacing);
    }

    [Theory]
    [InlineData("overlap", false, false)]
    [InlineData("overlap", true, true)]
    [InlineData("multi", true, false)]
    [InlineData("unknown", true, false)]
    [InlineData("nan", true, false)]
    [InlineData("valid", false, true)]
    public void Mcp_OnlyCommitsFaithfulSingleSheet(string mode, bool allow, bool committed)
    {
        var session = Session();
        var plate = session.GetPlate(0);
        var engine = Engine((job, _) =>
        {
            Assert.Equal(1, Assert.Single(job.Plates).Quantity);
            return Result(job, mode);
        });
        var tool = new NestingTools(session);
        var result = CallMcp(tool, engine, allow);
        if (committed)
        {
            Assert.NotEmpty(plate.Parts);
            Assert.Equal(1, plate.Quantity);
            Assert.All(plate.Parts, p => Assert.Same(session.Nest.Drawings.Single(), p.BaseDrawing));
            Assert.Equal(plate.Parts.Count, session.Nest.Drawings.Single().Quantity.Nested);
        }
        else
        {
            Assert.Contains("Error", result);
            Assert.Empty(plate.Parts);
            Assert.Equal(7, plate.Quantity);
        }
        if (mode != "valid") Assert.Contains("Violation", result);
    }

    private static string CallMcp(NestingTools tool, string engine, bool allow = false, CancellationToken token = default)
    {
        // Reflection lets RED exercise the old implementation before optional parameters exist.
        var method = typeof(NestingTools).GetMethod(nameof(NestingTools.AutoNestPlate))!;
        var args = method.GetParameters().Select(p => p.Name switch
        {
            "plateIndex" => (object)0,
            "drawingNames" => "square",
            "quantities" => "2",
            "engine" => engine,
            "allow_invalid" => allow,
            "cancellationToken" => token,
            _ => p.DefaultValue,
        }).ToArray();
        try { return (string)method.Invoke(tool, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        { throw ex.InnerException; }
    }

    [Fact]
    public void Mcp_OccupiedSheetRejectsBeforeSolve()
    {
        var session = Session(true);
        var original = session.GetPlate(0).Parts[0];
        var engine = Engine((_, _) => throw new Xunit.Sdk.XunitException("must not solve occupied stock"));
        Assert.Contains("occupied", CallMcp(new NestingTools(session), engine));
        Assert.Same(original, Assert.Single(session.GetPlate(0).Parts));
    }

    [Fact]
    public void Mcp_CancellationDoesNotCommit()
    {
        var session = Session();
        using var cts = new CancellationTokenSource();
        var engine = Engine((job, _) => { cts.Cancel(); return Result(job, "valid"); });
        Assert.ThrowsAny<OperationCanceledException>(() => CallMcp(new NestingTools(session), engine, token: cts.Token));
        Assert.Empty(session.GetPlate(0).Parts);
    }

    [Fact]
    public void UnknownEngineDoesNotWriteOrCommit()
    {
        var session = Session();
        Assert.Contains("unknown", CallMcp(new NestingTools(session), "missing-engine"), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.GetPlate(0).Parts);
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(session.Nest).Write(input));
        Assert.Equal(1, RunConsole(input, "--autonest", "--engine", "missing-engine", "--output", output));
        Assert.False(File.Exists(output));
    }

    private NestRequest Request(string engine)
    {
        var path = Path.Combine(directory, "square.dxf");
        Dxf.ExportProgram(Square().Program, path);
        var request = new NestRequest
        {
            Parts = [new NestRequestPart { Id = "external-id", DxfPath = path, Quantity = 2, AllowRotation = false }],
            Plates = [new NestRequestPlate { Id = "stock", Size = new Size(20, 30), Quantity = 1 }],
        };
        typeof(NestRequest).GetProperty("Engine")?.SetValue(request, engine);
        return request;
    }

    [Theory]
    [InlineData("valid", 1, "Valid")]
    [InlineData("empty", 0, "Valid")]
    [InlineData("overlap", 2, "Invalid")]
    [InlineData("unknown", 0, "Unrepresentable")]
    [InlineData("nan", 0, "Unrepresentable")]
    public async Task Api_PluginSelectionDerivesCountsPreservesIdsAndReportsValidation(string mode, int placed, string validation)
    {
        var called = false;
        var engine = Engine((job, _) => { called = true; return Result(job, mode); });
        var response = await NestRunner.RunAsync(Request(engine));
        Assert.True(called);
        var fulfillment = Assert.Single(response.Fulfillment);
        Assert.Equal("external-id", fulfillment.PartId);
        Assert.Equal(placed, fulfillment.Placed);
        Assert.Equal(2 - placed, fulfillment.Unplaced);
        Assert.Equal(placed == 2 ? NestJobStatus.Complete : NestJobStatus.Incomplete, response.Status);
        Assert.Equal(validation, typeof(NestResponse).GetProperty("ValidationStatus")?.GetValue(response)?.ToString());
        Assert.Equal(placed > 0 ? 1 : 0, Assert.Single(response.StockUsage).Used);
        Assert.Equal(placed, response.Nest.Plates.Sum(p => p.Parts.Count));
        Assert.Equal("external-id", Assert.Single(response.Nest.Drawings).Name);
        var archive = Path.Combine(directory, "result.nestquote");
        await response.SaveAsync(archive);
        var loaded = await NestResponse.LoadAsync(archive);
        Assert.Equal(validation, typeof(NestResponse).GetProperty("ValidationStatus")?.GetValue(loaded)?.ToString());
    }

    [Fact]
    public async Task Mcp_SessionGateSerializesCallsBeforeCheckingOccupiedTarget()
    {
        var session = Session();
        var tool = new NestingTools(session);
        var engine = Engine((job, _) => Result(job, "valid"));
        using var gate = new SessionToolGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.RunAsync(async token =>
        {
            entered.SetResult();
            await release.Task;
            return CallMcp(tool, engine, token: token);
        }, CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = gate.RunAsync(token => new ValueTask<string>(CallMcp(tool, engine, token: token)),
            CancellationToken.None).AsTask();
        try
        {
            Assert.False(second.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Contains("success", await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("occupied", await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(session.GetPlate(0).Parts);
    }

    [Fact]
    public void Console_CancelledOrdinaryFillDoesNotOverwriteOutput()
    {
        var input = Path.Combine(directory, "input.nest");
        var output = Path.Combine(directory, "output.nest");
        Assert.True(new NestWriter(Session(true).Nest).Write(input));
        File.WriteAllText(output, "existing output");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var run = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("RunCore", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => run.Invoke(null,
            new object[] { new[] { input, "--quantity", "1", "--output", output }, cts.Token }));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal("existing output", File.ReadAllText(output));
    }

    [Fact]
    public async Task Api_UnknownEngineAndCancellationPropagate()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => NestRunner.RunAsync(Request("missing-engine")));
        using var cts = new CancellationTokenSource();
        var engine = Engine((job, _) => { cts.Cancel(); return Result(job, "valid"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NestRunner.RunAsync(Request(engine), token: cts.Token));
    }
}
