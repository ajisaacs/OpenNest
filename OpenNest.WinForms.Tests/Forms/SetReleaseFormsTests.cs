using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;
using OpenNest.Shapes;
using OpenNest.Tests.Data;
using ReleasePanel = OpenNest.Controls.ReleasePanel;

namespace OpenNest.WinForms.Tests.Forms;

public class SetReleaseFormsTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void MakeNests_CreatesOneDraftNestPerTickedGroup_OpensEach_AndRecordsTheRelease()
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
                var nestsOpened = 0;

                var (host, panel) = ShowPanel(sets, library, nests, (nest, record) => opened.Add((nest, record)));
                using var _ = host;
                panel.NestsOpened += (_, _) => nestsOpened++;
                var grid = Field<DataGridView>(panel, "groupGrid");
                var release = Field<Button>(panel, "releaseButton");
                Assert.Equal(("Make Nests", false), (release.Text, release.Enabled));

                Assert.True(panel.AddSet(set));
                Assert.Equal(2, grid.Rows.Count);
                Assert.Contains("'SYN-OLD'", Field<Label>(panel, "previewLabel").Text);
                Assert.True(Field<CheckBox>(panel, "includeArchivedCheckBox").Visible);
                Assert.Equal("Synthetic customer", Field<Label>(panel, "customerValue").Text);
                var lines = Field<DataGridView>(panel, "lineGrid");
                Assert.Equal(new object[] { "Synthetic kit", "1", "kit" }, lines.Rows[0].Cells.Cast<DataGridViewCell>().Take(3).Select(c => c.Value));
                lines.Rows[0].Cells["lineQuantityColumn"].Value = "3";
                Assert.Equal("kits", lines.Rows[0].Cells["lineUnitColumn"].Value);
                Assert.Equal(new object[] { 12, 9 }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["partsColumn"].Value));
                Assert.Equal("Synthetic Aluminum Grade A 0.25 in, Laser", grid.Rows[0].Cells["groupColumn"].Value);
                Assert.Equal(("Make Nests (2)", true), (release.Text, release.Enabled));
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), r => Assert.Equal("→ new Draft nest, nested with Auto Nest.", Note(r)));
                Field<TextBox>(panel, "notesBox").Text = "Synthetic run";

                Run(panel.ReleaseAsync());

                Assert.Equal(2, nests.Uploaded.Count);
                Assert.Equal(2, opened.Count);
                Assert.Equal(1, nestsOpened);
                Assert.All(opened, o => Assert.Equal(NestStatus.Draft, o.Nest.Status));
                var steel = opened.Single(o => o.Nest.Material.Name == "Synthetic Steel");
                Assert.Equal(new[] { ("SYN-A", 6), ("SYN-B", 3) },
                    steel.Nest.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required)));

                var recorded = Assert.Single(sets.Releases);
                Assert.Equal((3, "Synthetic run", 2), (recorded.Sets.Single().Quantity, recorded.Notes, recorded.Nests.Count));
                Assert.Same(recorded, panel.Result!.Release);
                Assert.StartsWith("Done: 2 nests", Field<Label>(panel, "statusLabel").Text);
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), r => Assert.StartsWith("Created N26-", Note(r)));
                Assert.False(release.Enabled);
                Assert.True(lines.Columns["lineQuantityColumn"]!.ReadOnly);
                Assert.Null(panel.TakeDraft());

                // The next set starts the next release.
                Assert.True(panel.AddSet(set));
                Assert.Null(panel.Result);
                Assert.Equal(("Synthetic kit", 1), panel.Lines().Select(l => (l.Set.Name, l.Quantity)).Single());
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

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.AddSet(set);
                var refused = GroupRow(panel, "Synthetic Steel");

                Assert.Equal(false, refused.Cells["includeColumn"].Value);
                Assert.True(refused.Cells["includeColumn"].ReadOnly);
                Assert.Contains("named 'SYN-A'", Note(refused));
                GroupRow(panel, "Synthetic Copper").Cells["includeColumn"].Value = false;
                Assert.Equal("Left out of this release.", Note(GroupRow(panel, "Synthetic Copper")));
                Assert.Equal("Make Nests (1)", Field<Button>(panel, "releaseButton").Text);
                // Rebuilding the plan keeps the operator's choice.
                Field<DataGridView>(panel, "lineGrid").Rows[0].Cells["lineQuantityColumn"].Value = "2";
                Assert.Equal(false, GroupRow(panel, "Synthetic Copper").Cells["includeColumn"].Value);

                Run(panel.ReleaseAsync());

                Assert.Equal("Synthetic Aluminum", Assert.Single(Assert.Single(sets.Releases).Nests).MaterialName);
                Assert.Single(nests.Uploaded);
            },
            TestTimeout,
            "The release choice test did not complete.");
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

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.EditDrawing = edit =>
                {
                    edits.Add(edit.Listed.Name);
                    return Run(edit.SaveAsync(edit.Details with { MaterialName = "Synthetic Steel", Thickness = 0.25 }, edit.Listed.Notes, edit.Listed.Settings ?? new DrawingNestingSettings()));
                };
                Field<CheckBox>(panel, "nestAfterReleaseCheckBox").Checked = false;
                panel.AddSet(set);
                var grid = Field<DataGridView>(panel, "groupGrid");
                var fix = Field<Button>(panel, "fixDrawingsButton");
                var refused = grid.Rows.Cast<DataGridViewRow>().Single(r => ((SetReleaseGroup)r.Tag!).IsUnassigned);
                Assert.Contains("Edit Drawing", Note(refused));
                Assert.Equal("(no material or thickness)", refused.Cells["groupColumn"].Value);
                Assert.Contains("1 material can't be released yet: choose Fix Drawings.", Field<Label>(panel, "previewLabel").Text);
                // With a releasable group selected, Fix Drawings takes the refused one.
                grid.CurrentCell = grid.Rows[0].Cells["groupColumn"];
                Assert.NotSame(refused, grid.CurrentRow);
                Assert.True(fix.Visible && fix.Enabled);

                Run(panel.FixSelectedGroupAsync());

                Assert.Equal(new[] { "SYN-B", "SYN-C" }, edits);
                Assert.False(fix.Visible);
                var group = Assert.IsType<SetReleaseGroup>(Assert.Single(grid.Rows.Cast<DataGridViewRow>()).Tag);
                Assert.True(group.CanRelease);
                Assert.Equal(new[] { ("SYN-A", 1), ("SYN-B", 2), ("SYN-C", 2) }, group.Items.Select(i => (i.Drawing.Name, i.Drawing.Revision)));

                Run(panel.ReleaseAsync());

                Assert.Single(nests.Uploaded);
                Assert.Equal("Synthetic Steel", Assert.Single(Assert.Single(sets.Releases).Nests).MaterialName);
            },
            TestTimeout,
            "The fix drawings test did not complete.");
    }

    [Fact]
    public void ReleaseNotRecorded_CanBeRecordedAgain_AndIsNotClearedByANewLine()
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

                var (host, panel) = ShowPanel(sets, library, new MemoryNests(), (_, _) => opened++);
                using var _ = host;
                panel.AddSet(set);
                var release = Field<Button>(panel, "releaseButton");

                Run(panel.ReleaseAsync());

                Assert.Equal(1, opened);
                Assert.Empty(sets.Releases);
                Assert.Equal(("Try Again", true), (release.Text, release.Enabled));
                Assert.EndsWith("Synthetic release post failure. Try Again records it; the nests are not made again.",
                    Field<Label>(panel, "statusLabel").Text);
                Assert.False(panel.AddSet(set));
                Assert.NotNull(panel.Result);

                Run(panel.RetryRecordAsync());

                Assert.Equal(2, sets.PostAttempts);
                Assert.Same(Assert.Single(sets.Releases), panel.Result!.Release);
                Assert.Equal(("Make Nests (1)", false), (release.Text, release.Enabled));
            },
            TestTimeout,
            "The release retry test did not complete.");
    }

    [Fact]
    public void OneMatchingDraft_IsMergedIntoByDefault()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var c = library.Add("SYN-C", "Synthetic Aluminum");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1), (c, 1));
                var nests = new MemoryNests();
                var draft = nests.AddDraft("N26-0007", "Synthetic Steel", "Grade A", 0.25);

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.AddSet(set);
                var steel = GroupRow(panel, "Synthetic Steel");
                var aluminum = GroupRow(panel, "Synthetic Aluminum");

                Assert.Equal("N26-0007", steel.Cells["targetColumn"].Value);
                Assert.Equal("→ merges into N26-0007; the whole draft (1 sheet) is nested again.", Note(steel));
                Assert.Equal(ReleasePanel.NewNestChoice, aluminum.Cells["targetColumn"].Value);
                Assert.True(aluminum.Cells["targetColumn"].ReadOnly);
                Assert.Same(draft, panel.ChosenMerges().Values.Single());
                Assert.StartsWith("1 new, 1 merged, 2 parts.", Field<Label>(panel, "previewLabel").Text);

                Run(panel.ReleaseAsync());

                var release = Assert.Single(sets.Releases);
                var merged = release.Nests.Single(n => n.Kind == SetReleaseNestKind.Merged);
                Assert.Equal((draft.Id, (int?)draft.Revision, draft.Revision + 1), (merged.NestId, merged.RevisionBefore, merged.RevisionAfter));
                Assert.StartsWith("Merged into N26-0007", Note(steel));
            },
            TestTimeout,
            "The one-draft merge test did not complete.");
    }

    [Fact]
    public void SeveralMatchingDrafts_DefaultToTheNewest_AndTheChoiceIsInTheRow()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1));
                var nests = new MemoryNests();
                var older = nests.AddDraft("N26-0005", "Synthetic Steel", "Grade A", 0.25, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
                var newer = nests.AddDraft("N26-0009", "Synthetic Steel", "Grade A", 0.25, new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc));

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.AddSet(set);
                var row = Field<DataGridView>(panel, "groupGrid").Rows[0];
                var cell = (DataGridViewComboBoxCell)row.Cells["targetColumn"];
                var group = (SetReleaseGroup)row.Tag!;

                Assert.Equal(new object[] { "N26-0009", "N26-0005", ReleasePanel.NewNestChoice }, cell.Items.Cast<object>());
                Assert.False(cell.ReadOnly);
                Assert.Equal("N26-0009", cell.Value);
                Assert.Same(newer, panel.TargetOf(group));
                Assert.Contains("2 Draft nests match; the newest is chosen.", Note(row));

                panel.ChooseTarget(row, group, "N26-0005");
                Assert.Same(older, panel.TargetOf(group));
                Assert.DoesNotContain("the newest is chosen", Note(row));
                // The choice survives a rebuilt plan.
                Field<DataGridView>(panel, "lineGrid").Rows[0].Cells["lineQuantityColumn"].Value = "2";
                row = Field<DataGridView>(panel, "groupGrid").Rows[0];
                Assert.Equal("N26-0005", row.Cells["targetColumn"].Value);

                panel.ChooseTarget(row, (SetReleaseGroup)row.Tag!, ReleasePanel.NewNestChoice);
                Assert.Empty(panel.ChosenMerges());
                Assert.StartsWith("→ new Draft nest", Note(row));
                Assert.Contains("2 Draft nests match; choose one to merge.", Note(row));
            },
            TestTimeout,
            "The several-drafts merge test did not complete.");
    }

    [Fact]
    public void NoMatchingDraft_MakesANewDraftNest()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1));
                var nests = new MemoryNests();
                // Another material's draft does not match.
                nests.AddDraft("N26-0007", "Synthetic Aluminum", "Grade A", 0.25);

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.AddSet(set);
                var row = Field<DataGridView>(panel, "groupGrid").Rows[0];

                Assert.Equal(ReleasePanel.NewNestChoice, row.Cells["targetColumn"].Value);
                Assert.Equal("→ new Draft nest, nested with Auto Nest.", Note(row));

                Run(panel.ReleaseAsync());

                Assert.Equal(SetReleaseNestKind.Created, Assert.Single(Assert.Single(sets.Releases).Nests).Kind);
            },
            TestTimeout,
            "The no-draft release test did not complete.");
    }

    [Fact]
    public void DraftOpenInATab_IsNeverMergedInto_AndMakeNestsWaitsUntilItIsClosed()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, (a, 1));
                var nests = new MemoryNests();
                var draft = nests.AddDraft("N26-0007", "Synthetic Steel", "Grade A", 0.25);
                var openTabs = new HashSet<Guid> { draft.Id };
                var messages = new List<string>();

                var (host, panel) = ShowPanel(sets, library, nests, messages: messages);
                using var _ = host;
                panel.IsNestOpen = openTabs.Contains;
                panel.AddSet(set);
                var row = Field<DataGridView>(panel, "groupGrid").Rows[0];
                var release = Field<Button>(panel, "releaseButton");

                Assert.Equal("N26-0007", row.Cells["targetColumn"].Value);
                Assert.Equal("N26-0007 is open — save and close it to merge into it.", Note(row));
                Assert.False(release.Enabled);
                Assert.Contains("Make Nests waits: N26-0007 is open in a tab.", Field<Label>(panel, "previewLabel").Text);

                Run(panel.ReleaseAsync());
                Assert.Empty(nests.Uploaded);
                Assert.Empty(sets.Releases);
                Assert.Contains("is open in a tab", Assert.Single(messages));

                // Closed in its tab, as the window learns when it is activated again.
                openTabs.Clear();
                panel.RefreshOpenNests();
                Assert.True(release.Enabled);
                Assert.StartsWith("→ merges into N26-0007", Note(row));

                Run(panel.ReleaseAsync());
                Assert.Equal(SetReleaseNestKind.Merged, Assert.Single(Assert.Single(sets.Releases).Nests).Kind);
            },
            TestTimeout,
            "The open-draft release test did not complete.");
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

                var (host, panel) = ShowPanel(sets, library, new MemoryNests(), (nest, _) => opened.Add(nest));
                using var _ = host;
                panel.CreateNest = (group, _) =>
                {
                    var nest = new Nest("N26-0200") { Units = group.Units };
                    nest.PlateDefaults.Size = new OpenNest.Geometry.Size(48, 96);
                    return Task.FromResult(nest);
                };
                Field<CheckBox>(panel, "nestAfterReleaseCheckBox").Checked = true;
                panel.AddSet(set);

                Run(panel.ReleaseAsync());

                var nest = Assert.Single(opened);
                Assert.Equal(3, nest.Plates.Sum(p => p.Parts.Count * p.Quantity));
                Assert.Equal(0, Assert.Single(nest.Drawings).Quantity.Remaining);
                Assert.StartsWith("Created N26-0200 — Nested on 1 sheet, ", Note(Field<DataGridView>(panel, "groupGrid").Rows[0]));
            },
            TestTimeout,
            "The nest after release test did not complete.");
    }

    [Fact]
    public void NestAfterRelease_Unticked_SaysAMergedDraftIsLeftWithoutSheets()
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
                var renested = 0;

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                panel.Renest = (_, _) => { renested++; return Task.FromResult(new SetReleaseRenest(true, "")); };
                var nestAfter = Field<CheckBox>(panel, "nestAfterReleaseCheckBox");
                nestAfter.Checked = true;
                panel.AddSet(set);
                var row = Field<DataGridView>(panel, "groupGrid").Rows[0];
                Assert.Contains("the whole draft (1 sheet) is nested again", Note(row));

                nestAfter.Checked = false;
                Assert.Equal("→ merges into N26-0007; its 1 sheet is cleared, leaving the draft without sheets until it is nested.", Note(row));

                Run(panel.ReleaseAsync());

                Assert.Equal(0, renested);
                var merged = Assert.Single(panel.Result!.Created);
                Assert.Same(draft, merged.MergedInto);
                Assert.Empty(merged.Nest!.Plates);
                Assert.Null(merged.Renest);
                Assert.False(nestAfter.Enabled);
            },
            TestTimeout,
            "The release without nesting test did not complete.");
    }

    [Fact]
    public void NestAfterRelease_RemembersTheOperatorsLastChoice()
    {
        StaTestThread.Run(
            () =>
            {
                var settings = OpenNest.Properties.Settings.Default;
                var saved = settings.ReleaseNestAfter;
                try
                {
                    settings.ReleaseNestAfter = true;
                    using (var first = new ReleasePanel())
                    {
                        var box = Field<CheckBox>(first, "nestAfterReleaseCheckBox");
                        Assert.True(box.Checked);
                        // Set by code, it is not remembered; clicked by the operator, it is.
                        box.Checked = false;
                        Assert.True(settings.ReleaseNestAfter);
                        box.Checked = true;
                        typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(box, new object[] { EventArgs.Empty });
                        Assert.False(box.Checked);
                        Assert.False(settings.ReleaseNestAfter);
                    }

                    using var second = new ReleasePanel();
                    Assert.False(Field<CheckBox>(second, "nestAfterReleaseCheckBox").Checked);
                }
                finally
                {
                    settings.ReleaseNestAfter = saved;
                    settings.Save();
                }
            },
            TestTimeout,
            "The remembered nest-after test did not complete.");
    }

    [Fact]
    public void SetsTab_DoubleClickAddsASetAtOneKit_AgainAddsAKit_AndAddToReleaseTakesEverySelectedSet()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                CreateSet(sets, "Synthetic kit", (a, 1));
                CreateSet(sets, "Synthetic frame", (a, 2));
                var edits = 0;

                using var form = new DrawingSetsForm(sets, library, Server);
                form.EnableRelease(new MemoryNests(), (_, _) => { });
                var browser = form.Browser;
                browser.EditSet = _ => { edits++; return null; };
                form.Show();
                var setGrid = Field<DataGridView>(browser, "setGrid");
                PumpUntil(() => setGrid.Rows.Count == 2, "the set list");
                var panel = form.ReleasePanel;
                Assert.True(panel.Visible);
                Assert.Equal("&Add to Release", Field<Button>(browser, "releaseButton").Text);

                // Listed by name: frame, then kit.
                setGrid.CurrentCell = setGrid.Rows[1].Cells[0];
                DoubleClick(setGrid, 1);
                PumpUntil(() => panel.Lines().Count == 1, "the set to be added");
                Assert.Equal(("Synthetic kit", 1), (panel.Lines()[0].Set.Name, panel.Lines()[0].Quantity));
                Assert.Equal("Synthetic Steel", Assert.Single(panel.Lines()[0].Set.Members).Drawing!.MaterialName);

                DoubleClick(setGrid, 1);
                PumpUntil(() => panel.Lines()[0].Quantity == 2, "the kit to be added");
                Assert.Single(panel.Lines());
                Assert.Equal(0, edits);

                setGrid.Rows[0].Selected = true;
                setGrid.Rows[1].Selected = true;
                Run(browser.AddSelectedToReleaseAsync());
                Assert.Equal(new[] { ("Synthetic kit", 3), ("Synthetic frame", 1) }, panel.Lines().Select(l => (l.Set.Name, l.Quantity)));

                // Edit moved to its button and the right-click menu.
                Assert.True(Field<ToolStripMenuItem>(browser, "editMenuItem").Available);
                Run(browser.EditSelectedAsync());
                Assert.Equal(1, edits);

                // The panel shows beside the lists, never over the releases.
                Field<TabControl>(form, "tabs").SelectedIndex = 2;
                Assert.True(Field<SplitContainer>(form, "splitContainer").Panel2Collapsed);
                form.ShowLibrary();
                Assert.False(Field<SplitContainer>(form, "splitContainer").Panel2Collapsed);
            },
            TestTimeout,
            "The sets tab add test did not complete.");
    }

    [Fact]
    public void LibraryTab_DoubleClickAddsADrawingOnItsOwnAtTheQuantity_AgainAddsThatMore_IntoTheSameRelease()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var set = CreateSet(sets, "Synthetic kit", (a, 1));
                var edits = 0;

                using var form = new DrawingSetsForm(sets, library, Server);
                form.EnableRelease(new MemoryNests(), (_, _) => { });
                var view = form.LibraryView;
                view.EditDrawing = _ => { edits++; return null; };
                view.LoadPreview = (_, _) => Task.FromResult<Drawing>(null!);
                form.ShowLibrary();
                form.Show();
                var grid = Field<DataGridView>(view, "drawingGrid");
                PumpUntil(() => grid.Rows.Count == 1, "the library page");
                grid.CurrentCell = grid.Rows[0].Cells[0];
                var panel = form.ReleasePanel;
                Assert.True(panel.AddSet(set));

                Field<NumericUpDown>(view, "quantityBox").Value = 4;
                DoubleClick(grid, 0);
                PumpUntil(() => panel.DrawingLines().Count == 1, "the drawing to be added");
                Assert.Equal(("SYN-A", 4), (panel.DrawingLines()[0].Drawing.Name, panel.DrawingLines()[0].Quantity));

                DoubleClick(grid, 0);
                PumpUntil(() => panel.DrawingLines()[0].Quantity == 8, "the quantity to be added");
                Assert.Equal(0, edits);
                var lines = Field<DataGridView>(panel, "lineGrid");
                Assert.Equal(new object[] { "Synthetic kit", "SYN-A" }, lines.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["lineNameColumn"].Value));
                Assert.Equal("pcs", lines.Rows[1].Cells["lineUnitColumn"].Value);
                // The set and the drawing on its own share one group.
                Assert.Equal(9, Field<DataGridView>(panel, "groupGrid").Rows[0].Cells["partsColumn"].Value);

                var addButton = Field<Button>(view, "addButton");
                Assert.Equal(("Add to Release", true, true), (addButton.Text, addButton.Visible, addButton.Enabled));
                Assert.Equal("Edit Drawing...", Field<Button>(view, "editButton").Text);
                Run(view.EditSelectedAsync());
                Assert.Equal(1, edits);

                // ✕ removes a line.
                typeof(DataGridView).GetMethod("OnCellContentClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(lines, new object[] { new DataGridViewCellEventArgs(lines.Columns["lineRemoveColumn"]!.Index, 0) });
                Assert.Empty(panel.Lines());
                Assert.Single(panel.DrawingLines());

                // Closing keeps the release for the next time the window opens.
                var draft = form.TakeRelease();
                Assert.NotNull(draft);
                using var again = new DrawingSetsForm(sets, library, Server);
                again.EnableRelease(new MemoryNests(), (_, _) => { });
                again.RestoreRelease(draft);
                Assert.Equal(("SYN-A", 8), again.ReleasePanel.DrawingLines().Select(l => (l.Drawing.Name, l.Quantity)).Single());
            },
            TestTimeout,
            "The library tab add test did not complete.");
    }

    [Fact]
    public void ReleaseDialog_OfASetAndDrawingsAddedFromTheLibrary_GroupsThemTogether_AndRecordsBoth()
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

                using var form = new ReleaseSetForm(sets, library, nests, Server, new[] { set }, Array.Empty<SetReleaseDrawingLine>(), (_, _) => { });
                var panel = form.Panel;
                panel.ShowMessage = (_, _, _) => DialogResult.OK;
                var created = 0;
                panel.CreateNest = (group, _) => Task.FromResult(new Nest($"N26-{++created:D4}") { Units = group.Units });
                form.Show();
                using var picker = new AddReleaseDrawingsForm(library, Server, panel.AddDrawings,
                    id => panel.DrawingLines().Any(l => l.Drawing.Id == id));
                picker.Show(form);
                Run(picker.RefreshAsync());

                picker.Select("SYN-A", "SYN-C");
                picker.Quantity = 5;
                picker.AddSelectedDrawings();
                Assert.Equal("Added 2 drawings.", picker.Status);
                picker.Select("SYN-C", "SYN-B");
                picker.Quantity = 1;
                picker.AddSelectedDrawings();
                Assert.Equal("Added 1 drawing and 1 more of 1 listed.", picker.Status);

                var lines = Field<DataGridView>(panel, "lineGrid");
                Assert.Equal(new object[] { "kit", "pcs", "pcs", "pc" },
                    lines.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["lineUnitColumn"].Value));
                lines.Rows[2].Cells["lineQuantityColumn"].Value = "40";
                Assert.Equal(new[] { ("SYN-A", 5), ("SYN-C", 40), ("SYN-B", 1) }, panel.DrawingLines().Select(l => (l.Drawing.Name, l.Quantity)));
                var grid = Field<DataGridView>(panel, "groupGrid");
                Assert.Equal(new object[] { 40, 8 }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["partsColumn"].Value));

                Run(panel.ReleaseAsync());

                var release = Assert.Single(sets.Releases);
                Assert.Equal(new[] { 5, 40, 1 }, release.Drawings.Select(d => d.Quantity));
                var steel = release.Nests.Single(n => n.MaterialName == "Synthetic Steel");
                Assert.Equal(new[] { ((Guid?)set.Id, 2), (null, 5), (null, 1) }, steel.Items.Select(i => (i.SetId, i.Quantity)));
                Assert.Equal(2, nests.Uploaded.Count);
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

                var (host, panel) = ShowPanel(sets, library, nests);
                using var _ = host;
                Assert.Equal("Added 'SYN-A' ×4.", panel.AddDrawings(new[] { a }, 4));
                Assert.Equal("Release drawing 'SYN-A' ×4", Field<Label>(panel, "headingLabel").Text);
                Assert.Contains("4 parts", Field<Label>(panel, "previewLabel").Text);

                Run(panel.ReleaseAsync());

                Assert.Empty(nests.Uploaded);
                Assert.Empty(sets.Releases);
                Assert.Equal("Nothing saved. See What happens.", Field<Label>(panel, "statusLabel").Text);
                Assert.Contains("does not support releasing library drawings on their own",
                    Note(Field<DataGridView>(panel, "groupGrid").Rows[0]));
            },
            TestTimeout,
            "The old-server drawing release test did not complete.");
    }

    [Fact]
    public void NewRelease_SwitchesToTheSetsTab_AndTheReleasesTabRereadsWhenShownAgain()
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

                using var form = new DrawingSetsForm(sets, library, Server);
                var view = form.Releases;
                Run(form.ShowReleasesAsync());
                form.Show();
                var releases = Field<DataGridView>(view, "releaseGrid");
                PumpUntil(() => releases.Rows.Count == 2, "the releases");

                Assert.False(Field<TableLayoutPanel>(view, "filterTable").Visible);
                Assert.Equal("Released", releases.Columns["multiplierColumn"]!.HeaderText);
                Assert.Equal(new object[] { "SYN-A ×1", "Synthetic kit ×2, Synthetic frame; SYN-A ×4" },
                    releases.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["multiplierColumn"].Value));
                var newRelease = Field<Button>(view, "newReleaseButton");
                Assert.False(newRelease.Visible);

                form.EnableRelease(new MemoryNests(), (_, _) => { });
                Assert.True(newRelease.Visible);
                newRelease.PerformClick();
                Assert.False(form.ShowsReleases);
                Assert.True(form.ReleasePanel.Visible);

                // A release made meanwhile shows once the tab is shown again.
                sets.Releases.Add(Release(DateTime.UtcNow, new[] { new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name } },
                    Array.Empty<SetReleaseDrawing>()));
                Run(form.ShowReleasesAsync());
                PumpUntil(() => releases.Rows.Count == 3, "the re-read releases");
                Assert.Equal("Synthetic kit", releases.Rows[0].Cells["multiplierColumn"].Value);
            },
            TestTimeout,
            "The new release test did not complete.");
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

                using var form = new DrawingSetsForm(sets, new PackageLibrary(), Server);
                Run(form.ShowReleasesAsync(set));
                form.Show();
                var view = form.Releases;
                var releases = Field<DataGridView>(view, "releaseGrid");
                var nests = Field<DataGridView>(view, "nestGrid");
                PumpUntil(() => releases.Rows.Count == 1 && nests.Rows.Count == 3, "the releases");

                Assert.True(form.ShowsReleases);
                Assert.True(Field<TableLayoutPanel>(view, "filterTable").Visible);
                Assert.Equal("Releases of set 'Synthetic kit'", Field<Label>(view, "filterLabel").Text);
                Assert.True(releases.Columns["revisionColumn"]!.Visible);
                Assert.Equal("1 of 2 cut, 1 deleted", releases.Rows[0].Cells["progressColumn"].Value);
                Assert.Equal("×2", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.Equal(new object[] { "Has Been Cut", "To Be Cut", "Deleted" },
                    nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["statusColumn"].Value));
            },
            TestTimeout,
            "The releases view test did not complete.");
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

                using var form = new DrawingSetsForm(sets, new PackageLibrary(), Server);
                var view = form.Releases;
                view.ShowMessage = (text, buttons, _) =>
                {
                    questions.Add(text);
                    return buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK;
                };
                Run(form.ShowReleasesAsync(set));
                form.Show();
                var releases = Field<DataGridView>(view, "releaseGrid");
                var nests = Field<DataGridView>(view, "nestGrid");
                PumpUntil(() => releases.Rows.Count == 1 && nests.Rows.Count == 2, "the releases");
                Assert.Equal("×2 (+1 other set)", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.Equal(new object[] { "Created", "Merged (revision 3 → 4)" }, nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["changeColumn"].Value));
                Assert.True(Field<Button>(view, "undoButton").Enabled);

                Run(view.UndoSelectedAsync());

                Assert.Contains("1 nest it created is deleted", questions[0]);
                Assert.Contains("1 draft it merged into goes back", questions[0]);
                Assert.Contains("- N2: saved since the release (now revision 6, To Be Cut)", questions[1]);
                Assert.Equal(new[] { 0, 1 }, sets.UndoRequests.Select(r => r.Request.SkipNests.Count));
                Assert.Equal(merged.NestId, sets.UndoRequests[1].Request.SkipNests.Single());
                PumpUntil(() => (string)releases.Rows[0].Cells["progressColumn"].Value == "Undone", "the release to read as undone");
                Assert.Equal(new object[] { "Created; undo deleted it", "Merged (revision 3 → 4); undo left it as it was" },
                    nests.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["changeColumn"].Value));
                Assert.False(Field<Button>(view, "undoButton").Enabled);
                Assert.Same(set, view.FilterSet);
            },
            TestTimeout,
            "The release undo test did not complete.");
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

                using var form = new DrawingSetsForm(sets, library, Server);
                var view = form.Releases;
                view.ShowMessage = (text, _, _) => { questions.Add(text); return DialogResult.OK; };
                Run(form.ShowReleasesAsync(drawing: a));
                form.Show();
                var releases = Field<DataGridView>(view, "releaseGrid");
                PumpUntil(() => releases.Rows.Count == 1, "the releases");

                Assert.Equal("Releases of drawing 'SYN-A' on its own", Field<Label>(view, "filterLabel").Text);
                Assert.Equal("Quantity", releases.Columns["multiplierColumn"]!.HeaderText);
                Assert.Equal("×4 (+1 set)", releases.Rows[0].Cells["multiplierColumn"].Value);
                Assert.False(releases.Columns["revisionColumn"]!.Visible);

                Run(view.UndoSelectedAsync());

                Assert.Equal(release.Id, Assert.Single(sets.UndoRequests).Id);
                Assert.Contains("deleted (they can be recovered)", Assert.Single(questions));
            },
            TestTimeout,
            "The drawing releases view test did not complete.");
    }

    [Fact]
    public void DrawingSetsWindow_ReadsEachTabWhenShown_AndASetsReleasesFilterTheReleasesTab_UntilCleared()
    {
        StaTestThread.Run(
            () =>
            {
                var library = new PackageLibrary();
                var a = library.Add("SYN-A", "Synthetic Steel");
                var sets = new InMemoryDrawingSets();
                var kit = CreateSet(sets, "Synthetic kit", (a, 1));
                var frame = CreateSet(sets, "Synthetic frame", (a, 2));
                SetReleaseRecord Release(DrawingSetRecord set) => new()
                {
                    Id = Guid.NewGuid(),
                    Sets = new[] { new SetReleaseLine { SetId = set.Id, SetRevision = 1, SetName = set.Name, Quantity = 1 } },
                    ReleasedBy = "Synthetic operator",
                    Nests = new[] { new SetReleaseNest { NestId = Guid.NewGuid(), Nest = new NestRecord { Name = "N1", Status = NestStatus.Draft } } },
                };
                sets.Releases.Add(Release(kit));
                sets.Releases.Add(Release(frame));

                using var form = new DrawingSetsForm(sets, library, Server);
                var browser = form.Browser;
                var view = form.Releases;
                form.Show();
                var setGrid = Field<DataGridView>(browser, "setGrid");
                var releases = Field<DataGridView>(view, "releaseGrid");
                var filter = Field<TableLayoutPanel>(view, "filterTable");
                PumpUntil(() => setGrid.Rows.Count == 2, "the set list");

                Assert.False(form.ShowsReleases);
                Assert.False(view.HasLoaded);

                setGrid.CurrentCell = setGrid.Rows.Cast<DataGridViewRow>()
                    .Single(r => (string)r.Cells["nameColumn"].Value == "Synthetic kit").Cells[0];
                Assert.True(Field<Button>(browser, "moreButton").Enabled);
                Field<ToolStripMenuItem>(browser, "releasesMenuItem").PerformClick();
                PumpUntil(() => releases.Rows.Count == 1, "the set's releases");

                Assert.True(form.ShowsReleases);
                Assert.Equal(kit.Id, view.FilterSet.Id);
                Assert.True(filter.Visible);
                Assert.Equal("Releases of set 'Synthetic kit'", Field<Label>(view, "filterLabel").Text);
                Assert.Equal("Quantity", releases.Columns["multiplierColumn"]!.HeaderText);

                Field<Button>(view, "clearFilterButton").PerformClick();
                PumpUntil(() => releases.Rows.Count == 2, "every release");

                Assert.Null(view.FilterSet);
                Assert.False(filter.Visible);
                Assert.Equal("Released", releases.Columns["multiplierColumn"]!.HeaderText);
                Assert.False(releases.Columns["revisionColumn"]!.Visible);

                // A release made meanwhile (as Make Nests on the Sets tab does) shows once the tab is shown again.
                form.ShowSets();
                Assert.False(form.ShowsReleases);
                sets.Releases.Add(Release(frame));
                Field<TabControl>(form, "tabs").SelectedIndex = 2;
                PumpUntil(() => releases.Rows.Count == 3, "the re-read releases");
                Assert.Equal("Releases 1-3 of 3", Field<Label>(view, "summaryLabel").Text);
            },
            TestTimeout,
            "The drawing sets window tabs test did not complete.");
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

    /// <summary>A release panel in a shown window, initialized, with test hooks for messages and new nests.</summary>
    private static (Form Host, ReleasePanel Panel) ShowPanel(InMemoryDrawingSets sets, PackageLibrary library, MemoryNests nests,
        Action<Nest, NestRecord>? openNest = null, List<string>? messages = null)
    {
        var host = new Form { Width = 900, Height = 700 };
        var panel = new ReleasePanel { Dock = DockStyle.Fill };
        host.Controls.Add(panel);
        panel.Initialize(sets, library, nests, Server, openNest ?? ((_, _) => { }));
        panel.ShowMessage = (text, _, _) => { messages?.Add(text); return DialogResult.OK; };
        var created = 0;
        panel.CreateNest = (group, _) => Task.FromResult(new Nest($"N26-{++created:D4}") { Units = group.Units });
        // As by default, whatever this PC last chose.
        Field<CheckBox>(panel, "nestAfterReleaseCheckBox").Checked = true;
        host.Show();
        return (host, panel);
    }

    private static DataGridViewRow GroupRow(ReleasePanel panel, string material) =>
        Field<DataGridView>(panel, "groupGrid").Rows.Cast<DataGridViewRow>()
            .Single(r => ((SetReleaseGroup)r.Tag!).MaterialName == material);

    private static string Note(DataGridViewRow row) => (string)row.Cells["statusColumn"].Value;

    private static void DoubleClick(DataGridView grid, int row) =>
        typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, new object[] { new DataGridViewCellEventArgs(0, row) });

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
        public NestRecord AddDraft(string name, string material, string grade, double thickness, DateTime? savedAt = null)
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
                SavedAt = savedAt ?? new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
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
