using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

public class SaveToDrawingLibraryFormTests
{
    private const string Server = "http://library.test:8090";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void NoMatch_PrefillsTheDetails_DefaultsToASeparateDrawing_AndSavesAndLinks()
    {
        StaTestThread.Run(
            () =>
            {
                var store = new FakeStore();
                var (nest, drawing) = MatchingNest();

                var form = new SaveToDrawingLibraryForm(store, Server, nest, drawing, CutType.Waterjet, "Synthetic operator");
                form.ShowMessage = (text, _, _) => throw new Xunit.Sdk.XunitException("Unexpected message: " + text);
                form.Show();
                var saveAs = Field<ComboBox>(form, "saveAsBox");
                var save = Field<Button>(form, "saveButton");

                Assert.Equal("SYN-101", Field<TextBox>(form, "nameBox").Text);
                Assert.Equal("Synthetic Steel", Field<ComboBox>(form, "materialBox").Text);
                Assert.Equal("Grade A", Field<TextBox>(form, "gradeBox").Text);
                Assert.Equal("1/4\" - 0.25", Field<OpenNest.Controls.ThicknessComboBox>(form, "thicknessBox").Text);
                Assert.Equal(CutType.Waterjet, Field<ComboBox>(form, "cutTypeBox").SelectedItem);
                PumpUntil(() => save.Enabled, "the duplicate check");
                Assert.Equal("Create a separate library drawing", saveAs.Text);

                Run(form.SaveAsync());

                Assert.Equal(DialogResult.OK, form.DialogResult);
                Assert.Equal(DrawingLibrarySaveOutcome.Created, form.Result.Outcome);
                var stored = Assert.Single(store.Records);
                Assert.Equal((CutType.Waterjet, "Synthetic operator"), (stored.CutType, stored.SavedBy));
                Assert.Equal((Server, stored.Id, 1), (drawing.Library!.ServerKey, drawing.Library.Id, drawing.Library.Revision));
                form.Dispose();
            },
            TestTimeout,
            "The save to drawing library test did not complete.");
    }

    [Fact]
    public void OneMatch_DefaultsToItsNextRevision_AndEditedDetailsMustBeCheckedAgain()
    {
        StaTestThread.Run(
            () =>
            {
                var store = new FakeStore();
                var match = store.Seed("SYN-101", 0.25);
                var (nest, drawing) = MatchingNest();

                using var form = new SaveToDrawingLibraryForm(store, Server, nest, drawing, CutType.Laser, "");
                form.Show();
                var saveAs = Field<ComboBox>(form, "saveAsBox");
                var save = Field<Button>(form, "saveButton");
                var thickness = Field<OpenNest.Controls.ThicknessComboBox>(form, "thicknessBox");

                PumpUntil(() => save.Enabled, "the duplicate check");
                Assert.StartsWith("Revision 2 of 'SYN-101'", saveAs.Text);

                thickness.Value = 0.1875;
                Assert.False(save.Enabled);
                Assert.Equal(-1, saveAs.SelectedIndex);
                PumpUntil(() => save.Enabled, "the second duplicate check");
                Assert.Equal("Create a separate library drawing", saveAs.Text);

                thickness.Value = 0.25;
                PumpUntil(() => save.Enabled, "the third duplicate check");
                Run(form.SaveAsync());

                Assert.Equal(DrawingLibrarySaveOutcome.Revised, form.Result.Outcome);
                Assert.Equal((match.Id, 2), (drawing.Library!.Id, drawing.Library.Revision));
            },
            TestTimeout,
            "The drawing library revision test did not complete.");
    }

    [Fact]
    public void SeveralMatches_LeaveSaveDisabledUntilOneIsChosen()
    {
        StaTestThread.Run(
            () =>
            {
                var store = new FakeStore();
                store.Seed("SYN-101", 0.25);
                store.Seed("SYN-101", 0.25);
                var (nest, drawing) = MatchingNest();

                using var form = new SaveToDrawingLibraryForm(store, Server, nest, drawing, CutType.Laser, "");
                form.Show();
                var saveAs = Field<ComboBox>(form, "saveAsBox");
                var save = Field<Button>(form, "saveButton");

                PumpUntil(() => saveAs.Items.Count == 3, "the duplicate check");
                Assert.Equal(-1, saveAs.SelectedIndex);
                Assert.False(save.Enabled);
                Assert.Contains("Choose one", Field<Label>(form, "duplicateLabel").Text);

                saveAs.SelectedIndex = 2;

                Assert.True(save.Enabled);
            },
            TestTimeout,
            "The drawing library choice test did not complete.");
    }

