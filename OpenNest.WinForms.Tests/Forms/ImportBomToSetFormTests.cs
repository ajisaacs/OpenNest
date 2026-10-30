using System.Reflection;
using System.Windows.Forms;
using OpenNest.Converters;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;
using OpenNest.Tests.Data;
using DrawingSetBrowser = OpenNest.Controls.DrawingSetBrowser;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>Drawing Sets &gt; More &gt; Import BOM: a BOM becomes a prefilled set, and nothing is stored unasked.</summary>
public sealed class ImportBomToSetFormTests : IDisposable
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    private readonly string folder = Path.Combine(Path.GetTempPath(), "bom-to-set-" + Guid.NewGuid().ToString("N"));

    public ImportBomToSetFormTests()
    {
        Directory.CreateDirectory(folder);
        WriteSquare("PT01", 2);
        WriteSquare("PT02", 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void ImportBom_OpensTheEditorPrefilled_AndCancelingItStoresNoSet()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new InMemoryDrawingLibrary();
                // PT01 was imported from this file before; the import reuses it as it is.
                var existing = library.Seed("PT01", "Steel", 0.25, Package("PT01"), customer: "Synthetic Co");
                var sets = new InMemoryDrawingSets();
                var confirmations = new List<string>();
                var editorChecked = false;

                using var form = new DrawingSetsForm(sets, library, Server);
                var browser = form.Browser;
                browser.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                browser.ImportBom = () =>
                {
                    using var import = new ImportBomToSetForm(sets, library, Units.Inches, CutType.Laser, "synthetic operator");
                    import.ShowMessage = (text, _, _) =>
                    {
                        confirmations.Add(text);
                        return DialogResult.OK;
                    };
                    import.Show();
                    Field<TextBox>(import, "customerBox").Text = "Synthetic Co";
                    Field<TextBox>(import, "nameBox").Text = "Job 7";
                    Run(import.AnalyzeAsync(Items(), folder, "Job 7 BOM.xlsx"));

                    var grid = Field<DataGridView>(import, "drawingGrid");
                    Assert.Equal(
                        new[] { "In the library (revision 1)", "New library drawing", "Not imported: no drawing file found" },
                        grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["libraryColumn"].Value));
                    Assert.Contains("no material or thickness", Field<Label>(import, "summaryLabel").Text);
                    Assert.Empty(library.Writes);

                    Assert.True(Run(import.CreateSetAsync()));
                    return import.Edit;
                };
                browser.EditSet = edit =>
                {
                    using var editor = new EditDrawingSetForm(edit, library, Server);
                    editor.ShowMessage = (_, _, _) => DialogResult.Yes;
                    editor.Show();
                    Assert.Equal("Job 7", Field<TextBox>(editor, "nameBox").Text);
                    Assert.Equal("Synthetic Co", Field<TextBox>(editor, "customerBox").Text);
                    var members = Field<DataGridView>(editor, "memberGrid");
                    Assert.Equal(new object[] { "PT01", "PT02" },
                        members.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["drawingColumn"].Value));
                    Assert.Equal(new[] { (existing.Id, 7), (library.Writes.Single().Id, 3) },
                        edit.Members.Select(m => (m.DrawingId, m.Quantity)));
                    editorChecked = true;
                    editor.Close();
                    return null;
                };
                form.Show();

                Run(browser.ImportBomAsync());

                Assert.True(editorChecked);
                // One confirmation, naming the one new drawing before it was uploaded.
                Assert.Contains("- PT02 (item 2)", Assert.Single(confirmations));
                Assert.Equal("upload", library.Writes.Single().Kind);
                Assert.Empty(sets.Creates);
                Assert.Empty(sets.Current);
                Assert.Equal("The imported set was not saved.", Field<Label>(browser, "statusLabel").Text);
            },
            TestTimeout,
            "The Import BOM to set test did not complete.");
    }

    [Fact]
    public void DecliningTheLibraryWrites_OrCanceling_StoresNothing()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new InMemoryDrawingLibrary();
                var sets = new InMemoryDrawingSets();
                using var import = new ImportBomToSetForm(sets, library, Units.Inches, CutType.Laser, "synthetic operator");
                var asked = 0;
                import.ShowMessage = (_, buttons, _) =>
                {
                    asked++;
                    Assert.Equal(MessageBoxButtons.OKCancel, buttons);
                    return DialogResult.Cancel;
                };
                import.Show();
                Run(import.AnalyzeAsync(Items(), folder, "Job 7 BOM.xlsx"));
                Assert.True(import.Plan!.HasWrites);

                Assert.False(Run(import.CreateSetAsync()));
                Assert.Equal(1, asked);
                Assert.Null(import.Edit);
                Assert.Empty(library.Writes);
                Assert.Empty(library.Current);

                // A changed customer makes the plan stale until the BOM is read again.
                Field<TextBox>(import, "customerBox").Text = "Another customer";
                Assert.Null(import.Plan);
                Assert.False(Field<Button>(import, "createButton").Enabled);

                Field<Button>(import, "cancelButton").PerformClick();
                Assert.Equal(DialogResult.Cancel, import.DialogResult);
                Assert.Empty(library.Writes);
                Assert.Empty(sets.Creates);
            },
            TestTimeout,
            "The declined Import BOM test did not complete.");
    }

    [Fact]
    public void ImportBom_IsInTheMoreMenu_WithoutASelectedSet_AndNotInThePicker()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new InMemoryDrawingLibrary();
                var sets = new InMemoryDrawingSets();
                using var form = new DrawingSetsForm(sets, library, Server);
                form.Show();
                var browser = form.Browser;
                PumpUntil(() => sets.Queries.Count == 1, "the set list");
                PumpUntil(() => Field<Button>(browser, "moreButton").Enabled, "the More button");

                var item = Field<ToolStripMenuItem>(browser, "importBomMenuItem");
                Assert.Null(browser.SelectedSet);
                Assert.True(item.Enabled);
                Assert.Same(item, Field<ContextMenuStrip>(browser, "moreMenu").Items.Cast<ToolStripItem>().Last());

                using var picker = new ChooseDrawingSetForm(sets, library, Server);
                picker.Show();
                var pickBrowser = Field<DrawingSetBrowser>(picker, "browser");
                Assert.False(Field<ToolStripMenuItem>(pickBrowser, "importBomMenuItem").Enabled);
                Assert.Null(Field<DataGridView>(pickBrowser, "setGrid").ContextMenuStrip);
            },
            TestTimeout,
            "The Import BOM menu test did not complete.");
    }

    private static List<BomItem> Items() => new()
    {
        new BomItem { ItemNum = 1, FileName = "PT01", Qty = 3, Material = "Steel", Thickness = 0.25 },
        new BomItem { ItemNum = 2, FileName = "PT02", Qty = 3 },
        new BomItem { ItemNum = 3, FileName = "MISSING", Qty = 1, Material = "Steel", Thickness = 0.25 },
        new BomItem { ItemNum = 4, FileName = "PT01.dxf", Qty = 4, Material = "steel", Thickness = 0.25 },
    };

    private byte[] Package(string name)
    {
        var rows = BomImportRows.Build(new List<BomItem> { new() { FileName = name, Qty = 1 } }, folder);
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

    private static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the operation to finish");

    private static T Run<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted, "the operation to finish");
        return task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(condition(), $"Timed out waiting for {what}.");
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
