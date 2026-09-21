using OpenNest.Benchmark;
using OpenNest.Geometry;

namespace OpenNest.Tests.Benchmark;

public sealed class JobLoaderManifestTests : IDisposable
{
    private static readonly string SourceDxf = Path.Combine(
        "Bending",
        "TestData",
        "4526 A14 PT11.dxf"
    );

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "opennest-manifest-" + Guid.NewGuid().ToString("N")
    );

    public JobLoaderManifestTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "parts"));
        File.Copy(SourceDxf, Path.Combine(_dir, "parts", "bracket.dxf"));
        File.Copy(SourceDxf, Path.Combine(_dir, "parts", "plate.dxf"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private string WriteManifest(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private const string ValidManifest = """
        {
          "sheetSizes": ["48x96", "60x120"],
          "spacing": 0.25,
          "edgeSpacing": 0.5,
          "quadrant": 2,
          "parts": [
            { "dxf": "parts/bracket.dxf", "quantity": 12 },
            { "dxf": "parts/plate.dxf", "quantity": 4 }
          ]
        }
        """;

    [Fact]
    public void Manifest_ImportsDxfsWithQuantitiesAndJobSettings()
    {
        var path = WriteManifest("bench.json", ValidManifest);

        var job = Assert.Single(JobLoader.Load(path));

        Assert.Equal("bench", job.Name);
        Assert.Equal(new[] { 12, 4 }, job.Requests.Select(r => r.Quantity));
        Assert.Equal(16, job.TotalRequestedQuantity);
        Assert.All(job.Requests, r => Assert.NotEmpty(r.Drawing.Program.Codes));
        Assert.Equal(new[] { new Size(48, 96), new Size(60, 120) }, job.CandidateSizes.ToArray());
        Assert.Equal(0.25, job.PartSpacing);
        Assert.Equal(0.5, job.EdgeSpacing.Left);
        Assert.Equal(0.5, job.EdgeSpacing.Top);
        Assert.Equal(2, job.Quadrant);
    }

    [Fact]
    public void Manifest_DxfPathsResolveRelativeToTheManifestNotTheWorkingDirectory()
    {
        var path = WriteManifest("bench.json", ValidManifest);
        var elsewhere = Directory.GetCurrentDirectory();
        Assert.NotEqual(_dir, elsewhere);

        var job = Assert.Single(JobLoader.Load(path));

        Assert.Equal(2, job.Requests.Count);
    }

    [Fact]
    public void Overrides_ReplaceManifestSheetSizesAndSpacing()
    {
        var path = WriteManifest("bench.json", ValidManifest);

        var job = Assert.Single(JobLoader.Load(path, new[] { new Size(10, 20) }, 0.75));

        Assert.Equal(new[] { new Size(10, 20) }, job.CandidateSizes.ToArray());
        Assert.Equal(0.75, job.PartSpacing);
    }

    [Fact]
    public void SheetSizesFromOverrideAreEnoughWhenManifestOmitsThem()
    {
        var path = WriteManifest(
            "bench.json",
            """{ "parts": [ { "dxf": "parts/bracket.dxf", "quantity": 1 } ] }"""
        );

        var job = Assert.Single(JobLoader.Load(path, new[] { new Size(48, 96) }));

        Assert.Equal(new[] { new Size(48, 96) }, job.CandidateSizes.ToArray());
        Assert.Equal(0, job.PartSpacing);
        Assert.Equal(1, job.Quadrant);
    }

    [Fact]
    public void NoSheetSizesAnywhere_ThrowsAndMentionsSheetSizes()
    {
        var path = WriteManifest(
            "bench.json",
            """{ "parts": [ { "dxf": "parts/bracket.dxf", "quantity": 1 } ] }"""
        );

        var ex = Assert.Throws<InvalidOperationException>(() => JobLoader.Load(path));

        Assert.Contains("sheet size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnparseableSheetSize_ThrowsNamingTheValue()
    {
        var path = WriteManifest(
            "bench.json",
            """{ "sheetSizes": ["huge"], "parts": [ { "dxf": "parts/bracket.dxf", "quantity": 1 } ] }"""
        );

        var ex = Assert.Throws<InvalidOperationException>(() => JobLoader.Load(path));

        Assert.Contains("huge", ex.Message);
    }

    [Fact]
    public void MissingDxf_ThrowsNamingTheEntry()
    {
        var path = WriteManifest(
            "bench.json",
            """{ "sheetSizes": ["48x96"], "parts": [ { "dxf": "parts/nope.dxf", "quantity": 1 } ] }"""
        );

        var ex = Assert.Throws<FileNotFoundException>(() => JobLoader.Load(path));

        Assert.Contains("nope.dxf", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveQuantity_ThrowsNamingTheEntry(int quantity)
    {
        var path = WriteManifest(
            "bench.json",
            $$"""{ "sheetSizes": ["48x96"], "parts": [ { "dxf": "parts/bracket.dxf", "quantity": {{quantity}} } ] }"""
        );

        var ex = Assert.Throws<InvalidOperationException>(() => JobLoader.Load(path));

        Assert.Contains("bracket.dxf", ex.Message);
        Assert.Contains("quantity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyPartsList_Throws()
    {
        var path = WriteManifest("bench.json", """{ "sheetSizes": ["48x96"], "parts": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => JobLoader.Load(path));

        Assert.Contains("parts", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowRotationFalse_LocksThatPartsRotationInTheNestJob()
    {
        var path = WriteManifest(
            "bench.json",
            """
            {
              "sheetSizes": ["48x96"],
              "parts": [
                { "dxf": "parts/bracket.dxf", "quantity": 2 },
                { "dxf": "parts/plate.dxf", "quantity": 2, "allowRotation": false }
              ]
            }
            """
        );

        var job = Assert.Single(JobLoader.Load(path));
        var nestJob = job.BuildNestJob(maxPlates: 4);

        Assert.Equal(RotationPolicyKind.Automatic, nestJob.Parts[0].Rotation.Kind);

        // The legacy lock (step 2π, start = end = 0) reaches the engine as a single-angle sweep at 0.
        var locked = nestJob.Parts[1].Rotation;
        Assert.Equal(RotationPolicyKind.BoundedSweep, locked.Kind);
        Assert.Equal(0, locked.Start);
        Assert.Equal(0, locked.End);
    }

    [Fact]
    public void Folder_LoadsManifestSuffixedFilesAndIgnoresOtherJson()
    {
        WriteManifest("a.manifest.json", ValidManifest);
        WriteManifest("b.manifest.json", ValidManifest);
        WriteManifest("results.json", """{ "not": "a manifest" }""");

        var jobs = JobLoader.Load(_dir);

        Assert.Equal(new[] { "a.manifest", "b.manifest" }, jobs.Select(j => j.Name));
    }
}
