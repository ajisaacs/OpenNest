using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

public class DrawingLibraryFormTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void ListsTheNestFilteredPage_AndAddThenAddAgainRaisesTheQuantity()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-101", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "Deburr both sides");
                var nest = MatchingNest();
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                // Only the adds' downloads are counted here; the preview has its own test.
                form.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var notes = Field<TextBox>(form, "notesBox");
                var quantity = Field<NumericUpDown>(form, "quantityBox");

                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                var query = Assert.Single(library.Queries);
                Assert.Equal(("Synthetic Steel", "Grade A", (double?)0.25), (query.MaterialName, query.MaterialGrade, query.Thickness));
                Assert.Equal("SYN-101", grid.Rows[0].Cells["nameColumn"].Value);
                Assert.Equal("Laser", grid.Rows[0].Cells["cutTypeColumn"].Value);
                Assert.Equal("Deburr both sides", notes.Text);

                quantity.Value = 3;
                Run(form.AddSelectedAsync());
                quantity.Value = 2;
                Run(form.AddSelectedAsync());

                Assert.Empty(messages);
                var drawing = Assert.Single(nest.Drawings);
                Assert.Equal(5, drawing.Quantity.Required);
                Assert.Equal((Server, record.Id, record.Revision), (drawing.Library!.ServerKey, drawing.Library.Id, drawing.Library.Revision));
                Assert.Same(drawing, Assert.Single(form.AddedDrawings));
                Assert.Equal(new[] { (record.Id, record.Revision), (record.Id, record.Revision) }, library.Downloads);
            },
            TestTimeout,
            "The drawing library dialog test did not complete.");
    }

    [Fact]
    public void SelectingADrawing_ShowsItsDetailsAndPreviewsItsGeometry()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-505", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var preview = Field<OpenNest.Controls.ShapePreviewControl>(form, "previewBox");

                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                Assert.Equal("SYN-505", Field<Label>(form, "detailNameLabel").Text);
                Assert.Equal("Synthetic Steel Grade A", Field<Label>(form, "materialValue").Text);
                Assert.Equal("0.25 in", Field<Label>(form, "thicknessValue").Text);

                Run(form.LoadSelectedPreviewAsync());
                PumpUntil(() => preview.Plate.Parts.Count > 0, "the preview");
                Assert.NotNull(Assert.Single(preview.Plate.Parts).BaseDrawing.Program);

                // A cached revision is shown again without another download.
                Run(form.LoadSelectedPreviewAsync());
                Assert.Single(preview.Plate.Parts);
                Assert.Single(library.Downloads, d => d == (record.Id, record.Revision));
                Assert.Empty(nest.Drawings);
            },
            TestTimeout,
            "The drawing library preview test did not complete.");
    }

    [Fact]
    public void InThisNestTab_ListsTheNestsDrawings_AndEditsTheirRequiredQuantity()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-606", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "");
                var nest = MatchingNest();
                var local = new RectangleShape { Name = "LOCAL-1", Length = 3, Width = 1 }.GetDrawing();
                local.Quantity.Required = 4;
                nest.Drawings.Add(local);
                var cutoff = new RectangleShape { Name = "CUTOFF", Length = 1, Width = 1 }.GetDrawing();
                cutoff.IsCutOff = true;
                nest.Drawings.Add(cutoff);

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var nestGrid = Field<DataGridView>(form, "nestGrid");
                var nestTab = Field<TabPage>(form, "nestTab");

                Assert.Equal("In This Nest (1)", nestTab.Text);
                Assert.Equal("LOCAL-1", Assert.Single(nestGrid.Rows.Cast<DataGridViewRow>()).Cells["nestNameColumn"].Value);
                Assert.Equal("Not from the library", nestGrid.Rows[0].Cells["nestLibraryColumn"].Value);

                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                Field<NumericUpDown>(form, "quantityBox").Value = 2;
                Run(form.AddSelectedAsync());
                Assert.Equal("In This Nest (2)", nestTab.Text);
                Assert.Equal($"Revision {record.Revision}", nestGrid.Rows[1].Cells["nestLibraryColumn"].Value);
                Assert.Equal(2, nestGrid.Rows[1].Cells["nestRequiredColumn"].Value);

                Field<TabControl>(form, "tabControl").SelectedTab = nestTab;
                var required = nestGrid.Rows[0].Cells["nestRequiredColumn"];

                nestGrid.CurrentCell = required;
                Assert.True(nestGrid.BeginEdit(true));
                nestGrid.EditingControl!.Text = "-1";
                nestGrid.NotifyCurrentCellDirty(true);
                // Leaving the cell validates it and keeps the editor open on a bad number.
                Assert.Throws<InvalidOperationException>(() => nestGrid.CurrentCell = nestGrid.Rows[0].Cells["nestNameColumn"]);
                Assert.NotEmpty(nestGrid.Rows[0].ErrorText);
                // A commit that skips validation is refused when it is applied.
                nestGrid.EndEdit();
                Assert.Equal(4, local.Quantity.Required);
                Assert.Equal(4, required.Value);
                Assert.False(form.QuantitiesChanged);

                Assert.True(nestGrid.BeginEdit(true));
                nestGrid.EditingControl!.Text = "9";
                nestGrid.NotifyCurrentCellDirty(true);
                Assert.True(nestGrid.EndEdit());
                Assert.Equal(9, local.Quantity.Required);
                Assert.Equal(9, nestGrid.Rows[0].Cells["nestRemainingColumn"].Value);
                Assert.True(form.QuantitiesChanged);
                Assert.Contains("11 parts required", Field<Label>(form, "nestSummaryLabel").Text);
            },
            TestTimeout,
            "The In This Nest tab test did not complete.");
    }

    [Fact]
    public void MismatchWarning_IsShownBeforeTheAdd_AndCancelLeavesTheNestAlone()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                library.Add("SYN-202", "Other Steel", "Grade B", 0.1875, Units.Inches, "");
                var nest = MatchingNest();
                var answers = new Queue<DialogResult>(new[] { DialogResult.Cancel, DialogResult.OK });
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, buttons, _) =>
                {
                    messages.Add(text);
                    Assert.Equal(MessageBoxButtons.OKCancel, buttons);
                    return answers.Dequeue();
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                var showAll = Field<CheckBox>(form, "showAllCheckBox");

                showAll.Checked = true;
                PumpUntil(() => grid.Rows.Count == 1, "the unfiltered page");
                Assert.Equal(("", "", (double?)null), (library.Queries[^1].MaterialName, library.Queries[^1].MaterialGrade, library.Queries[^1].Thickness));

                Run(form.AddSelectedAsync());
                Assert.Empty(nest.Drawings);
                Run(form.AddSelectedAsync());

                Assert.Equal(2, messages.Count);
                Assert.Contains("thickness 0.1875 (nest 0.25)", messages[0]);
                Assert.Contains("cut pins are dropped", messages[0]);
                Assert.Single(nest.Drawings);
            },
            TestTimeout,
            "The drawing library mismatch test did not complete.");
    }

    [Fact]
    public void Refusal_IsShownAndNothingIsAdded()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                library.Add("SYN-303", "Synthetic Steel", "Grade A", 0.25, Units.Millimeters, "");
                var nest = MatchingNest();
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, buttons, _) => { messages.Add(text); return DialogResult.OK; };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");

                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                Run(form.AddSelectedAsync());

                Assert.Contains("millimeters", Assert.Single(messages));
                Assert.Empty(nest.Drawings);
                Assert.Empty(form.AddedDrawings);
            },
            TestTimeout,
            "The drawing library refusal test did not complete.");
    }

    [Fact]
    public void EditSettings_SavesCutFirstAsANewRevision_AndTheAddAppliesIt()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-404", "Synthetic Steel", "Grade A", 0.25, Units.Inches, "Test square");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.EditDrawing = edit =>
                {
                    using var editor = new EditLibraryDrawingForm(edit);
                    editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                    editor.Show();
                    var save = Field<Button>(editor, "saveButton");
                    Assert.False(save.Enabled);
                    Field<CheckBox>(editor, "cutFirstCheckBox").Checked = true;
                    Assert.True(save.Enabled);
                    Run(editor.SaveAsync());
                    return editor.Saved;
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                Assert.Equal("", grid.Rows[0].Cells["cutFirstColumn"].Value);

                Run(form.EditSelectedAsync());
                PumpUntil(() => Equals(grid.Rows[0].Cells["revisionColumn"].Value, record.Revision + 1), "the reloaded page");

                var update = Assert.Single(library.MetadataUpdates);
                Assert.Equal((record.Id, record.Revision), (update.Id, update.ExpectedRevision));
                Assert.True(update.Record.Settings.CutFirst);
                Assert.Equal("Test square", update.Record.Notes);
                Assert.Equal("Yes", grid.Rows[0].Cells["cutFirstColumn"].Value);

                Run(form.AddSelectedAsync());
                Assert.True(Assert.Single(nest.Drawings).CutFirst);
            },
            TestTimeout,
            "The drawing library edit test did not complete.");
    }

    [Fact]
    public void EditDrawing_SavesNewDetailsAsANewRevision_KeepingUnits()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var record = library.Add("SYN-405", "", "", 0, Units.Inches, "Test square");
                var nest = MatchingNest();

                using var form = new DrawingLibraryForm(library, Server, nest);
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                form.EditDrawing = edit =>
                {
                    using var editor = new EditLibraryDrawingForm(edit);
                    editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                    editor.Show();
                    var save = Field<Button>(editor, "saveButton");
                    Assert.Equal("SYN-405", Field<TextBox>(editor, "nameBox").Text);
                    Assert.Equal("in", Field<Label>(editor, "unitsLabel").Text);
                    Assert.False(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = "SYN-405B";
                    Field<TextBox>(editor, "materialBox").Text = "Synthetic Stainless";
                    Field<TextBox>(editor, "gradeBox").Text = "304";
                    Field<NumericUpDown>(editor, "thicknessBox").Value = 0.1875m;
                    Field<ComboBox>(editor, "cutTypeBox").SelectedItem = "Waterjet";
                    Assert.True(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = " ";
                    Assert.False(save.Enabled);
                    Field<TextBox>(editor, "nameBox").Text = "SYN-405B";
                    Run(editor.SaveAsync());
                    return editor.Saved;
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntil(() => grid.Rows.Count == 1, "the library page");

                Run(form.EditSelectedAsync());
                PumpUntil(() => Equals(grid.Rows[0].Cells["nameColumn"].Value, "SYN-405B"), "the reloaded page");

                var update = Assert.Single(library.MetadataUpdates);
                Assert.Equal((record.Id, record.Revision), (update.Id, update.ExpectedRevision));
                Assert.Equal(("SYN-405B", "Synthetic Stainless", "304", 0.1875, CutType.Waterjet, Units.Inches),
                    (update.Record.Name, update.Record.MaterialName, update.Record.MaterialGrade, update.Record.Thickness,
                        update.Record.CutType, update.Record.Units));
                Assert.Equal("Waterjet", grid.Rows[0].Cells["cutTypeColumn"].Value);
            },
            TestTimeout,
            "The drawing library details edit test did not complete.");
    }

    private static Nest MatchingNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Material = new Material("Synthetic Steel", "Grade A"),
    };

    private static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the add to finish");

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

    /// <summary>Serves every record (ignoring filters) and real packages by revision.</summary>
    private sealed class FakeLibrary : IDrawingLibraryStore
    {
        private readonly List<DrawingLibraryRecord> _records = new();
        private readonly Dictionary<(Guid, int), byte[]> _packages = new();

        public List<DrawingLibraryQuery> Queries { get; } = new();

        public List<(Guid, int)> Downloads { get; } = new();

        public DrawingLibraryRecord Add(string name, string material, string grade, double thickness, Units units, string notes)
        {
            var drawing = new RectangleShape { Name = name, Length = 4, Width = 2 }.GetDrawing();
            var package = DrawingPackage.Write(drawing, units);
            var record = new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Revision = 2,
                Name = name,
                Customer = "Synthetic customer",
                MaterialName = material,
                MaterialGrade = grade,
                Thickness = thickness,
                Units = units,
                CutType = CutType.Laser,
                Notes = notes,
                PackageSha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(),
                SavedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            };
            _records.Add(record);
            _packages[(record.Id, record.Revision)] = package;
            return record;
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = _records.ToArray(),
                Total = _records.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default)
        {
            Downloads.Add((id, revision));
            return Task.FromResult(_packages.TryGetValue((id, revision), out var package) ? package : null);
        }

        public List<(Guid Id, int ExpectedRevision, DrawingLibraryRecord Record)> MetadataUpdates { get; } = new();

        /// <summary>Replaces the listed record with the next revision, keeping its package.</summary>
        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            MetadataUpdates.Add((id, expectedRevision, record));
            var index = _records.FindIndex(r => r.Id == id);
            var current = _records[index];
            if (current.Revision != expectedRevision)
                throw new DrawingLibraryConflictException(current);
            var saved = new DrawingLibraryRecord
            {
                Id = id,
                Revision = current.Revision + 1,
                Name = record.Name,
                Customer = record.Customer,
                MaterialName = record.MaterialName,
                MaterialGrade = record.MaterialGrade,
                Thickness = record.Thickness,
                Units = record.Units,
                CutType = record.CutType,
                Notes = record.Notes,
                Settings = record.Settings,
                PackageSha256 = current.PackageSha256,
                SavedAt = current.SavedAt.AddMinutes(1),
            };
            _records[index] = saved;
            _packages[(id, saved.Revision)] = _packages[(id, current.Revision)];
            return Task.FromResult(saved);
        }

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
