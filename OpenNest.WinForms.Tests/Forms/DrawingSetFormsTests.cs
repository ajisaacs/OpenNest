using System.Reflection;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Tests.Data;
using DrawingSetBrowser = OpenNest.Controls.DrawingSetBrowser;

namespace OpenNest.WinForms.Tests.Forms;

public class DrawingSetFormsTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void SetsTab_LoadsWhenShown_AndNewSetIsSavedThroughTheEditor()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var bracket = library.Add("SYN-BRACKET", "Synthetic Steel", 0.25);
                var gusset = library.Add("SYN-GUSSET", "Synthetic Aluminum", 0.125);
                var sets = new InMemoryDrawingSets();

                using var form = new DrawingLibraryForm(library, Server, MatchingNest(), sets);
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                var browser = form.SetBrowser;
                browser.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                browser.EditSet = edit =>
                {
                    using var editor = new EditDrawingSetForm(edit, library, Server);
                    editor.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                    editor.PickDrawings = () => (new[] { bracket, gusset }, 3);
                    editor.Show();
                    var save = Field<Button>(editor, "saveButton");
                    Assert.False(save.Enabled);

                    Field<TextBox>(editor, "nameBox").Text = "Synthetic kit";
                    Field<TextBox>(editor, "customerBox").Text = "Synthetic customer";
                    editor.AddDrawings();
                    Assert.True(save.Enabled);
                    Run(editor.SaveAsync());
                    return editor.Saved;
                };
                form.Show();
                var tabs = Field<TabControl>(form, "tabControl");
                var grid = Field<DataGridView>(browser, "setGrid");

                Assert.Equal(new[] { "Drawings", "Sets" }, tabs.TabPages.Cast<TabPage>().Select(t => t.Text));
                Assert.Empty(sets.Queries);
                tabs.SelectedIndex = 1;
                PumpUntil(() => sets.Queries.Count == 1 && !Field<Label>(browser, "summaryLabel").Text.StartsWith("Loading"), "the set list");
                Assert.Contains("New Set", Field<Label>(browser, "summaryLabel").Text);

                Run(browser.NewSetAsync());
                PumpUntil(() => grid.Rows.Count == 1, "the saved set");

                var stored = Assert.Single(sets.Current);
                Assert.Equal(("Synthetic kit", "Synthetic customer", 1), (stored.Name, stored.Customer, stored.Revision));
                Assert.Equal(new[] { (bracket.Id, 3), (gusset.Id, 3) }, stored.Members.Select(m => (m.DrawingId, m.Quantity)));
                Assert.Equal("Synthetic kit", grid.Rows[0].Cells["nameColumn"].Value);
                Assert.Equal(2, grid.Rows[0].Cells["membersColumn"].Value);
            },
            TestTimeout,
            "The new set test did not complete.");
    }

    [Fact]
    public void PickMode_HasNoSetsTab_AndReturnsEverySelectedDrawingInListOrder()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel", 0.25);
                library.Add("SYN-B", "Synthetic Steel", 0.25);
                var c = library.Add("SYN-C", "Synthetic Steel", 0.25);

                using var form = new DrawingLibraryForm(library, Server);
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntil(() => grid.Rows.Count == 3, "the unfiltered library page");

                Assert.True(form.IsPickMode);
                Assert.Single(Field<TabControl>(form, "tabControl").TabPages);
                Assert.Equal(("", "", (double?)null), (library.Queries[0].MaterialName, library.Queries[0].MaterialGrade, library.Queries[0].Thickness));
                grid.ClearSelection();
                grid.Rows[2].Selected = true;
                grid.Rows[0].Selected = true;
                Field<NumericUpDown>(form, "quantityBox").Value = 4;
                Run(form.AddSelectedAsync());

                Assert.Equal(DialogResult.OK, form.DialogResult);
                Assert.Equal(new[] { a, c }, form.PickedDrawings);
                Assert.Equal(4, form.PickedQuantity);
            },
            TestTimeout,
            "The pick mode test did not complete.");
    }

    [Fact]
    public void AddToSet_AppendsTheDrawingToTheChosenSet_AndRefusesADuplicate()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var existing = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var added = library.Add("SYN-B", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                var set = Run(sets.CreateAsync(new DrawingSetRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Synthetic kit",
                    Members = new[] { new DrawingSetMember { DrawingId = existing.Id, Quantity = 1, Drawing = existing } },
                }));
                var messages = new List<string>();
                var edited = new List<DrawingSetEditSession>();

                using var form = new DrawingLibraryForm(library, Server, MatchingNest(), sets);
                form.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                form.ChooseSet = () => (true, set);
                form.EditSet = edit =>
                {
                    edited.Add(edit);
                    return Run(edit.SaveAsync());
                };
                form.Show();
                var grid = Field<DataGridView>(form, "drawingGrid");
                PumpUntil(() => grid.Rows.Count == 2, "the library page");

                grid.CurrentCell = grid.Rows[1].Cells[0];
                Field<NumericUpDown>(form, "quantityBox").Value = 5;
                Run(form.AddSelectedToSetAsync());
                grid.CurrentCell = grid.Rows[0].Cells[0];
                Run(form.AddSelectedToSetAsync());

                var edit = Assert.Single(edited);
                Assert.Equal(set.Id, edit.Id);
                var stored = Assert.Single(sets.Current);
                Assert.Equal(new[] { (existing.Id, 1), (added.Id, 5) }, stored.Members.Select(m => (m.DrawingId, m.Quantity)));
                Assert.Equal(2, stored.Revision);
                Assert.Contains("'SYN-A' is already in set 'Synthetic kit'", Assert.Single(messages));
            },
            TestTimeout,
            "The add to set test did not complete.");
    }

    [Fact]
    public void ChooseSet_ListsActiveSetsOnly_ChoosesTheSelectedOneOrNew()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var sets = new InMemoryDrawingSets();
                var active = Run(sets.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = "Synthetic kit" }));
                var hidden = Run(sets.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = "Synthetic old kit" }));
                Run(sets.ArchiveAsync(hidden.Id, 1));

                using (var picker = new ChooseDrawingSetForm(sets, library, Server))
                {
                    picker.Show();
                    var grid = Field<DataGridView>(picker.Browser, "setGrid");
                    var choose = Field<Button>(picker, "chooseButton");
                    PumpUntil(() => grid.Rows.Count == 1 && choose.Enabled, "the active set");

                    Assert.False(Field<CheckBox>(picker.Browser, "showArchivedCheckBox").Visible);
                    Assert.False(Field<Button>(picker.Browser, "editButton").Visible);
                    choose.PerformClick();

                    Assert.Equal(DialogResult.OK, picker.DialogResult);
                    Assert.Equal(active.Id, picker.PickedSet!.Id);
                    Assert.False(picker.PickedNew);
                }

                using (var picker = new ChooseDrawingSetForm(sets, library, Server))
                {
                    picker.Show();
                    Run(picker.Browser.NewSetAsync());

                    Assert.Equal(DialogResult.OK, picker.DialogResult);
                    Assert.True(picker.PickedNew);
                    Assert.Null(picker.PickedSet);
                }
            },
            TestTimeout,
            "The choose set test did not complete.");
    }

    [Fact]
    public void UsedInSets_ListsTheSetsContainingTheDrawing()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var drawing = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                Run(sets.CreateAsync(new DrawingSetRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Synthetic kit",
                    Customer = "Synthetic customer",
                    Members = new[] { new DrawingSetMember { DrawingId = drawing.Id, Quantity = 2, Drawing = drawing } },
                }));
                var messages = new List<string>();

                using var form = new DrawingLibraryForm(library, Server, MatchingNest(), sets);
                form.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                form.Show();
                PumpUntil(() => Field<DataGridView>(form, "drawingGrid").Rows.Count == 1, "the library page");

                Run(form.ShowUsedInSetsAsync());

                Assert.Contains("is in 1 drawing set", Assert.Single(messages));
                Assert.Contains("Synthetic kit (Synthetic customer)", messages[0]);
            },
            TestTimeout,
            "The used in sets test did not complete.");
    }

    [Fact]
    public void Browser_ArchivesAfterConfirming_AndRestoresFromShowArchived()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var sets = new InMemoryDrawingSets();
                Run(sets.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = "Synthetic kit" }));
                var answers = new Queue<DialogResult>(new[] { DialogResult.Cancel, DialogResult.OK });

                using var host = new Form();
                var browser = new DrawingSetBrowser { Dock = DockStyle.Fill };
                host.Controls.Add(browser);
                browser.Initialize(sets, library, Server);
                browser.ShowMessage = (_, _, _) => answers.Dequeue();
                host.Show();
                var grid = Field<DataGridView>(browser, "setGrid");
                Run(browser.RefreshAsync());
                PumpUntil(() => grid.Rows.Count == 1, "the set list");

                Run(browser.ToggleArchivedAsync());
                Assert.False(Assert.Single(sets.Current).Archived);
                Run(browser.ToggleArchivedAsync());
                PumpUntil(() => grid.Rows.Count == 0, "the archived set to leave the list");
                Assert.True(Assert.Single(sets.Current).Archived);

                Field<CheckBox>(browser, "showArchivedCheckBox").Checked = true;
                PumpUntil(() => grid.Rows.Count == 1, "the archived set");
                Assert.Equal("Archived", grid.Rows[0].Cells["stateColumn"].Value);
                Assert.Equal("Restore", Field<Button>(browser, "archiveButton").Text);
                Assert.False(Field<Button>(browser, "editButton").Enabled);
                Run(browser.ToggleArchivedAsync());

                Assert.False(Assert.Single(sets.Current).Archived);
                Assert.Equal(3, sets.Current.Single().Revision);
            },
            TestTimeout,
            "The archive test did not complete.");
    }

    [Fact]
    public void Editor_EditsQuantityOrderAndMembers_AndAsksBeforeDiscarding()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var b = library.Add("SYN-B", "Synthetic Steel", 0.25);
                var c = library.Add("SYN-C", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                var created = Run(sets.CreateAsync(new DrawingSetRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Synthetic kit",
                    Members = new[] { a, b, c }.Select(d => new DrawingSetMember { DrawingId = d.Id, Quantity = 1, Drawing = d }).ToList(),
                }));
                var questions = new List<string>();

                using var editor = new EditDrawingSetForm(new DrawingSetEditSession(sets, created, "Synthetic operator"), library, Server);
                editor.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.No; };
                editor.Show();
                var grid = Field<DataGridView>(editor, "memberGrid");
                var save = Field<Button>(editor, "saveButton");
                Assert.False(save.Enabled);

                grid.CurrentCell = grid.Rows[0].Cells["quantityColumn"];
                Assert.True(grid.BeginEdit(true));
                grid.EditingControl!.Text = "0";
                grid.EndEdit();
                Assert.Equal(1, grid.Rows[0].Cells["quantityColumn"].Value);
                Assert.Contains("1 to 100,000", grid.Rows[0].ErrorText);
                Assert.False(save.Enabled);
                Assert.True(grid.BeginEdit(true));
                grid.EditingControl!.Text = "12";
                Assert.True(grid.EndEdit());
                Assert.Equal("", grid.Rows[0].ErrorText);
                grid.CurrentCell = grid.Rows[2].Cells["drawingColumn"];
                Field<Button>(editor, "upButton").PerformClick();
                grid.CurrentCell = grid.Rows[2].Cells["drawingColumn"];
                Field<Button>(editor, "removeButton").PerformClick();
                Assert.True(save.Enabled);

                editor.Close();
                Assert.False(editor.IsDisposed);
                Assert.Contains("Discard", Assert.Single(questions));

                Run(editor.SaveAsync());
                var stored = sets.Current.Single();
                Assert.Equal(new[] { ("SYN-A", 12), ("SYN-C", 1) }, stored.Members.Select(m => (m.Drawing!.Name, m.Quantity)));
                Assert.Equal((2, "Synthetic operator"), (stored.Revision, stored.SavedBy));
                Assert.Equal(DialogResult.OK, editor.DialogResult);
            },
            TestTimeout,
            "The set editor test did not complete.");
    }

    [Fact]
    public void History_ListsRevisionsNewestFirst_AndTheSelectedRevisionsMembers()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                var created = Run(sets.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = "Synthetic kit" }));
                Run(sets.UpdateAsync(created.Id, new DrawingSetRecord
                {
                    Name = "Synthetic kit",
                    Members = new[] { new DrawingSetMember { DrawingId = a.Id, Quantity = 6, Drawing = a } },
                }, 1));

                using var form = new DrawingSetHistoryForm(sets, created);
                form.Show();
                var revisions = Field<DataGridView>(form, "revisionGrid");
                var members = Field<DataGridView>(form, "memberGrid");
                PumpUntil(() => revisions.Rows.Count == 2 && members.Rows.Count == 1, "the history");

                Assert.Equal(new object[] { 2, 1 }, revisions.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["revisionColumn"].Value));
                Assert.Equal(("SYN-A", 6), (members.Rows[0].Cells["drawingColumn"].Value, members.Rows[0].Cells["quantityColumn"].Value));
                revisions.CurrentCell = revisions.Rows[1].Cells[0];
                PumpUntil(() => members.Rows.Count == 0 && Field<GroupBox>(form, "membersGroup").Text.EndsWith("(0)"), "revision 1");
            },
            TestTimeout,
            "The set history test did not complete.");
    }

    [Fact]
    public void NestDrawingsToSet_AppendsTheNestsLibraryDrawings_KeepsSetQuantities_AndReportsSkips()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var existing = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var added = library.Add("SYN-B", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                var set = Run(sets.CreateAsync(new DrawingSetRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Synthetic kit",
                    Members = new[] { new DrawingSetMember { DrawingId = existing.Id, Quantity = 1, Drawing = existing } },
                }));
                var nest = MatchingNest();
                nest.Drawings.Add(LinkedDrawing(existing, 6));
                nest.Drawings.Add(LinkedDrawing(added, 4));
                nest.Drawings.Add(new Drawing("SYN-LOOSE") { Quantity = { Required = 2 } });
                var messages = new List<string>();

                var command = new NestDrawingsToSet(null, sets, library, Server);
                command.ShowMessage = (text, buttons, _) =>
                {
                    messages.Add(text);
                    Assert.Equal(MessageBoxButtons.OKCancel, buttons);
                    return DialogResult.OK;
                };
                command.ChooseSet = () => (true, set);
                command.EditSet = edit => Run(edit.SaveAsync());
                var saved = Run(command.RunAsync(nest));

                Assert.Equal(2, saved.Revision);
                Assert.Equal(new[] { (existing.Id, 1), (added.Id, 4) }, saved.Members.Select(m => (m.DrawingId, m.Quantity)));
                var message = Assert.Single(messages);
                Assert.StartsWith("Added 1 drawing to set 'Synthetic kit'.", message);
                Assert.Contains("- SYN-A: set 1, nest 6", message);
                Assert.Contains("- SYN-LOOSE: not in the drawing library", message);
            },
            TestTimeout,
            "The nest drawings to set test did not complete.");
    }

    [Fact]
    public void NestDrawingsToSet_NamesANewSetAfterTheNest_AndAsksNothingWhenEveryDrawingIsCurrent()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new FakeLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel", 0.25);
                var sets = new InMemoryDrawingSets();
                var nest = MatchingNest();
                nest.Customer = "Synthetic customer";
                nest.Drawings.Add(LinkedDrawing(a, 3));
                DrawingSetEditSession edited = null;

                var command = new NestDrawingsToSet(null, sets, library, Server);
                command.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException(text);
                command.ChooseSet = () => (true, null);
                command.EditSet = edit =>
                {
                    edited = edit;
                    return null;
                };
                Assert.Null(Run(command.RunAsync(nest)));

                Assert.True(edited!.IsNew);
                Assert.Equal(("Synthetic job", "Synthetic customer"), (edited.Name, edited.Customer));
                Assert.Equal((a.Id, 3), edited.Members.Select(m => (m.DrawingId, m.Quantity)).Single());
                Assert.Empty(sets.Creates);
            },
            TestTimeout,
            "The nest drawings to new set test did not complete.");
    }

    [Fact]
    public void NestDrawingsToSet_WithoutLibraryDrawings_SaysSoBeforeChoosingASet()
    {
        StaTestThread.Run(
            () =>
            {
                var nest = MatchingNest();
                nest.Drawings.Add(new Drawing("SYN-LOOSE") { Quantity = { Required = 1 } });
                var messages = new List<string>();

                var command = new NestDrawingsToSet(null, new InMemoryDrawingSets(), new FakeLibrary(), Server);
                command.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                command.ChooseSet = () => throw new Xunit.Sdk.XunitException("No set should be chosen.");
                Assert.Null(Run(command.RunAsync(nest)));

                var message = Assert.Single(messages);
                Assert.Contains("Save them to the drawing library first", message);
                Assert.Contains("- SYN-LOOSE:", message);
            },
            TestTimeout,
            "The nest without library drawings test did not complete.");
    }

    private static Drawing LinkedDrawing(DrawingLibraryRecord record, int required) => new(record.Name)
    {
        Quantity = { Required = required },
        Library = new DrawingLibraryLink(Server, record.Id, record.Revision, record.CutType, record.Thickness,
            record.MaterialName, record.MaterialGrade, record.Units, record.Notes, ""),
    };

    private static Nest MatchingNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Material = new Material("Synthetic Steel", "Grade A"),
    };

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

    /// <summary>Serves every record, ignoring filters; packages are never needed here.</summary>
    private sealed class FakeLibrary : IDrawingLibraryStore
    {
        private readonly List<DrawingLibraryRecord> _records = new();

        public List<DrawingLibraryQuery> Queries { get; } = new();

        public DrawingLibraryRecord Add(string name, string material, double thickness)
        {
            var record = new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Revision = 1,
                Name = name,
                MaterialName = material,
                MaterialGrade = "Grade A",
                Thickness = thickness,
                Units = Units.Inches,
                CutType = CutType.Laser,
                SavedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            };
            _records.Add(record);
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

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.FirstOrDefault(r => r.Id == id));

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
