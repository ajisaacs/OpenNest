using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.Api;
using OpenNest.Bending;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO.Bending;
using OpenNest.Mcp;
using OpenNest.Mcp.Tools;
using CadLayer = ACadSharp.Tables.Layer;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.FrontEnd.Tests;

// Console redirection and both registries are process-static. Reuse the existing
// nonparallel front-end collection; these tests require a Windows runtime.
[Collection("FrontEndRegistry")]
public class RegexImportFailureTests : IDisposable
{
    private const string MarkerLayer = "TESTTIMEOUT";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-regex-frontends-" + Guid.NewGuid());
    private readonly MarkerDetector detector = new();

    public RegexImportFailureTests()
    {
        Directory.CreateDirectory(directory);
        BendDetectorRegistry.Register(detector);
    }

    public void Dispose()
    {
        // No unregister API: retained registrations become inert, and active
        // registrations only throw for the marker document, never ordinary imports.
        detector.Active = false;
        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Console_TimeoutExitsOneWithoutOutputEvenAfterEarlierImport(bool earlierSuccess)
    {
        var bad = WriteDxf("marker.dxf", marker: true);
        var hash = Hash(bad);
        var output = Path.Combine(directory, "output.nest");
        var args = new List<string>();
        if (earlierSuccess)
            args.Add(WriteDxf("good.dxf", marker: false));
        args.AddRange(new[] { bad, "--size", "20x30", "--output", output,
            "--repair-bends-mm", "2", "--cad-units", "inches" });
        var originalOut = System.Console.Out;
        var originalError = System.Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            System.Console.SetOut(stdout);
            System.Console.SetError(stderr);
            Assert.Equal(1, RunConsole(args.ToArray()));
        }
        finally
        {
            System.Console.SetOut(originalOut);
            System.Console.SetError(originalError);
        }

        Assert.Equal(1, detector.CallCount);
        Assert.Contains("Error: failed to import DXF", stderr.ToString());
        Assert.Contains(bad, stderr.ToString());
        if (earlierSuccess)
            Assert.Contains("Imported: good", stdout.ToString());
        Assert.DoesNotContain("Bend repair", stdout.ToString());
        Assert.False(File.Exists(output));
        Assert.Equal(hash, Hash(bad));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_TimeoutRetainsInnerExceptionAndNeverSolvesOrReturnsResponse(bool earlierSuccess)
    {
        var bad = WriteDxf("marker.dxf", marker: true);
        var hash = Hash(bad);
        var engine = new CountingEngine();
        var engineName = "RegexImportFailure-" + Guid.NewGuid();
        NestingEngineRegistry.Register(engineName, "test", () => engine);
        var parts = new List<NestRequestPart>();
        if (earlierSuccess)
            parts.Add(new NestRequestPart { Id = "good", DxfPath = WriteDxf("good.dxf", marker: false), Quantity = 1 });
        parts.Add(new NestRequestPart { Id = "bad", DxfPath = bad, Quantity = 1 });
        var request = new NestRequest
        {
            Parts = parts,
            Plates = [new NestRequestPlate { Id = "stock", Size = new Size(20, 30), Quantity = 1 }],
            Engine = engineName,
        };
        NestResponse? response = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            response = await NestRunner.RunAsync(request));

        Assert.IsType<RegexMatchTimeoutException>(exception.InnerException);
        Assert.Same(detector.Timeout, exception.InnerException);
        Assert.Contains(bad, exception.Message);
        Assert.Equal(1, detector.CallCount);
        Assert.Equal(0, engine.CallCount);
        Assert.Null(response);
        Assert.Equal(hash, Hash(bad));
    }

    [Fact]
    public void Mcp_TimeoutReturnsErrorWithoutAddingDrawingOrChangingEarlierDrawing()
    {
        var good = WriteDxf("good.dxf", marker: false);
        var bad = WriteDxf("marker.dxf", marker: true);
        var hash = Hash(bad);
        var session = new NestSession();
        var tools = new InputTools(session);
        Assert.StartsWith("Imported drawing", tools.ImportDxf(good));
        var earlier = Assert.Single(session.Drawings);
        var earlierProgram = earlier.Program;
        var count = session.Drawings.Count;

        var result = tools.ImportDxf(bad);

        Assert.StartsWith("Error:", result);
        Assert.Contains(bad, result);
        Assert.Equal(count, session.Drawings.Count);
        Assert.Same(earlier, Assert.Single(session.Drawings));
        Assert.Same(earlierProgram, earlier.Program);
        Assert.Equal(1, detector.CallCount);
        Assert.Equal(hash, Hash(bad));
    }

    private static int RunConsole(params string[] args)
    {
        var method = Assembly.Load("OpenNest.Console").GetType("NestConsole")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
        return (int)method.Invoke(null, new object[] { args })!;
    }

    private string WriteDxf(string name, bool marker)
    {
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Inches;
        document.Entities.Add(new CadLine(new XYZ(0, 0, 0), new XYZ(2, 0, 0))
        {
            Layer = new CadLayer(marker ? MarkerLayer : "0"),
        });
        document.Entities.Add(new CadLine(new XYZ(2, 0, 0), new XYZ(2, 2, 0)));
        document.Entities.Add(new CadLine(new XYZ(2, 2, 0), new XYZ(0, 2, 0)));
        document.Entities.Add(new CadLine(new XYZ(0, 2, 0), new XYZ(0, 0, 0)));
        var path = Path.Combine(directory, name);
        DxfWriter.Write(path, document, false);
        return path;
    }

    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));

    private sealed class MarkerDetector : IBendDetector
    {
        public string Name { get; } = "FrontEndRegexTimeout-" + Guid.NewGuid();
        public bool Active { get; set; } = true;
        public int CallCount { get; private set; }
        public RegexMatchTimeoutException Timeout { get; } = new("marker", "<test>", TimeSpan.FromSeconds(1));

        public List<Bend> DetectBends(CadDocument document)
        {
            if (!Active || !document.Entities.Any(e => e.Layer?.Name == MarkerLayer))
                return new List<Bend>();
            CallCount++;
            throw Timeout;
        }
    }

    private sealed class CountingEngine : INestingEngine
    {
        public int CallCount { get; private set; }

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            CallCount++;
            throw new InvalidOperationException("Import failure must abort before solving.");
        }
    }
}
