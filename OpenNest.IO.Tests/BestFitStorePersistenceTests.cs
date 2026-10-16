using System.IO.Compression;
using System.Text;
using OpenNest.BestFit;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

public class BestFitStorePersistenceTests
{
    private static readonly SavedPairResult Result = new(
        0, System.Math.PI, 10.25, -0.5, 1, 4, 0.25, 120, 20, 6, 0.5, true, null!, 100, null!);

    [Fact]
    public void Write_WithoutStore_WritesNoBestFitEntries()
    {
        var entries = EntryNames(Save(CreateNest(), store: null));

        Assert.DoesNotContain(entries, IsBestFitEntry);
    }

    [Fact]
    public void Write_WithStore_PassesUsedPlateKeysAndWritesExportedSets()
    {
        var store = new RecordingStore { ToExport = [new SavedPairSet(60, 40, 0.25, [Result])] };

        var bytes = Save(CreateNest(), store);

        var exported = Assert.Single(store.Exported);
        Assert.Equal("Part", exported.Drawing.Name);
        Assert.Equal(
            new[] { (60.0, 40.0, 0.25), (30.0, 20.0, 0.5) }.OrderBy(k => k),
            exported.PlateKeys.OrderBy(k => k));
        Assert.Contains("bestfits/bestfit-1", EntryNames(bytes));
    }

    [Fact]
    public void Write_StoreExportsNothing_WritesNoBestFitEntry()
    {
        var entries = EntryNames(Save(CreateNest(), new RecordingStore()));

        Assert.DoesNotContain(entries, IsBestFitEntry);
    }

    [Fact]
    public void Read_WithStore_ImportsSavedSetsWithWireDefaults()
    {
        var bytes = Save(CreateNest(), new RecordingStore { ToExport = [new SavedPairSet(60, 40, 0.25, [Result])] });
        var store = new RecordingStore();

        var nest = new NestReader(new MemoryStream(bytes)) { BestFitStore = store }.Read();

        var imported = Assert.Single(store.Imported);
        Assert.Same(nest.Drawings.Single(), imported.Drawing);
        var set = Assert.Single(imported.Sets);
        Assert.Equal((60.0, 40.0, 0.25), (set.PlateWidth, set.PlateHeight, set.Spacing));
        var result = Assert.Single(set.Results);
        // A null reason and hull list are written as "" and [].
        Assert.Equal(Result with { Reason = "", HullAngles = result.HullAngles }, result);
        Assert.Empty(result.HullAngles);
    }

    [Fact]
    public void Read_WithoutStore_SkipsBestFitEntriesUnparsed()
    {
        var bytes = AddEntry(Save(CreateNest(), store: null), "bestfits/bestfit-1", "not json");

        var nest = new NestReader(new MemoryStream(bytes)).Read();

        Assert.Single(nest.Drawings);
    }

    private static bool IsBestFitEntry(string name) => name.StartsWith("bestfits/", StringComparison.Ordinal);

    private static Nest CreateNest()
    {
        var program = new CNC.Program();
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(10, 5));
        program.Codes.Add(new LinearMove(0, 5));
        program.Codes.Add(new LinearMove(0, 0));
        var drawing = new Drawing("Part", program);
        var nest = new Nest("best fits");
        nest.Drawings.Add(drawing);
        foreach (var (width, length, spacing) in new[] { (60.0, 40.0, 0.25), (30.0, 20.0, 0.5) })
        {
            var plate = new Plate(width, length) { PartSpacing = spacing };
            plate.Parts.Add(new Part(drawing, new Vector(1, 1)));
            nest.Plates.Add(plate);
        }
        return nest;
    }

    private static byte[] Save(Nest nest, IBestFitStore? store)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest) { BestFitStore = store }.Write(stream));
        return stream.ToArray();
    }

    private static string[] EntryNames(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToArray();
    }

    private static byte[] AddEntry(byte[] bytes, string name, string text)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(text);
        }
        return stream.ToArray();
    }

    private sealed class RecordingStore : IBestFitStore
    {
        public IReadOnlyList<SavedPairSet> ToExport { get; init; } = [];

        public List<(Drawing Drawing, IReadOnlySet<(double Width, double Length, double Spacing)> PlateKeys)> Exported { get; } = [];

        public List<(Drawing Drawing, IReadOnlyList<SavedPairSet> Sets)> Imported { get; } = [];

        public IReadOnlyList<SavedPairSet> Export(
            Drawing drawing, IReadOnlySet<(double Width, double Length, double Spacing)> plateKeys)
        {
            Exported.Add((drawing, plateKeys));
            return ToExport;
        }

        public void Import(Drawing drawing, IReadOnlyList<SavedPairSet> sets) => Imported.Add((drawing, sets));
    }
}
