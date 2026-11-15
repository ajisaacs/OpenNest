using System.Xml.Linq;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Desktop.Tests;

public sealed class LibraryDrawingExportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"opennest-library-export-{Guid.NewGuid():N}");

    public LibraryDrawingExportTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    [Theory]
    [InlineData(1, LibraryExportFormat.Dxf)]
    [InlineData(2, LibraryExportFormat.Svg)]
    [InlineData(3, LibraryExportFormat.Nest)]
    public void FromFilterIndex_FollowsTheDialogFilter(int index, LibraryExportFormat expected)
    {
        Assert.Equal(expected, LibraryDrawingExport.FromFilterIndex(index));
        var patterns = LibraryDrawingExport.DialogFilter.Split('|').Where((_, i) => i % 2 == 1).ToArray();
        Assert.Equal("*" + LibraryDrawingExport.Extension(expected), patterns[index - 1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void FromFilterIndex_RefusesOtherIndexes(int index) =>
        Assert.Throws<ArgumentException>(() => LibraryDrawingExport.FromFilterIndex(index));

    [Theory]
    [InlineData("BRKT-1", LibraryExportFormat.Dxf, "BRKT-1.dxf")]
    [InlineData(" A/B:C ", LibraryExportFormat.Svg, "A_B_C.svg")]
    [InlineData("", LibraryExportFormat.Nest, "Drawing.nest")]
    public void SuggestedFileName_IsTheSafeDrawingName(string name, LibraryExportFormat format, string expected) =>
        Assert.Equal(expected, LibraryDrawingExport.SuggestedFileName(new DrawingLibraryRecord { Name = name }, format));

    [Fact]
    public void PlanFolder_NumbersNamesTheBatchAlreadyTook()
    {
        var records = new[] { "Plate", "plate", "Other", "Plate" }
            .Select(name => new DrawingLibraryRecord { Id = Guid.NewGuid(), Name = name });

        var targets = LibraryDrawingExport.PlanFolder(records, folder, LibraryExportFormat.Dxf);

        Assert.Equal(new[] { "Plate.dxf", "plate (2).dxf", "Other.dxf", "Plate (3).dxf" },
            targets.Select(target => Path.GetFileName(target.Path)));
        Assert.All(targets, target => Assert.Equal(folder, Path.GetDirectoryName(target.Path)));
    }

    [Fact]
    public void Write_Dxf_ReadsBackAsTheDrawingsGeometry()
    {
        var path = Path.Combine(folder, "part.dxf");

        LibraryDrawingExport.Write(Package(Units.Inches), path, LibraryExportFormat.Dxf);

        var box = Dxf.GetGeometry(path).Cast<IBoundable>().GetBoundingBox();
        Assert.Equal(4, box.Width, 6);
        Assert.Equal(4, box.Length, 6);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void Write_Svg_UsesThePackagesUnits()
    {
        var path = Path.Combine(folder, "part.svg");

        LibraryDrawingExport.Write(Package(Units.Millimeters), path, LibraryExportFormat.Svg);

        Assert.EndsWith("mm", XDocument.Load(path).Root!.Attribute("width")!.Value);
    }

    [Fact]
    public void Write_Nest_IsThePackageAsStored()
    {
        var path = Path.Combine(folder, "part.nest");
        var package = Package(Units.Inches);

        LibraryDrawingExport.Write(package, path, LibraryExportFormat.Nest);

        Assert.Equal(package, File.ReadAllBytes(path));
    }

    [Fact]
    public void Write_ADamagedPackage_LeavesTheExistingFileAlone()
    {
        var path = Path.Combine(folder, "part.nest");
        File.WriteAllText(path, "keep me");

        Assert.Throws<InvalidDataException>(() =>
            LibraryDrawingExport.Write(new byte[] { 1, 2, 3 }, path, LibraryExportFormat.Nest));

        Assert.Equal("keep me", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task ExportAsync_WritesWhatItCan_AndReportsTheRest()
    {
        var library = new FakeLibrary();
        var good = library.Add("Good", Package(Units.Inches));
        var gone = new DrawingLibraryRecord { Id = Guid.NewGuid(), Revision = 1, Name = "Gone" };
        var damaged = library.Add("Damaged", new byte[] { 9 });
        var targets = LibraryDrawingExport.PlanFolder(new[] { good, gone, damaged }, folder, LibraryExportFormat.Dxf);
        var reported = new List<int>();

        var result = await LibraryDrawingExport.ExportAsync(library, targets, LibraryExportFormat.Dxf,
            new SynchronousProgress(reported.Add));

        Assert.Equal(new[] { targets[0].Path }, result.Written);
        Assert.Same(gone, Assert.Single(result.Missing));
        Assert.Same(damaged, Assert.Single(result.Failed).Record);
        Assert.Equal(new[] { 1, 2, 3 }, reported);
        Assert.Equal(new[] { "Good.dxf" }, Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ExportAsync_StopsWhenCanceled()
    {
        var library = new FakeLibrary();
        var targets = LibraryDrawingExport.PlanFolder(new[] { library.Add("A", Package(Units.Inches)) },
            folder, LibraryExportFormat.Dxf);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LibraryDrawingExport.ExportAsync(library, targets, LibraryExportFormat.Dxf, null, cancel.Token));
        Assert.Empty(Directory.GetFiles(folder));
    }

    private static byte[] Package(Units units) =>
        DrawingPackage.Write(new Drawing("square", PlateExportTests.Square(4)), units);

    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class FakeLibrary : IDrawingLibrary
    {
        private readonly Dictionary<(Guid, int), byte[]> packages = new();

        public DrawingLibraryRecord Add(string name, byte[] package)
        {
            var record = new DrawingLibraryRecord { Id = Guid.NewGuid(), Revision = 1, Name = name };
            packages[(record.Id, record.Revision)] = package;
            return record;
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            Task.FromResult(packages.TryGetValue((id, revision), out var package) ? package : null);
    }
}
