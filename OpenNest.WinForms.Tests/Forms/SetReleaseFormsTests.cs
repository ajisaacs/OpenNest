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
                Field<NumericUpDown>(form, "multiplierBox").Value = 3;
                Assert.Equal(new object[] { 12, 9 }, grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["partsColumn"].Value));
                Field<TextBox>(form, "notesBox").Text = "Synthetic run";

                Run(form.ReleaseAsync());

                Assert.Contains("Create 2 Draft nests from set 'Synthetic kit' ×3", Assert.Single(questions));
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
                Assert.StartsWith("Released: 2 nests", Field<Label>(form, "statusLabel").Text);
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), r => Assert.StartsWith("Created N26-", (string)r.Cells["statusColumn"].Value));
                Assert.False(Field<Button>(form, "releaseButton").Enabled);
                Assert.False(Field<NumericUpDown>(form, "multiplierBox").Enabled);
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
                Assert.Contains("1 of 3 groups chosen", Field<Label>(form, "summaryLabel").Text);
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
                Assert.Equal(("Record Release", true), (release.Text, release.Enabled));
                Assert.Contains("Synthetic release post failure.", Field<Label>(form, "statusLabel").Text);

                Run(form.RetryRecordAsync());

                Assert.Equal(2, sets.PostAttempts);
                Assert.Same(Assert.Single(sets.Releases), form.Result!.Release);
                Assert.Equal(("Release", false), (release.Text, release.Enabled));
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

    private static DrawingSetRecord CreateSet(InMemoryDrawingSets sets, params (DrawingLibraryRecord Drawing, int Quantity)[] members)
    {
        var task = sets.CreateAsync(new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Name = "Synthetic kit",
            Customer = "Synthetic customer",
            Members = members.Select(m => new DrawingSetMember { DrawingId = m.Drawing.Id, Quantity = m.Quantity, Drawing = m.Drawing }).ToList(),
        });
        return task.GetAwaiter().GetResult();
    }

    private static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the operation to finish");

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

        public DrawingLibraryRecord Add(string name, string material, bool archived = false)
        {
            var package = DrawingPackage.Write(new RectangleShape { Name = name, Length = 4, Width = 2 }.GetDrawing(), Units.Inches);
            var record = new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Revision = 1,
                Name = name,
                MaterialName = material,
                MaterialGrade = "Grade A",
                Thickness = 0.25,
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
            throw new NotSupportedException();

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Stores uploads in memory under their client-allocated ids.</summary>
    private sealed class MemoryNests : INestRepository
    {
        public List<NestRecord> Uploaded { get; } = new();

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
        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
