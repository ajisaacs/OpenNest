using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;
using OpenNest.Shapes;
using OpenNest.Tests.Data;
using DrawingSetBrowser = OpenNest.Controls.DrawingSetBrowser;

namespace OpenNest.WinForms.Tests.Forms;

public class SetReleaseFormsTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void Release_CreatesOneDraftNestPerTickedGroup_OpensEach_AndRecordsTheRelease()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var b = library.Add("SYN-B", "Synthetic Steel");
                var c = library.Add("SYN-C", "Synthetic Aluminum");
                var old = library.Add("SYN-OLD", "Synthetic Steel", archived: true);
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 2), (b, 1), (c, 4), (old, 1));
                var nests = new MemoryNests();
                var opened = new List<(Nest Nest, NestRecord Record)>();
                var questions = new List<string>();

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (nest, record) => opened.Add((nest, record)));
                form.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                var created = 0;
                form.CreateNest = (group, _) => Task.FromResult(new Nest($"N26-{++created:D4}") { Units = group.Units });
                form.Show();
                var grid = Field<DataGridView>(form, "groupGrid");
                var skipped = Field<Label>(form, "skippedLabel");

                Assert.Equal(2, grid.Rows.Count);
                Assert.True(skipped.Visible);
                Assert.Contains("'SYN-OLD'", skipped.Text);
                Field<DataGridView>(form, "setGrid").Rows[0].Cells["kitsColumn"].Value = "3";
                Assert.Equal(new object[] { 12, 9 }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["partsColumn"].Value));
                Field<TextBox>(form, "notesBox").Text = "Synthetic run";

                Run(form.ReleaseAsync());

                Assert.Contains("Make 2 Draft nests from set 'Synthetic kit' ×3?", Assert.Single(questions));
                Assert.Equal(2, nests.Uploaded.Count);
                Assert.Equal(2, opened.Count);
                Assert.All(opened, o => Assert.Equal(NestStatus.Draft, o.Nest.Status));
                var steel = opened.Single(o => o.Nest.Material.Name == "Synthetic Steel");
                Assert.Equal(new[] { ("SYN-A", 6), ("SYN-B", 3) },
                    steel.Nest.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required)));
                Assert.Equal(steel.Record.Id, nests.Uploaded.Single(u => u.Name == steel.Nest.Name).Id);

                var release = Assert.Single(sets.Releases);
                Assert.Equal((3, "Synthetic run", 2), (release.Sets.Single().Quantity, release.Notes, release.Nests.Count));
                Assert.Same(release, form.Result!.Release);
                Assert.StartsWith("Done: 2 nests", Field<Label>(form, "statusLabel").Text);
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), r => Assert.StartsWith("Created N26-", (string)r.Cells["statusColumn"].Value));
                Assert.False(Field<Button>(form, "releaseButton").Enabled);
                Assert.True(Field<DataGridView>(form, "setGrid").ReadOnly);
                Assert.False(Field<ComboBox>(form, "addSetCombo").Enabled);
            },
            TestTimeout,
            "The release test did not complete.");
    }

    [Fact]
    public void UntickedAndRefusedGroups_AreNotReleased()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var twin = library.Add("SYN-A", "Synthetic Steel");
                var c = library.Add("SYN-C", "Synthetic Aluminum");
                var d = library.Add("SYN-D", "Synthetic Copper");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1), (twin, 1), (c, 1), (d, 1));
                var nests = new MemoryNests();

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (_, _) => { });
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.Show();
                var grid = Field<DataGridView>(form, "groupGrid");
                var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
                var refused = rows.Single(r => ((SetReleaseGroup)r.Tag!).MaterialName == "Synthetic Steel");
                var copper = rows.Single(r => ((SetReleaseGroup)r.Tag!).MaterialName == "Synthetic Copper");

                Assert.Equal(false, refused.Cells["includeColumn"].Value);
                Assert.True(refused.Cells["includeColumn"].ReadOnly);
                Assert.Contains("named 'SYN-A'", (string)refused.Cells["statusColumn"].Value);
                copper.Cells["includeColumn"].Value = false;
                Assert.Contains("1 of 3 materials chosen", Field<Label>(form, "summaryLabel").Text);
                // Rebuilding the plan keeps the operator's choice.
                Field<CheckBox>(form, "includeArchivedCheckBox").Checked = true;
                Assert.Equal(false, grid.Rows.Cast<DataGridViewRow>()
                    .Single(r => ((SetReleaseGroup)r.Tag!).MaterialName == "Synthetic Copper").Cells["includeColumn"].Value);

                Run(form.ReleaseAsync());

                Assert.Equal("Synthetic Aluminum", Assert.Single(Assert.Single(sets.Releases).Nests).MaterialName);
                Assert.Single(nests.Uploaded);
            },
            TestTimeout,
            "The release choice test did not complete.");
    }

    [Fact]
    public void Release_PreviewsTheNestsWithOptionsCollapsed_AndKeepsChangedOptionsShown()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var c = library.Add("SYN-C", "Synthetic Aluminum");
                var old = library.Add("SYN-OLD", "Synthetic Steel", archived: true);
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 2), (c, 1), (old, 1));
                var nests = new MemoryNests();

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (_, _) => { });
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.Show();
                var grid = Field<DataGridView>(form, "groupGrid");
                var archived = Field<CheckBox>(form, "includeArchivedCheckBox");
                var options = Field<Button>(form, "optionsButton");
                var preview = Field<Label>(form, "previewLabel");
                Run(form.LoadDraftsAsync());

                // Collapsed: the defaults, read-only, with no draft to merge into.
                Assert.False(form.OptionsExpanded);
                Assert.Equal("Release set 'Synthetic kit' ×1", Field<Label>(form, "setLabel").Text);
                Assert.Equal("Creates 2 nests: Synthetic Aluminum Grade A 0.25 in, Synthetic Steel Grade A 0.25 in.", preview.Text);
                Assert.Equal((false, false, false), (grid.Columns["includeColumn"]!.Visible, grid.Columns["targetColumn"]!.Visible, archived.Visible));
                Assert.Contains("under Options", Field<Label>(form, "skippedLabel").Text);
                Assert.True(grid.Rows.Cast<DataGridViewRow>().All(r => (bool)r.Cells["includeColumn"].Value));
                Assert.Equal(new object[] { "New Draft nest", "New Draft nest" }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["targetColumn"].Value));
                Assert.True(Field<CheckBox>(form, "nestAfterReleaseCheckBox").Checked);

                Field<DataGridView>(form, "setGrid").Rows[0].Cells["kitsColumn"].Value = "3";
                Assert.Equal("Release set 'Synthetic kit' ×3", Field<Label>(form, "setLabel").Text);

                options.PerformClick();
                Assert.True(form.OptionsExpanded);
                Assert.Equal("Options ▾", options.Text);
                Assert.Equal((true, true, true), (grid.Columns["includeColumn"]!.Visible, grid.Columns["targetColumn"]!.Visible, archived.Visible));

                // Options left changed stay shown when collapsed; the defaults hide again.
                grid.Rows[0].Cells["includeColumn"].Value = false;
                archived.Checked = true;
                options.PerformClick();
                Assert.Equal("Options ▸", options.Text);
                Assert.Equal((true, false, true), (grid.Columns["includeColumn"]!.Visible, grid.Columns["targetColumn"]!.Visible, archived.Visible));
                Assert.Equal("Creates 1 nest: Synthetic Steel Grade A 0.25 in. 1 material is left out (unticked under Release).", preview.Text);

                grid.Rows[0].Cells["includeColumn"].Value = true;
                archived.Checked = false;
                // Nothing vanishes while collapsed until Options is toggled.
                Assert.True(grid.Columns["includeColumn"]!.Visible);
                options.PerformClick();
                options.PerformClick();
                Assert.Equal((false, false, false), (grid.Columns["includeColumn"]!.Visible, grid.Columns["targetColumn"]!.Visible, archived.Visible));

                Run(form.ReleaseAsync());

                Assert.Equal(2, Assert.Single(sets.Releases).Nests.Count);
            },
            TestTimeout,
            "The collapsed options test did not complete.");
    }

    [Fact]
    public void FixDrawings_EditsEachDrawingTheGroupIsRefusedFor_ThenTheGroupCanBeReleased()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var noMaterial = library.Add("SYN-B", "");
                var noThickness = library.Add("SYN-C", "Synthetic Steel", thickness: 0);
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1), (noMaterial, 2), (noThickness, 3));
                var nests = new MemoryNests();
                var edits = new List<string>();

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (_, _) => { });
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.EditDrawing = edit =>
                {
                    edits.Add(edit.Listed.Name);
                    return Run(edit.SaveAsync(edit.Details with { MaterialName = "Synthetic Steel", Thickness = 0.25 }, edit.Listed.Notes, edit.Listed.Settings ?? new DrawingNestingSettings()));
                };
                Field<CheckBox>(form, "nestAfterReleaseCheckBox").Checked = false;
                form.Show();
                var grid = Field<DataGridView>(form, "groupGrid");
                var fix = Field<Button>(form, "fixDrawingsButton");
                var refused = grid.Rows.Cast<DataGridViewRow>().Single(r => ((SetReleaseGroup)r.Tag!).IsUnassigned);
                Assert.Contains("Edit Drawing", (string)refused.Cells["statusColumn"].Value);
                Assert.Contains("1 material can't be released yet: choose Fix Drawings.", Field<Label>(form, "previewLabel").Text);
                // With a releasable group selected, Fix Drawings takes the refused one.
                grid.CurrentCell = grid.Rows[0].Cells["materialColumn"];
                Assert.NotSame(refused, grid.CurrentRow);
                Assert.True(fix.Enabled);

                Run(form.FixSelectedGroupAsync());

                Assert.Equal(new[] { "SYN-B", "SYN-C" }, edits);
                Assert.False(fix.Enabled);
                var group = Assert.IsType<SetReleaseGroup>(Assert.Single(grid.Rows.Cast<DataGridViewRow>()).Tag);
                Assert.True(group.CanRelease);
                Assert.Equal(new[] { ("SYN-A", 1), ("SYN-B", 2), ("SYN-C", 2) }, group.Items.Select(i => (i.Drawing.Name, i.Drawing.Revision)));

                Run(form.ReleaseAsync());

                Assert.Single(nests.Uploaded);
                Assert.Equal("Synthetic Steel", Assert.Single(Assert.Single(sets.Releases).Nests).MaterialName);
            },
            TestTimeout,
            "The fix drawings test did not complete.");
    }

    [Fact]
    public void ReleaseNotRecorded_CanBeRecordedAgain()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1));
                sets.FailNextPost = true;
                var opened = 0;

                using var form = new ReleaseSetForm(sets, library, new MemoryNests(), Server, set, (_, _) => opened++);
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.Show();
                var release = Field<Button>(form, "releaseButton");

                Run(form.ReleaseAsync());

                Assert.Equal(1, opened);
                Assert.Empty(sets.Releases);
                Assert.Equal(("Try Again", true), (release.Text, release.Enabled));
                Assert.EndsWith("Synthetic release post failure. Try Again records it; the nests are not made again.",
                    Field<Label>(form, "statusLabel").Text);

                Run(form.RetryRecordAsync());

                Assert.Equal(2, sets.PostAttempts);
                Assert.Same(Assert.Single(sets.Releases), form.Result!.Release);
                Assert.Equal(("Make Nests...", false), (release.Text, release.Enabled));
            },
            TestTimeout,
            "The release retry test did not complete.");
    }

    [Fact]
    public void Releases_ListsEachReleaseWithProgress_AndItsNestsStates()
    {
        StaTestThread.Run(
            () =>
            {
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets);
                SetReleaseNest Nest(string name, NestStatus status, bool deleted = false) => new()
                {
                    NestId = Guid.NewGuid(),
                    MaterialName = "Synthetic Steel",
                    Thickness = 0.25,
                    Nest = new NestRecord { Name = name, Status = status, Deleted = deleted },
                };
                sets.Releases.Add(new SetReleaseRecord
                {
                    Id = Guid.NewGuid(),
                    Sets = new[] { new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name, Quantity = 2 } },
                    ReleasedBy = "Synthetic operator",
                    ReleasedAt = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
                    Nests = new[] { Nest("N1", NestStatus.HasBeenCut), Nest("N2", NestStatus.ToBeCut), Nest("N3", NestStatus.Draft, deleted: true) },
                });

                using var form = new SetReleasesForm(sets, set);
                form.Show();
                var releases = Field<DataGridView>(form, "releaseGrid");
                var nests = Field<DataGridView>(form, "nestGrid");
                PumpUntil(() => releases.Rows.Count == 1 && nests.Rows.Count == 3, "the releases");

                Assert.Equal("1 of 2 cut, 1 deleted", releases.Rows[0].Cells["progressColumn"].Value);
                Assert.Equal("×2", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.Equal(new object[] { "Has Been Cut", "To Be Cut", "Deleted" },
                    nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["statusColumn"].Value));
            },
            TestTimeout,
            "The releases view test did not complete.");
    }

    [Fact]
    public void Release_OfATypedInSecondSet_MergesOneGroupIntoADraft_UnlessItIsOpen()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var b = library.Add("SYN-B", "Synthetic Steel");
                var c = library.Add("SYN-C", "Synthetic Aluminum");
                var sets = new InMemoryDrawingSets();
                var first = CreateSet(sets, "First kit", (a, 1), (c, 1));
                CreateSet(sets, "Second kit", (b, 2));
                var nests = new MemoryNests();
                var draft = nests.AddDraft("N26-0007", "Synthetic Steel", "Grade A", 0.25);
                var opened = new List<NestRecord>();
                var messages = new List<string>();

                using var form = new ReleaseSetForm(sets, library, nests, Server, first, (_, record) => opened.Add(record));
                form.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N26-0100") { Units = group.Units });
                var openTabs = new HashSet<Guid> { draft.Id };
                form.IsNestOpen = openTabs.Contains;
                form.Show();

                Field<ComboBox>(form, "addSetCombo").Text = "second KIT";
                Run(form.AddTypedSetAsync());
                Assert.Equal(new[] { ("First kit", 1), ("Second kit", 1) }, form.Lines().Select(l => (l.Set.Name, l.Quantity)));
                Assert.False(form.AddLine(first, 2));
                Field<DataGridView>(form, "setGrid").Rows[1].Cells["kitsColumn"].Value = "2";

                var grid = Field<DataGridView>(form, "groupGrid");
                DataGridViewRow Steel() => grid.Rows.Cast<DataGridViewRow>().Single(r => ((SetReleaseGroup)r.Tag!).MaterialName == "Synthetic Steel");
                PumpUntil(() => ((DataGridViewComboBoxCell)Steel().Cells["targetColumn"]).Items.Count == 2, "the drafts to merge into");
                // A draft to merge into shows the Nest column while Options is collapsed.
                Assert.False(form.OptionsExpanded);
                Assert.True(grid.Columns["targetColumn"]!.Visible);
                Assert.Contains("A Draft nest of the same material exists", Field<Label>(form, "previewLabel").Text);
                Assert.Equal(new object[] { 1, 4 }, new[] { a, b }.Select(d => (object)((SetReleaseGroup)Steel().Tag!).Items.Single(i => i.Drawing.Id == d.Id).Quantity));
                var mergeChoice = (string)((DataGridViewComboBoxCell)Steel().Cells["targetColumn"]).Items[1]!;
                Assert.StartsWith("Merge into N26-0007 (1 sheet", mergeChoice);

                // An open draft is refused and the group stays a new nest.
                Steel().Cells["targetColumn"].Value = mergeChoice;
                Assert.Contains("is open in a tab here", Assert.Single(messages));
                Assert.Equal("New Draft nest", Steel().Cells["targetColumn"].Value);
                Assert.Empty(form.ChosenMerges());

                openTabs.Clear();
                Steel().Cells["targetColumn"].Value = mergeChoice;
                Assert.Same(draft, form.ChosenMerges().Values.Single());
                Assert.Contains("Replaces the layout of 'N26-0007'", (string)Steel().Cells["statusColumn"].Value);
                Assert.Contains("1 new Draft nest, 1 merge, 6 parts", Field<Label>(form, "summaryLabel").Text);
                Assert.Contains("Creates 1 nest and merges into 1 Draft nest: ", Field<Label>(form, "previewLabel").Text);
                Assert.Contains("Synthetic Steel Grade A 0.25 in into 'N26-0007'", Field<Label>(form, "previewLabel").Text);

                Run(form.ReleaseAsync());

                Assert.Contains("1 new, 1 merged into existing drafts", messages[^1]);
                var release = Assert.Single(sets.Releases);
                Assert.Equal(new[] { ("First kit", 1), ("Second kit", 2) }, release.Sets.Select(l => (l.SetName, l.Quantity)));
                var merged = release.Nests.Single(n => n.Kind == SetReleaseNestKind.Merged);
                Assert.Equal((draft.Id, (int?)draft.Revision, draft.Revision + 1), (merged.NestId, merged.RevisionBefore, merged.RevisionAfter));
                Assert.Equal(SetReleaseNestKind.Created, release.Nests.Single(n => n.NestId != draft.Id).Kind);
                Assert.Equal(2, opened.Count);
                Assert.StartsWith("Merged into N26-0007", (string)Steel().Cells["statusColumn"].Value);
            },
            TestTimeout,
            "The multi-set merge release test did not complete.");
    }

    [Fact]
    public void NestAfterRelease_NestsEachGroupWithAutoNest_AndListsItsSheets()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 3));
                var opened = new List<Nest>();
                var questions = new List<string>();

                using var form = new ReleaseSetForm(sets, library, new MemoryNests(), Server, set, (nest, _) => opened.Add(nest));
                form.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                form.CreateNest = (group, _) =>
                {
                    var nest = new Nest("N26-0200") { Units = group.Units };
                    nest.PlateDefaults.Size = new OpenNest.Geometry.Size(48, 96);
                    return Task.FromResult(nest);
                };
                form.Show();
                Assert.True(Field<CheckBox>(form, "nestAfterReleaseCheckBox").Checked);

                Run(form.ReleaseAsync());

                Assert.Contains("Each is nested with Auto Nest", Assert.Single(questions));
                var nest = Assert.Single(opened);
                Assert.Equal(3, nest.Plates.Sum(p => p.Parts.Count * p.Quantity));
                Assert.Equal(0, Assert.Single(nest.Drawings).Quantity.Remaining);
                var status = (string)Field<DataGridView>(form, "groupGrid").Rows[0].Cells["statusColumn"].Value;
                Assert.StartsWith("Created N26-0200 — Nested on 1 sheet, ", status);
            },
            TestTimeout,
            "The nest after release test did not complete.");
    }

    [Fact]
    public void NestAfterRelease_Unticked_LeavesNestsAndMergedDraftsForAutoNest()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 2));
                var nests = new MemoryNests();
                var draft = nests.AddDraft("N26-0007", "Synthetic Steel", "Grade A", 0.25);
                var questions = new List<string>();
                var renested = 0;

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (_, _) => { });
                form.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.Renest = (_, _) => { renested++; return Task.FromResult(new SetReleaseRenest(true, "")); };
                form.Show();
                var row = Field<DataGridView>(form, "groupGrid").Rows[0];
                PumpUntil(() => ((DataGridViewComboBoxCell)row.Cells["targetColumn"]).Items.Count == 2, "the drafts to merge into");
                row.Cells["targetColumn"].Value = ((DataGridViewComboBoxCell)row.Cells["targetColumn"]).Items[1];
                Assert.Contains("and nests again", (string)row.Cells["statusColumn"].Value);

                Field<CheckBox>(form, "nestAfterReleaseCheckBox").Checked = false;
                Assert.Contains("and leaves it for Auto Nest", (string)row.Cells["statusColumn"].Value);

                Run(form.ReleaseAsync());

                Assert.Equal(0, renested);
                Assert.Contains("layout is cleared for Auto Nest", Assert.Single(questions));
                Assert.DoesNotContain("nested with Auto Nest", questions[0]);
                var merged = Assert.Single(form.Result!.Created);
                Assert.Same(draft, merged.MergedInto);
                Assert.Empty(merged.Nest!.Plates);
                Assert.Null(merged.Renest);
                Assert.False(Field<CheckBox>(form, "nestAfterReleaseCheckBox").Enabled);
            },
            TestTimeout,
            "The release without nesting test did not complete.");
    }

    [Fact]
    public void Releases_UndoOffersToLeaveChangedNests_AndShowsWhatTheReleaseDid()
    {
        StaTestThread.Run(
            () =>
            {
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets);
                var created = new SetReleaseNest { NestId = Guid.NewGuid(), MaterialName = "Synthetic Steel", Thickness = 0.25, RevisionAfter = 1, Nest = new NestRecord { Name = "N1", Status = NestStatus.Draft } };
                var merged = new SetReleaseNest
                {
                    NestId = Guid.NewGuid(),
                    Kind = SetReleaseNestKind.Merged,
                    RevisionBefore = 3,
                    RevisionAfter = 4,
                    MaterialName = "Synthetic Aluminum",
                    Thickness = 0.25,
                    Nest = new NestRecord { Name = "N2", Status = NestStatus.Draft },
                };
                var release = new SetReleaseRecord
                {
                    Id = Guid.NewGuid(),
                    Sets = new[]
                    {
                        new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name, Quantity = 2 },
                        new SetReleaseLine { SetId = Guid.NewGuid(), SetRevision = 1, SetName = "Other kit", Quantity = 1 },
                    },
                    Nests = new[] { created, merged },
                };
                sets.Releases.Add(release);
                sets.UndoHandler = (_, request) =>
                {
                    if (request.SkipNests.Count == 0)
                        throw new SetReleaseUndoConflictException(new SetReleaseUndoConflict
                        {
                            Reason = SetReleaseUndoRefusal.NestsChanged,
                            Release = release,
                            ChangedNests = new[] { new NestRecord { Id = merged.NestId, Name = "N2", Revision = 6, Status = NestStatus.ToBeCut } },
                        });
                    release.UndoneAt = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
                    created.Undo = SetReleaseNestUndo.Deleted;
                    merged.Undo = SetReleaseNestUndo.Skipped;
                    return release;
                };
                var questions = new List<string>();

                using var form = new SetReleasesForm(sets, set);
                form.ShowMessage = (text, buttons, _) =>
                {
                    questions.Add(text);
                    return buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK;
                };
                form.Show();
                var releases = Field<DataGridView>(form, "releaseGrid");
                var nests = Field<DataGridView>(form, "nestGrid");
                PumpUntil(() => releases.Rows.Count == 1 && nests.Rows.Count == 2, "the releases");
                Assert.Equal("×2 (+1 other set)", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.Equal(new object[] { "Created", "Merged (revision 3 → 4)" }, nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["changeColumn"].Value));
                Assert.True(Field<Button>(form, "undoButton").Enabled);

                Run(form.UndoSelectedAsync());

                Assert.Contains("1 nest it created is deleted", questions[0]);
                Assert.Contains("1 draft it merged into goes back", questions[0]);
                Assert.Contains("- N2: saved since the release (now revision 6, To Be Cut)", questions[1]);
                Assert.Equal(new[] { 0, 1 }, sets.UndoRequests.Select(r => r.Request.SkipNests.Count));
                Assert.Equal(merged.NestId, sets.UndoRequests[1].Request.SkipNests.Single());
                PumpUntil(() => (string)releases.Rows[0].Cells["progressColumn"].Value == "Undone", "the release to read as undone");
                Assert.Equal(new object[] { "Created; undo deleted it", "Merged (revision 3 → 4); undo left it as it was" },
                    nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["changeColumn"].Value));
                Assert.False(Field<Button>(form, "undoButton").Enabled);
            },
            TestTimeout,
            "The release undo test did not complete.");
    }

    [Fact]
    public void Browser_Release_ReadsTheSetWithItsMembers_OnlyWhenEnabled()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                CreateSet(sets, (a, 2));
                var released = new List<DrawingSetRecord>();

                using var host = new Form();
                var browser = new DrawingSetBrowser { Dock = DockStyle.Fill };
                host.Controls.Add(browser);
                browser.Initialize(sets, library, Server);
                browser.ShowRelease = released.Add;
                host.Show();
                Run(browser.RefreshAsync());
                var releaseButton = Field<Button>(browser, "releaseButton");
                Assert.False(releaseButton.Visible);

                browser.EnableRelease(new MemoryNests(), (_, _) => { });
                Assert.True(releaseButton.Visible);
                Assert.True(releaseButton.Enabled);
                Run(browser.ReleaseSelectedAsync());

                var set = Assert.Single(released);
                Assert.Equal("SYN-A", Assert.Single(set.Members).Drawing!.Name);
            },
            TestTimeout,
            "The browser release test did not complete.");
    }

    [Fact]
    public void Release_OfASetAndDrawingsAddedFromTheLibrary_GroupsThemTogether_AndRecordsBoth()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                library.Add("SYN-B", "Synthetic Steel");
                library.Add("SYN-C", "Synthetic Aluminum");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 2));
                var nests = new MemoryNests();
                var questions = new List<string>();

                using var form = new ReleaseSetForm(sets, library, nests, Server, set, (_, _) => { });
                form.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                var created = 0;
                form.CreateNest = (group, _) => Task.FromResult(new Nest($"N26-{++created:D4}") { Units = group.Units });
                form.Show();
                using var picker = new AddReleaseDrawingsForm(library, Server, form.AddDrawingLines,
                    id => form.DrawingLines().Any(l => l.Drawing.Id == id));
                picker.Show(form);
                Run(picker.RefreshAsync());

                picker.Select("SYN-A", "SYN-C");
                picker.Quantity = 5;
                picker.AddSelectedDrawings();
                Assert.Equal("Added 2 drawings.", picker.Status);
                picker.Select("SYN-C", "SYN-B");
                picker.Quantity = 1;
                picker.AddSelectedDrawings();
                Assert.Equal("Added 'SYN-B'. Already listed (change the quantity there): 'SYN-C'.", picker.Status);

                var lines = Field<DataGridView>(form, "setGrid");
                Assert.Equal(new object[] { "Set", "Drawing", "Drawing", "Drawing" },
                    lines.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["lineTypeColumn"].Value));
                lines.Rows[2].Cells["kitsColumn"].Value = "40";
                Assert.Equal(new[] { ("SYN-A", 5), ("SYN-C", 40), ("SYN-B", 1) }, form.DrawingLines().Select(l => (l.Drawing.Name, l.Quantity)));
                var grid = Field<DataGridView>(form, "groupGrid");
                Assert.Equal(new object[] { 40, 8 }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["partsColumn"].Value));

                Run(form.ReleaseAsync());

                Assert.Contains("Make 2 Draft nests from set 'Synthetic kit' and 3 drawings?", Assert.Single(questions));
                var release = Assert.Single(sets.Releases);
                Assert.Equal(new[] { 5, 40, 1 }, release.Drawings.Select(d => d.Quantity));
                var steel = release.Nests.Single(n => n.MaterialName == "Synthetic Steel");
                Assert.Equal(new[] { ((Guid?)set.Id, 2), (null, 5), (null, 1) }, steel.Items.Select(i => (i.SetId, i.Quantity)));
                Assert.Equal(2, nests.Uploaded.Count);
                Assert.False(Field<Button>(form, "addDrawingsButton").Enabled);
            },
            TestTimeout,
            "The drawing release test did not complete.");
    }

    [Fact]
    public void Release_OfADrawingAlone_OnAServerWithoutDrawingsOnTheirOwn_SavesNothing()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets { WithoutDrawingReleases = true };
                var nests = new MemoryNests();

                using var form = new ReleaseSetForm(sets, library, nests, Server, Array.Empty<DrawingSetRecord>(),
                    new[] { new SetReleaseDrawingLine(a, 4) }, (_, _) => { });
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N") { Units = group.Units });
                form.Show();

                Assert.Equal("Release 'SYN-A'", form.Text);
                Assert.False(Field<Button>(form, "removeSetButton").Enabled);
                Assert.Contains("4 parts", Field<Label>(form, "summaryLabel").Text);

                Run(form.ReleaseAsync());

                Assert.Empty(nests.Uploaded);
                Assert.Empty(sets.Releases);
                Assert.Equal("Nothing saved. See Status.", Field<Label>(form, "statusLabel").Text);
                Assert.Contains("does not support releasing library drawings on their own",
                    (string)Field<DataGridView>(form, "groupGrid").Rows[0].Cells["statusColumn"].Value);
            },
            TestTimeout,
            "The old-server drawing release test did not complete.");
    }

    [Fact]
    public void Releases_OfADrawing_ListsReleasesThatHadItOnItsOwn_AndUndoesOne()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var release = new SetReleaseRecord
                {
                    Id = Guid.NewGuid(),
                    Sets = new[] { new SetReleaseLine { SetId = Guid.NewGuid(), SetRevision = 1, SetName = "Synthetic kit", Quantity = 1 } },
                    Drawings = new[] { new SetReleaseDrawing { DrawingId = a.Id, DrawingName = a.Name, Quantity = 4 } },
                    ReleasedBy = "Synthetic operator",
                    Nests = new[] { new SetReleaseNest { NestId = Guid.NewGuid(), Nest = new NestRecord { Name = "N1", Status = NestStatus.Draft } } },
                };
                sets.Releases.Add(release);
                sets.UndoHandler = (id, request) => new SetReleaseRecord { Id = id, UndoneAt = DateTime.UtcNow };
                var questions = new List<string>();

                using var form = new SetReleasesForm(sets, a);
                form.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                form.Show();
                var releases = Field<DataGridView>(form, "releaseGrid");
                PumpUntil(() => releases.Rows.Count == 1, "the releases");

                Assert.Equal("Releases of drawing 'SYN-A'", form.Text);
                Assert.Equal("×4 (+1 set)", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.False(releases.Columns["revisionColumn"]!.Visible);

                Run(form.UndoSelectedAsync());

                Assert.Equal(release.Id, Assert.Single(sets.UndoRequests).Id);
                Assert.Contains("deleted (they can be recovered)", Assert.Single(questions));
            },
            TestTimeout,
            "The drawing releases view test did not complete.");
    }

    [Fact]
    public void AllReleases_NamesWhatEachReleased_AndNewReleaseRereadsOnceOneIsRecorded()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1));
                var other = CreateSet(sets, "Synthetic frame", (a, 2));
                SetReleaseRecord Release(DateTime at, SetReleaseLine[] lines, SetReleaseDrawing[] drawings) => new()
                {
                    Id = Guid.NewGuid(),
                    Sets = lines,
                    Drawings = drawings,
                    ReleasedBy = "Synthetic operator",
                    ReleasedAt = at,
                    Nests = new[] { new SetReleaseNest { NestId = Guid.NewGuid(), Nest = new NestRecord { Name = "N1", Status = NestStatus.Draft } } },
                };
                sets.Releases.Add(Release(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
                    new[]
                    {
                        new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name, Quantity = 2 },
                        new SetReleaseLine { SetId = other.Id, SetRevision = 1, SetName = other.Name, Quantity = 1 },
                    },
                    new[] { new SetReleaseDrawing { DrawingId = a.Id, DrawingName = a.Name, Quantity = 4 } }));
                sets.Releases.Add(Release(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc),
                    Array.Empty<SetReleaseLine>(),
                    new[] { new SetReleaseDrawing { DrawingId = a.Id, DrawingName = a.Name, Quantity = 1 } }));

                using var form = new SetReleasesForm(sets);
                var opened = 0;
                form.ShowNewRelease = () =>
                {
                    opened++;
                    // The dialog records a release before it closes; a cancelled dialog records none.
                    if (opened == 2)
                        sets.Releases.Add(Release(DateTime.UtcNow, new[] { new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name } },
                            Array.Empty<SetReleaseDrawing>()));
                    return opened == 2;
                };
                form.Show();
                var releases = Field<DataGridView>(form, "releaseGrid");
                PumpUntil(() => releases.Rows.Count == 2, "the releases");

                Assert.Equal("Releases", form.Text);
                Assert.False(releases.Columns["revisionColumn"]!.Visible);
                Assert.Equal("Released", releases.Columns["multiplierColumn"]!.HeaderText);
                Assert.Equal(new object[] { "SYN-A ×1", "Synthetic kit ×2, Synthetic frame; SYN-A ×4" },
                    releases.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["multiplierColumn"].Value));
                Assert.Equal("Releases 1-2 of 2", Field<Label>(form, "summaryLabel").Text);
                Assert.False(Field<Button>(form, "newReleaseButton").Visible);

                Run(form.NewReleaseAsync());
                Assert.Equal(2, releases.Rows.Count);

                Run(form.NewReleaseAsync());
                PumpUntil(() => releases.Rows.Count == 3, "the re-read releases");
                Assert.Equal("Synthetic kit", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.Equal("Releases 1-3 of 3", Field<Label>(form, "summaryLabel").Text);
            },
            TestTimeout,
            "The all-releases view test did not complete.");
    }

    [Fact]
    public void Release_ListsTheFirstSetsInTheAddSetBox_BeforeAnythingIsTyped()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                CreateSet(sets, "Synthetic kit", (a, 1));
                CreateSet(sets, "Synthetic frame", (a, 2));
                var nests = new MemoryNests();

                using var form = new ReleaseSetForm(sets, library, nests, Server, Array.Empty<DrawingSetRecord>(),
                    Array.Empty<SetReleaseDrawingLine>(), (_, _) => { });
                form.Show();
                var combo = Field<ComboBox>(form, "addSetCombo");
                PumpUntil(() => combo.Items.Count == 2, "the set list");

                Assert.Equal(new[] { "Synthetic frame", "Synthetic kit" }, combo.Items.Cast<object>().Select(i => i.ToString()!.Split(" (")[0]));
                Assert.Equal("", combo.Text);
                Assert.False(combo.DroppedDown);
            },
            TestTimeout,
            "The set list test did not complete.");
    }

    [Fact]
    public void Release_StartedEmpty_AddsASetAndReleasesIt()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 2));
                var nests = new MemoryNests();
                var opened = new List<(Nest Nest, NestRecord Record)>();

                using var form = new ReleaseSetForm(sets, library, nests, Server, Array.Empty<DrawingSetRecord>(),
                    Array.Empty<SetReleaseDrawingLine>(), (nest, record) => opened.Add((nest, record)));
                form.ShowMessage = (_, _, _) => DialogResult.OK;
                form.CreateNest = (group, _) => Task.FromResult(new Nest("N26-0001") { Units = group.Units });
                form.Show();

                Assert.Equal("New Release", form.Text);
                Assert.Empty(Field<DataGridView>(form, "groupGrid").Rows);
                Assert.False(Field<Button>(form, "releaseButton").Enabled);

                Assert.True(form.AddLine(set, 3));
                Assert.Equal("Release 'Synthetic kit'", form.Text);
                Assert.True(Field<Button>(form, "releaseButton").Enabled);

                Run(form.ReleaseAsync());

                Assert.Single(opened);
                Assert.Equal(6, opened[0].Nest.Drawings.Single().Quantity.Required);
                Assert.Equal(3, Assert.Single(sets.Releases).Sets.Single().Quantity);
            },
            TestTimeout,
            "The empty release test did not complete.");
    }

    private static DrawingSetRecord CreateSet(InMemoryDrawingSets sets, params (DrawingLibraryRecord Drawing, int Quantity)[] members) =>
        CreateSet(sets, "Synthetic kit", members);

    private static DrawingSetRecord CreateSet(InMemoryDrawingSets sets, string name, params (DrawingLibraryRecord Drawing, int Quantity)[] members)
    {
        var task = sets.CreateAsync(new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Customer = "Synthetic customer",
            Members = members.Select(m => new DrawingSetMember { DrawingId = m.Drawing.Id, Quantity = m.Quantity, Drawing = m.Drawing }).ToList(),
        });
        return task.GetAwaiter().GetResult();
    }

    private static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the operation to finish");

    private static T Run<T>(Task<T> task)
    {
        Run((Task)task);
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

    /// <summary>Real drawing packages by revision, so the release reads them as the desktop does.</summary>
    private sealed class PackageLibrary : IDrawingLibraryStore
    {
        private readonly List<DrawingLibraryRecord> _records = new();
        private readonly Dictionary<(Guid, int), byte[]> _packages = new();

        public DrawingLibraryRecord Add(string name, string material, bool archived = false, double thickness = 0.25)
        {
            var package = DrawingPackage.Write(new RectangleShape { Name = name, Length = 4, Width = 2 }.GetDrawing(), Units.Inches);
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
                Archived = archived,
                PackageSha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(),
            };
            _records.Add(record);
            _packages[(record.Id, record.Revision)] = package;
            return record;
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            Task.FromResult(_packages.TryGetValue((id, revision), out var package) ? package : null);

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DrawingLibraryPage { Items = _records.ToArray(), Total = _records.Count, Offset = query.Offset, Limit = query.Limit });

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.FirstOrDefault(r => r.Id == id));

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>Stores the next revision of the sent details with the same package.</summary>
        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            var index = _records.FindIndex(r => r.Id == id);
            var current = _records[index];
            if (current.Revision != expectedRevision)
                throw new DrawingLibraryConflictException(current);
            var saved = new DrawingLibraryRecord
            {
                Id = id,
                Revision = expectedRevision + 1,
                Name = record.Name,
                Customer = record.Customer,
                MaterialName = record.MaterialName,
                MaterialGrade = record.MaterialGrade,
                Thickness = record.Thickness,
                Units = current.Units,
                CutType = record.CutType,
                Notes = record.Notes,
                Settings = record.Settings,
                PackageSha256 = current.PackageSha256,
            };
            _records[index] = saved;
            _packages[(id, saved.Revision)] = _packages[(id, current.Revision)];
            return Task.FromResult(saved);
        }
    }

    /// <summary>Stores uploads in memory under their client-allocated ids, and drafts a release can merge into.</summary>
    private sealed class MemoryNests : INestRepository
    {
        private readonly Dictionary<(Guid, int), byte[]> _revisions = new();

        public List<NestRecord> Uploaded { get; } = new();
        public List<NestRecord> Drafts { get; } = new();

        /// <summary>A Draft nest on one empty sheet, stored as a real archive at revision 3.</summary>
        public NestRecord AddDraft(string name, string material, string grade, double thickness)
        {
            var nest = new Nest(name) { Units = Units.Inches, Material = new Material(material, grade), Thickness = thickness, Status = NestStatus.Draft };
            nest.CreatePlate();
            using var stream = new MemoryStream();
            new NestWriter(nest).Write(stream);
            var record = new NestRecord
            {
                Id = Guid.NewGuid(),
                Revision = 3,
                Name = name,
                Material = material,
                MaterialGrade = grade,
                Thickness = thickness,
                Status = NestStatus.Draft,
                PlateCount = 1,
                SavedAt = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
            };
            _revisions[(record.Id, record.Revision)] = stream.ToArray();
            Drafts.Add(record);
            return record;
        }

        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default)
        {
            var found = Drafts.Where(d => query.Status == d.Status && query.Material == d.Material
                && query.MaterialGrade == d.MaterialGrade && query.Thickness == d.Thickness).ToList();
            return Task.FromResult(new NestPage { Items = found, Total = found.Count, Limit = query.Limit });
        }

        public Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revisions.TryGetValue((id, revisionNumber), out var bytes) ? bytes : null);

        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] nestFile, NestRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            var current = Drafts.Single(d => d.Id == id);
            Assert.Equal(current.Revision, expectedRevision);
            var saved = new NestRecord { Id = id, Revision = expectedRevision + 1, Name = record.Name, Status = record.Status };
            _revisions[(id, saved.Revision)] = nestFile;
            return Task.FromResult(saved);
        }

        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default)
        {
            Assert.NotEmpty(file);
            var stored = new NestRecord { Id = record.Id, Revision = 1, Name = record.Name, Status = record.Status };
            Uploaded.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Uploaded.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
