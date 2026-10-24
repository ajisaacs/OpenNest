using OpenNest.Converters;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;
using OpenNest.Tests.Data;

namespace OpenNest.Tests.Bom;

/// <summary>
/// BOM rows to library drawings to a prefilled set: the drawings a BOM puts in a set,
/// and how each one is matched to the library (reuse, new revision, new drawing).
/// </summary>
public sealed class BomSetImportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "bom-set-" + Guid.NewGuid().ToString("N"));

    public BomSetImportTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Build_SumsRowsOfOneDrawing_KeepsRowsWithoutDetails_AndSkipsTheRestWithAReason()
    {
        WriteSquare("PT01", 2);
        WriteSquare("PT02", 3);
        WriteSquare("PT03", 4);
        File.WriteAllText(Path.Combine(folder, "BAD.dxf"), "not a drawing");
        var rows = BomImportRows.Build(new List<BomItem>
        {
            Item(1, "PT01", 2, "Steel", 0.25),
            Item(2, "PT02", 3, null, null),
            Item(3, "pt01.dxf", 5, " steel ", 0.25),
            Item(4, "PT01", 1, "Steel", 0.5),
            Item(5, "MISSING", 1, "Steel", 0.25),
            Item(6, null, 1, "Steel", 0.25),
            Item(7, "PT03", 0, "Steel", 0.25),
            Item(8, "BAD", 1, "Steel", 0.25),
        }, folder);

        var result = BomSetDrawings.Build(rows, Units.Inches);

        Assert.Equal(new[] { ("PT01", "Steel", 0.25, 7L), ("PT02", "", 0.0, 3L), ("PT01", "Steel", 0.5, 1L) },
            result.Drawings.Select(d => (d.Name, d.Material, d.Thickness, d.Quantity)));
        Assert.Equal("PT01 (items 1, 3)", result.Drawings[0].Label);
        Assert.True(result.Drawings[1].NeedsDetails);
        Assert.False(result.Drawings[0].NeedsDetails);
        Assert.Equal(new[] { ("MISSING", "no drawing file found"), ("Item 6", "no file name"), ("PT03", "quantity is below 1") },
            result.Skipped.Take(3).Select(s => (s.Name, s.Reason)));
        var bad = result.Skipped[3];
        Assert.Equal("BAD", bad.Name);
        Assert.StartsWith("could not import the drawing", bad.Reason);
        // One file with two thicknesses is imported once.
        Assert.Same(result.Drawings[0].Drawing, result.Drawings[2].Drawing);
    }

    [Fact]
    public void GeometryKey_IgnoresTheFolder_ButNotGeometryOrUnits()
    {
        WriteSquare("PT01", 2);
        var rows = BomImportRows.Build(new List<BomItem> { Item(1, "PT01", 1, "Steel", 0.25) }, folder);
        var first = BomSetDrawings.Build(rows, Units.Inches).Drawings.Single().Package;
        var second = BomSetDrawings.Build(rows, Units.Inches).Drawings.Single().Package;
        var moved = Path.Combine(folder, "moved");
        Directory.CreateDirectory(moved);
        File.Copy(Path.Combine(folder, "PT01.dxf"), Path.Combine(moved, "PT01.dxf"));
        var elsewhere = BomSetDrawings.Build(BomImportRows.Build(
            new List<BomItem> { Item(1, "PT01", 1, "Steel", 0.25) }, moved), Units.Inches).Drawings.Single().Package;

        // An unchanged file imports with the same entity ids, so the same folder gives the
        // same package; another folder records another source path, so only the key matches.
        Assert.Equal(first, second);
        Assert.NotEqual(first, elsewhere);
        Assert.Equal(DrawingPackage.GeometryKey(first), DrawingPackage.GeometryKey(elsewhere));
        Assert.NotEqual(DrawingPackage.GeometryKey(first), DrawingPackage.GeometryKey(Square(3)));
        Assert.NotEqual(DrawingPackage.GeometryKey(first),
            DrawingPackage.GeometryKey(BomSetDrawings.Build(rows, Units.Millimeters).Drawings.Single().Package));
    }

    [Fact]
    public async Task Plan_ReusesALibraryDrawingImportedFromTheSameFileBefore()
    {
        var earlier = Drawings(("PT01", 2)).Single();
        var library = new InMemoryDrawingLibrary();
        var stored = library.Seed("PT01", "Steel", 0.25, earlier.Package, customer: "Synthetic Co");

        var plan = await DrawingSetImport.PlanAsync(library, Items(Drawings(("PT01", 2))), DrawingPackage.GeometryKey);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(DrawingSetImportAction.Reuse, entry.Action);
        Assert.Same(stored, entry.Existing);
        Assert.Equal(new[] { (stored.Id, 1) }, library.Downloads);
        Assert.False(plan.HasWrites);
    }

    [Fact]
    public async Task Plan_ReusesSameGeometry_RevisesChangedGeometry_AndCreatesTheRest_WithoutWriting()
    {
        var drawings = Drawings(("SAME", 2), ("CHANGED", 3), ("NEW", 4), ("OTHER-CUSTOMER", 5), ("ARCHIVED", 6));
        var library = new InMemoryDrawingLibrary();
        var same = library.Seed("SAME", "Steel", 0.25, drawings[0].Package, customer: "Synthetic Co");
        var changed = library.Seed("changed", "STEEL", 0.25, Square(9), customer: "synthetic co");
        library.Seed("OTHER-CUSTOMER", "Steel", 0.25, drawings[3].Package, customer: "Someone Else");
        library.Seed("ARCHIVED", "Steel", 0.25, drawings[4].Package, customer: "Synthetic Co", archived: true);

        var plan = await DrawingSetImport.PlanAsync(library, Items(drawings), DrawingPackage.GeometryKey);

        Assert.Equal(new[]
        {
            DrawingSetImportAction.Reuse, DrawingSetImportAction.Revise, DrawingSetImportAction.Create,
            DrawingSetImportAction.Create, DrawingSetImportAction.Create,
        }, plan.Entries.Select(e => e.Action));
        Assert.Same(same, plan.Entries[0].Existing);
        Assert.Same(changed, plan.Entries[1].Existing);
        Assert.Equal("In the library (revision 1)", plan.Entries[0].Describe());
        Assert.Equal("New revision 2 (geometry changed)", plan.Entries[1].Describe());
        Assert.Equal("New library drawing", plan.Entries[2].Describe());
        Assert.True(plan.HasWrites);
        Assert.Equal((3, 1), (plan.Count(DrawingSetImportAction.Create), plan.Count(DrawingSetImportAction.Revise)));
        Assert.Contains("- CHANGED (item 2): revision 1 to 2", plan.DescribeWrites());
        Assert.Empty(library.Writes);
    }

    [Fact]
    public async Task Plan_SkipsAmbiguousMatches_TwinsAndQuantitiesOutOfRange()
    {
        var drawings = Drawings(("AMBIGUOUS", 2), ("TWIN", 3));
        var library = new InMemoryDrawingLibrary();
        library.Seed("AMBIGUOUS", "Steel", 0.25, Square(7), customer: "Synthetic Co");
        library.Seed("AMBIGUOUS", "Steel", 0.25, Square(8), customer: "Synthetic Co");
        var items = Items(drawings);
        items.Add(new DrawingSetImportItem("twin again", Metadata(drawings[1]), drawings[1].Package, 1));
        items.Add(new DrawingSetImportItem("too many", Metadata(drawings[0], name: "BIG"), drawings[0].Package,
            DrawingSetRules.MaxQuantity + 1L));

        var plan = await DrawingSetImport.PlanAsync(library, items, DrawingPackage.GeometryKey);

        Assert.Equal(new[]
        {
            DrawingSetImportAction.Skip, DrawingSetImportAction.Create, DrawingSetImportAction.Skip,
            DrawingSetImportAction.Skip,
        }, plan.Entries.Select(e => e.Action));
        Assert.Contains("2 library drawings have these details", plan.Entries[0].Reason);
        Assert.Contains("same name and details as TWIN (item 2)", plan.Entries[2].Reason);
        Assert.Contains("quantity", plan.Entries[3].Reason);
    }

    [Fact]
    public async Task Apply_WritesOnlyNewDrawingsAndRevisions_AndPrefillsTheSetWithBomQuantities()
    {
        var drawings = Drawings(("SAME", 2), ("CHANGED", 3), ("NEW", 4));
        var library = new InMemoryDrawingLibrary();
        var same = library.Seed("SAME", "Steel", 0.25, drawings[0].Package, customer: "Synthetic Co");
        var changed = library.Seed("CHANGED", "Steel", 0.25, Square(9), customer: "Synthetic Co");
        var sets = new InMemoryDrawingSets();
        var edit = new DrawingSetEditSession(sets, null, "synthetic");
        var plan = await DrawingSetImport.PlanAsync(library, Items(drawings, quantities: new[] { 2L, 6L, 1L }),
            DrawingPackage.GeometryKey);

        var written = await plan.ApplyAsync(library, edit);

        Assert.Equal(new[] { ("update", changed.Id), ("upload", plan.Entries[2].NewId) }, library.Writes);
        Assert.Equal(new[] { changed.Id, plan.Entries[2].NewId }, written.Select(r => r.Id));
        var revised = library.Get(changed.Id);
        Assert.Equal((2, "Seeded notes", "synthetic operator"), (revised.Revision, revised.Notes, revised.SavedBy));
        Assert.Equal(InMemoryDrawingLibrary.Sha(drawings[1].Package), revised.PackageSha256);
        var created = library.Get(plan.Entries[2].NewId);
        Assert.Equal(("NEW", "Synthetic Co", "Steel", 0.25), (created.Name, created.Customer, created.MaterialName, created.Thickness));
        Assert.Equal(new[] { (same.Id, 2), (changed.Id, 6), (created.Id, 1) },
            edit.Members.Select(m => (m.DrawingId, m.Quantity)));
        Assert.Empty(sets.Creates);
    }

    [Fact]
    public async Task Apply_StopsAtAFailedWrite_LeavesTheSetUntouched_AndImportingAgainReusesWhatWasStored()
    {
        var drawings = Drawings(("FIRST", 2), ("SECOND", 3));
        var library = new InMemoryDrawingLibrary { FailWrite = 1 };
        var edit = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "synthetic");
        var plan = await DrawingSetImport.PlanAsync(library, Items(drawings), DrawingPackage.GeometryKey);

        var failure = await Assert.ThrowsAsync<DrawingSetImportException>(() => plan.ApplyAsync(library, edit));

        Assert.Equal("FIRST", Assert.Single(failure.Written).Name);
        Assert.Contains("Already stored: FIRST", failure.Message);
        Assert.Empty(edit.Members);
        Assert.Single(library.Current);

        library.FailWrite = null;
        var again = await DrawingSetImport.PlanAsync(library, Items(drawings), DrawingPackage.GeometryKey);
        Assert.Equal(new[] { DrawingSetImportAction.Reuse, DrawingSetImportAction.Create }, again.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task Plan_DrawingsWithoutMaterialOrThickness_StillImport()
    {
        WriteSquare("BARE", 2);
        var rows = BomImportRows.Build(new List<BomItem> { Item(1, "BARE", 4, null, null) }, folder);
        var library = new InMemoryDrawingLibrary();
        var edit = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "synthetic");

        var plan = await DrawingSetImport.PlanAsync(library, Items(BomSetDrawings.Build(rows, Units.Inches).Drawings),
            DrawingPackage.GeometryKey);
        await plan.ApplyAsync(library, edit);

        var member = Assert.Single(edit.Members);
        Assert.Equal(("", 0.0, 4), (member.Drawing!.MaterialName, member.Drawing.Thickness, member.Quantity));
    }

    private IReadOnlyList<BomSetDrawing> Drawings(params (string Name, double Size)[] files)
    {
        var items = new List<BomItem>();
        foreach (var (name, size) in files)
        {
            WriteSquare(name, size);
            items.Add(Item(items.Count + 1, name, 1, "Steel", 0.25));
        }

        return BomSetDrawings.Build(BomImportRows.Build(items, folder), Units.Inches).Drawings;
    }

    // What the desktop's Import BOM dialog sends for each drawing.
    private static List<DrawingSetImportItem> Items(IEnumerable<BomSetDrawing> drawings, long[]? quantities = null) =>
        drawings.Select((d, i) => new DrawingSetImportItem(d.Label, Metadata(d), d.Package, quantities?[i] ?? d.Quantity))
            .ToList();

    private static DrawingLibraryRecord Metadata(BomSetDrawing drawing, string? name = null) => new()
    {
        Name = name ?? drawing.Name,
        Customer = "Synthetic Co",
        MaterialName = drawing.Material,
        Thickness = drawing.Thickness,
        Units = Units.Inches,
        CutType = CutType.Laser,
        Settings = DrawingNestingSettings.FromDrawing(drawing.Drawing),
        SavedBy = "synthetic operator",
    };

    private static BomItem Item(int itemNum, string? fileName, int? qty, string? material, double? thickness) => new()
    {
        ItemNum = itemNum,
        FileName = fileName,
        Qty = qty,
        Material = material,
        Thickness = thickness,
    };

    private byte[] Square(double size)
    {
        var name = "SQUARE-" + size;
        WriteSquare(name, size);
        var rows = BomImportRows.Build(new List<BomItem> { Item(1, name, 1, "Steel", 0.25) }, folder);
        return BomSetDrawings.Build(rows, Units.Inches).Drawings.Single().Package;
    }

    private void WriteSquare(string name, double size)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(size, 0)));
        shape.Entities.Add(new Line(new Vector(size, 0), new Vector(size, size)));
        shape.Entities.Add(new Line(new Vector(size, size), new Vector(0, size)));
        shape.Entities.Add(new Line(new Vector(0, size), new Vector(0, 0)));
        Dxf.ExportProgram(ConvertGeometry.ToProgram(shape), Path.Combine(folder, name + ".dxf"));
    }
}