    [Fact]
    public void Conflict_IsShown_TheDialogStaysOpen_AndTheChoicesAreRefreshed()
    {
        StaTestThread.Run(
            () =>
            {
                var store = new FakeStore();
                var match = store.Seed("SYN-101", 0.25);
                var (nest, drawing) = MatchingNest();
                var messages = new List<string>();

                using var form = new SaveToDrawingLibraryForm(store, Server, nest, drawing, CutType.Laser, "");
                form.ShowMessage = (text, _, _) => { messages.Add(text); return DialogResult.OK; };
                form.Show();
                var saveAs = Field<ComboBox>(form, "saveAsBox");
                var save = Field<Button>(form, "saveButton");
                PumpUntil(() => save.Enabled, "the duplicate check");
                store.Revise(match.Id);

                Run(form.SaveAsync());

                Assert.Contains("changed by someone else", Assert.Single(messages));
                Assert.Null(form.Result);
                Assert.Null(drawing.Library);
                Assert.True(form.Visible);
                PumpUntil(() => save.Enabled, "the refreshed duplicate check");
                Assert.StartsWith("Revision 3 of 'SYN-101'", saveAs.Text);
            },
            TestTimeout,
            "The drawing library conflict test did not complete.");
    }

    private static (Nest Nest, Drawing Drawing) MatchingNest()
    {
        var nest = new Nest("Synthetic job")
        {
            Units = Units.Inches,
            Thickness = 0.25,
            Material = new Material("Synthetic Steel", "Grade A"),
        };
        var drawing = new RectangleShape { Name = "SYN-101", Length = 4, Width = 2 }.GetDrawing();
        drawing.Customer = "Synthetic customer";
        nest.Drawings.Add(drawing);
        return (nest, drawing);
    }

    private static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the save to finish");

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

    /// <summary>An in-memory library that keeps every revision and filters like the server.</summary>
    private sealed class FakeStore : IDrawingLibraryStore
    {
        private readonly Dictionary<Guid, List<DrawingLibraryRecord>> _revisions = new();
        private DateTime _clock = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        public IEnumerable<DrawingLibraryRecord> Records => _revisions.Values.Select(r => r[^1]);

        public DrawingLibraryRecord Seed(string name, double thickness)
        {
            var id = Guid.NewGuid();
            _revisions[id] = new List<DrawingLibraryRecord>
            {
                Stored(new DrawingLibraryRecord
                {
                    Name = name,
                    Customer = "Synthetic customer",
                    MaterialName = "Synthetic Steel",
                    MaterialGrade = "Grade A",
                    Thickness = thickness,
                    Units = Units.Inches,
                    CutType = CutType.Laser,
                }, id, 1, new byte[] { (byte)'P', (byte)'K', 1, 2 }),
            };
            return _revisions[id][^1];
        }

        public void Revise(Guid id)
        {
            var current = _revisions[id][^1];
            _revisions[id].Add(Stored(current, id, current.Revision + 1, new byte[] { (byte)'P', (byte)'K', 9, (byte)current.Revision }));
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            var items = Records
                .Where(r => r.Name.Contains(query.NormalizedSearch, StringComparison.OrdinalIgnoreCase))
                .Where(r => query.Thickness is not { } t || r.Thickness == DrawingLibraryRules.NormalizeThickness(t))
                .Where(r => query.CutType is not { } c || r.CutType == c)
                .OrderByDescending(r => r.SavedAt)
                .ToList();
            return Task.FromResult(new DrawingLibraryPage
            { Items = items.Skip(query.Offset).Take(query.Limit).ToList(), Total = items.Count, Offset = query.Offset, Limit = query.Limit });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revisions.TryGetValue(id, out var list) ? list[^1] : null);

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default) =>
            Task.FromResult<DrawingLibraryPage?>(_revisions.TryGetValue(id, out var list)
                ? new DrawingLibraryPage { Items = Enumerable.Reverse(list).Skip(offset).Take(limit).ToList(), Total = list.Count, Offset = offset, Limit = limit }
                : null);

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default)
        {
            _revisions[record.Id] = new List<DrawingLibraryRecord> { Stored(record, record.Id, 1, package) };
            return Task.FromResult(_revisions[record.Id][^1]);
        }

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default)
        {
            var current = _revisions[id][^1];
            if (current.Revision != expectedRevision)
                throw new DrawingLibraryConflictException(current);
            _revisions[id].Add(Stored(record, id, current.Revision + 1, package));
            return Task.FromResult(_revisions[id][^1]);
        }

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private DrawingLibraryRecord Stored(DrawingLibraryRecord metadata, Guid id, int revision, byte[] package)
        {
            _clock = _clock.AddMinutes(1);
            return new DrawingLibraryRecord
            {
                Id = id,
                Revision = revision,
                Name = metadata.Name,
                Customer = metadata.Customer,
                MaterialName = metadata.MaterialName,
                MaterialGrade = metadata.MaterialGrade,
                Thickness = DrawingLibraryRules.NormalizeThickness(metadata.Thickness),
                Units = metadata.Units,
                CutType = metadata.CutType,
                Notes = metadata.Notes,
                Settings = metadata.Settings,
                SavedBy = metadata.SavedBy,
                PackageSha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(),
                SavedAt = _clock,
            };
        }
    }
}
