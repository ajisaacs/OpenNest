using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.Bending;
using OpenNest.IO;
using OpenNest.IO.Bending;
using CadLayer = ACadSharp.Tables.Layer;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.Tests.IO;

[CollectionDefinition("Bend detector timeout registry", DisableParallelization = true)]
public class BendDetectorTimeoutRegistryCollection { }

[Collection("Bend detector timeout registry")]
public class CadImporterTimeoutTests : IDisposable
{
    private const string MarkerLayer = "TESTTIMEOUT";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-regex-" + Guid.NewGuid());
    private readonly List<MarkerDetector> registered = new();

    public CadImporterTimeoutTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        // Register has no unregister API. Disable every double after its test, and
        // gate calls on the marker layer while active. Never mutate the private registry.
        foreach (var detector in registered)
            detector.Active = false;
        Directory.Delete(directory, true);
    }

    [Fact]
    public void AutoDetect_OnTimeout_DoesNotFallThroughToSuccessfulDetector()
    {
        var document = Document(marker: true);
        // The built-in detector is registered first; this marker is not a bend line.
        Assert.Empty(new SolidWorksBendDetector().DetectBends(document));
        var throwing = Register(throws: true);
        var fallback = Register(throws: false);
        Assert.True(BendDetectorRegistry.Detectors.ToList().IndexOf(throwing)
            < BendDetectorRegistry.Detectors.ToList().IndexOf(fallback));

        var exception = Assert.Throws<RegexMatchTimeoutException>(() => BendDetectorRegistry.AutoDetect(document));

        Assert.Same(throwing.Timeout, exception);
        Assert.Equal(1, throwing.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_OnTimeout_PublishesNoResultOrRepairReportsAndPreservesSource(bool repair)
    {
        var path = WriteDxf("marker.dxf", marker: true);
        var originalHash = Hash(path);
        var detector = Register(throws: true);
        var options = new CadImportOptions
        {
            BendDetectorName = detector.Name,
            BendRepair = repair ? RepairOptions() : null,
        };
        var published = new List<CadImportResult>();
        var reports = new List<BendRepairReport>();

        var exception = Assert.Throws<RegexMatchTimeoutException>(() =>
        {
            var result = CadImporter.Import(path, options);
            // These publication steps must be unreachable, even for an empty result.
            published.Add(result);
            reports.AddRange(result.BendRepairReports);
        });

        Assert.Same(detector.Timeout, exception);
        Assert.Equal(1, detector.CallCount);
        Assert.Empty(published);
        Assert.Empty(reports);
        Assert.Equal(originalHash, Hash(path));
        // Detection precedes both repair and etch conversion in Import. No result
        // or output escapes; do not inspect its private, disposable scratch geometry.
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void ImportDrawing_AfterEarlierSuccess_AbortsBatchBeforePublication()
    {
        var good = WriteDxf("good.dxf", marker: false);
        var bad = WriteDxf("marker.dxf", marker: true);
        var hashes = new[] { Hash(good), Hash(bad) };
        var detector = Register(throws: true);
        var staged = new List<Drawing>();
        var published = new List<Drawing>();

        var exception = Assert.Throws<RegexMatchTimeoutException>(() =>
        {
            foreach (var path in new[] { good, bad })
                staged.Add(CadImporter.ImportDrawing(path, new CadImportOptions
                {
                    BendDetectorName = detector.Name,
                    BendRepair = RepairOptions(),
                }));
            published.AddRange(staged);
        });

        Assert.Same(detector.Timeout, exception);
        var earlier = Assert.Single(staged);
        Assert.Equal(good, earlier.Source.Path);
        Assert.NotEmpty(earlier.Program.Codes);
        // This first drawing is a discardable local, not a published batch result.
        Assert.Empty(published);
        Assert.Equal(1, detector.CallCount);
        Assert.Equal(hashes, new[] { Hash(good), Hash(bad) });
        Assert.Equal(new[] { good, bad }.Order(), Directory.GetFiles(directory).Order());
    }

    private MarkerDetector Register(bool throws)
    {
        var detector = new MarkerDetector(throws);
        BendDetectorRegistry.Register(detector);
        registered.Add(detector);
        return detector;
    }

    private static BendRepairOptions RepairOptions() => new()
    {
        DrawingUnits = BendRepairUnits.Inches,
        MaxEndpointMovementMillimeters = 2,
    };

    private string WriteDxf(string name, bool marker)
    {
        var path = Path.Combine(directory, name);
        DxfWriter.Write(path, Document(marker), false);
        return path;
    }

    private static CadDocument Document(bool marker)
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
        return document;
    }

    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));

    private sealed class MarkerDetector(bool throws) : IBendDetector
    {
        public string Name { get; } = "RegexTimeout-" + Guid.NewGuid();
        public bool Active { get; set; } = true;
        public int CallCount { get; private set; }
        public RegexMatchTimeoutException Timeout { get; } = new("marker", "<test>", TimeSpan.FromSeconds(1));

        public List<Bend> DetectBends(CadDocument document)
        {
            if (!Active || !document.Entities.Any(e => e.Layer?.Name == MarkerLayer))
                return new List<Bend>();
            CallCount++;
            if (throws)
                throw Timeout;
            return new List<Bend> { new() };
        }
    }
}
