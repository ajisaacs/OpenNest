using System.Security.Cryptography;
using OpenNest.Data;
using OpenNest.Shapes;

namespace OpenNest.Tests.Data;

public class DrawingLibrarySaveSessionTests
{
    private const string Server = "http://library.test:8090";
    private static readonly byte[] PackageA = { (byte)'P', (byte)'K', 3, 4, 1 };
    private static readonly byte[] PackageB = { (byte)'P', (byte)'K', 3, 4, 2 };

    private static Nest MatchingNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Customer = "Nest customer",
        Material = new Material("Synthetic Steel", "Grade A"),
    };

    private static Drawing SyntheticDrawing(string name = "SYN-101")
    {
        var drawing = new RectangleShape { Name = name, Length = 4, Width = 2 }.GetDrawing();
        drawing.Customer = "Synthetic customer";
        drawing.Material = new Material("Drawing material", "Drawing grade");
        return drawing;
    }

    private static DrawingLibraryRecord Metadata(string name = "SYN-101") => new()
    {
        Name = name,
        Customer = "Synthetic customer",
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        Units = Units.Inches,
        CutType = CutType.Laser,
        Notes = "Deburr",
        SavedBy = "Synthetic operator",
    };

    private static DrawingLibrarySaveSession Session(FakeStore store, Drawing? drawing = null, Nest? nest = null) =>
        new(store, Server + "/", nest ?? MatchingNest(), drawing ?? SyntheticDrawing());

    [Fact]
    public void Prefill_TakesNameAndCustomerFromTheDrawing_AndMaterialThicknessUnitsFromTheNest()
    {
        var nest = MatchingNest();
        nest.Thickness = 0.250004;
        var session = Session(new FakeStore(), nest: nest);

        var metadata = session.Prefill(CutType.Waterjet, "Synthetic operator");

        Assert.Equal(("SYN-101", "Synthetic customer"), (metadata.Name, metadata.Customer));
        Assert.Equal(("Synthetic Steel", "Grade A"), (metadata.MaterialName, metadata.MaterialGrade));
        Assert.Equal((0.25, Units.Inches), (metadata.Thickness, metadata.Units));
        Assert.Equal((CutType.Waterjet, "", "Synthetic operator"), (metadata.CutType, metadata.Notes, metadata.SavedBy));
        Assert.Null(DrawingLibraryRules.GetMetadataError(metadata));
    }

    [Fact]
    public void Prefill_TakesTheDrawingsNestingSettings()
    {
        var drawing = SyntheticDrawing();
        drawing.CutFirst = true;
        drawing.Priority = 3;
        drawing.Constraints = new NestConstraints { StepAngle = 0.5, StartAngle = 0, EndAngle = System.Math.PI, Allow180Equivalent = true };
        drawing.Color = System.Drawing.Color.FromArgb(255, 10, 20, 30);
        drawing.HasCustomColor = true;

        var metadata = Session(new FakeStore(), drawing).Prefill(CutType.Laser, "");

        Assert.Equal(new DrawingNestingSettings
        {
            CutFirst = true,
            Priority = 3,
            StepAngle = 0.5,
            EndAngle = System.Math.PI,
            Allow180Equivalent = true,
            ColorArgb = System.Drawing.Color.FromArgb(255, 10, 20, 30).ToArgb(),
        }, metadata.Settings);
    }

    [Fact]
    public async Task SaveAsync_SettingsOnlyChange_RevisesWithTheSamePackage()
    {
        var store = new FakeStore();
        var existing = store.Seed(Metadata(), PackageA);
        var session = Session(store);

        var result = await session.SaveAsync(Edit(Metadata(), r => { r.Settings = new DrawingNestingSettings { CutFirst = true }; }),
            PackageA, existing);

        Assert.Equal(DrawingLibrarySaveOutcome.Revised, result.Outcome);
        Assert.Equal(2, result.Saved.Revision);
        Assert.True(result.Saved.Settings.CutFirst);
        Assert.Equal(existing.PackageSha256, result.Saved.PackageSha256);
    }

    [Fact]
    public void Prefill_FallsBackToTheNestCustomerAndDrawingMaterial_AndUsesALinkOnThisServer()
    {
        var nest = new Nest("Synthetic job") { Units = Units.Millimeters, Customer = "Nest customer", Material = new Material() };
        var drawing = SyntheticDrawing();
        drawing.Customer = " ";
        drawing.Library = Link(Server, Guid.NewGuid(), 2, CutType.Plasma, "Linked notes");
        var other = SyntheticDrawing();
        other.Library = Link("http://other.test:8090", Guid.NewGuid(), 2, CutType.Plasma, "Other notes");

        var linked = Session(new FakeStore(), drawing, nest).Prefill(CutType.Laser, "");
        var foreign = Session(new FakeStore(), other, nest).Prefill(CutType.Waterjet, "");

        Assert.Equal("Nest customer", linked.Customer);
        Assert.Equal(("Drawing material", "Drawing grade"), (linked.MaterialName, linked.MaterialGrade));
        Assert.Equal((0, Units.Millimeters), (linked.Thickness, linked.Units));
        Assert.Equal((CutType.Plasma, "Linked notes"), (linked.CutType, linked.Notes));
        Assert.Equal((CutType.Waterjet, ""), (foreign.CutType, foreign.Notes));
    }

    [Fact]
    public async Task NoMatch_CreatesANewDrawingUnderAClientIdAndLinksIt()
    {
        var store = new FakeStore();
        store.Seed(Edit(Metadata("SYN-101"), r => { r.CutType = CutType.Plasma; }), PackageA);
        store.Seed(Edit(Metadata("SYN-101"), r => { r.Thickness = 0.1875; }), PackageA);
        store.Seed(Metadata("SYN-1010"), PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);
        var metadata = Metadata();

        var duplicates = await session.FindDuplicatesAsync(metadata);
        var result = await session.SaveAsync(metadata, PackageA, duplicates.Default);

        Assert.Empty(duplicates.Matches);
        Assert.Null(duplicates.Default);
        Assert.False(duplicates.RequiresChoice);
        Assert.Equal(DrawingLibrarySaveOutcome.Created, result.Outcome);
        Assert.False(result.RecoveredEarlierSave);
        var upload = Assert.Single(store.Uploads);
        Assert.NotEqual(Guid.Empty, upload);
        Assert.Equal((upload, 1), (result.Saved.Id, result.Saved.Revision));
        Assert.Equal((Server, upload, 1, Sha(PackageA)), (drawing.Library!.ServerKey, drawing.Library.Id, drawing.Library.Revision, drawing.Library.PackageSha256));
        Assert.Equal(("Synthetic Steel", "Grade A", 0.25, "Deburr"), (drawing.Library.MaterialName, drawing.Library.MaterialGrade, drawing.Library.Thickness, drawing.Library.Notes));
        Assert.False(session.HasPendingSave);
    }

    [Fact]
    public async Task OneMatch_IsTheDefault_AndSavesTheNextRevisionGuardedByItsRevision()
    {
        var store = new FakeStore();
        var match = store.Seed(Edit(Metadata(" syn-101 "), r => { r.Customer = "SYNTHETIC   customer"; r.MaterialGrade = "grade a"; }), PackageA);
        store.Revise(match.Id, PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);

        var duplicates = await session.FindDuplicatesAsync(Metadata());
        var result = await session.SaveAsync(Metadata(), PackageB, duplicates.Default);

        Assert.Equal(match.Id, Assert.Single(duplicates.Matches).Id);
        Assert.Equal((match.Id, 2), (duplicates.Default!.Id, duplicates.Default.Revision));
        Assert.Equal(new[] { (match.Id, 2) }, store.Updates);
        Assert.Empty(store.Uploads);
        Assert.Equal((DrawingLibrarySaveOutcome.Revised, 3), (result.Outcome, result.Saved.Revision));
        Assert.Equal((match.Id, 3), (drawing.Library!.Id, drawing.Library.Revision));
    }

    [Fact]
    public async Task SeveralMatches_RequireAChoice_AndArchivedOnesAreNotOffered()
    {
        var store = new FakeStore();
        var older = store.Seed(Metadata(), PackageA);
        var newer = store.Seed(Metadata(), PackageB);
        var archived = store.Seed(Metadata(), PackageA);
        store.Archive(archived.Id);
        var session = Session(store);

        var duplicates = await session.FindDuplicatesAsync(Metadata());
        var result = await session.SaveAsync(Metadata(), PackageB, duplicates.Matches.Single(m => m.Id == older.Id));

        Assert.Equal(new[] { newer.Id, older.Id }, duplicates.Matches.Select(m => m.Id));
        Assert.Null(duplicates.Default);
        Assert.True(duplicates.RequiresChoice);
        Assert.Equal((older.Id, 2), (result.Saved.Id, result.Saved.Revision));
    }

    [Fact]
    public async Task LinkedDrawing_IsTheDefaultWhenItMatches_AndOnlyOfferedWhenItDoesNot()
    {
        var store = new FakeStore();
        var first = store.Seed(Metadata(), PackageA);
        var linkedMatch = store.Seed(Metadata(), PackageA);
        var renamed = store.Seed(Metadata("SYN-OLD"), PackageA);
        var matching = SyntheticDrawing();
        matching.Library = Link(Server, linkedMatch.Id, 1, CutType.Laser, "");
        var drifted = SyntheticDrawing();
        drifted.Library = Link(Server, renamed.Id, 1, CutType.Laser, "");

        var linked = await Session(store, matching).FindDuplicatesAsync(Metadata());
        var offered = await Session(store, drifted).FindDuplicatesAsync(Metadata());

        Assert.Equal(linkedMatch.Id, linked.Default!.Id);
        Assert.False(linked.RequiresChoice);
        Assert.Equal(2, linked.Choices.Count);
        Assert.Equal(renamed.Id, offered.Linked!.Id);
        Assert.Null(offered.Default);
        Assert.True(offered.RequiresChoice);
        Assert.Equal(new[] { linkedMatch.Id, first.Id, renamed.Id }, offered.Choices.Select(c => c.Id));
    }

    [Fact]
    public async Task DuplicatesKnowWhichDetailsTheyWereFoundFor()
    {
        var session = Session(new FakeStore());
        var metadata = Metadata();

        var duplicates = await session.FindDuplicatesAsync(metadata);
        metadata.Notes = "Notes are not part of the match";

        Assert.True(duplicates.IsFor(metadata));
        Assert.True(duplicates.IsFor(Edit(metadata, r => { r.Name = " syn-101"; })));
        Assert.False(duplicates.IsFor(Edit(metadata, r => { r.Thickness = 0.1875; })));
        Assert.False(duplicates.IsFor(Edit(metadata, r => { r.CutType = CutType.Plasma; })));
    }

    [Fact]
    public async Task StaleRevision_Conflicts_ChangesNothing_AndLeavesNothingPending()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);
        var duplicates = await session.FindDuplicatesAsync(Metadata());
        store.Revise(match.Id, PackageB);

        var conflict = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() =>
            session.SaveAsync(Metadata(), PackageA, duplicates.Default));

        Assert.Equal(2, conflict.Current.Revision);
        Assert.Equal(2, store.Current(match.Id).Revision);
        Assert.Null(drawing.Library);
        Assert.False(session.HasPendingSave);
    }

    [Fact]
    public async Task IdenticalRevision_IsUnchanged_AndStillLinks()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);

        var result = await session.SaveAsync(Metadata(), PackageA, (await session.FindDuplicatesAsync(Metadata())).Default);

        Assert.Equal((DrawingLibrarySaveOutcome.Unchanged, 1), (result.Outcome, result.Saved.Revision));
        Assert.Equal((match.Id, 1), (drawing.Library!.Id, drawing.Library.Revision));
    }

    [Fact]
    public async Task LostCreateReply_IsReconciledOnRetry_WithoutASecondUpload()
    {
        var store = new FakeStore { LoseNextReply = true };
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);

        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(Metadata(), PackageA, null));
        Assert.True(session.HasPendingSave);
        Assert.Null(drawing.Library);

        var result = await session.SaveAsync(Metadata(), PackageA, null);

        var upload = Assert.Single(store.Uploads);
        Assert.Equal((upload, 1, DrawingLibrarySaveOutcome.Created), (result.Saved.Id, result.Saved.Revision, result.Outcome));
        Assert.False(result.RecoveredEarlierSave);
        Assert.Equal((upload, 1), (drawing.Library!.Id, drawing.Library.Revision));
        Assert.False(session.HasPendingSave);
    }

    [Fact]
    public async Task LostCreateReply_WithAChangedRetry_ReportsTheEarlierSaveInsteadOfWritingAgain()
    {
        var store = new FakeStore { LoseNextReply = true };
        var session = Session(store);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(Metadata(), PackageA, null));

        var result = await session.SaveAsync(Edit(Metadata(), r => { r.Notes = "Changed since"; }), PackageA, null);

        Assert.True(result.RecoveredEarlierSave);
        Assert.Equal("Deburr", result.Saved.Notes);
        Assert.Single(store.Uploads);
    }

    [Fact]
    public async Task CreateThatFailedBeforeStoring_RetriesUnderTheSameId()
    {
        var store = new FakeStore { FailNextRequest = true };
        var session = Session(store);

        await Assert.ThrowsAsync<HttpRequestException>(() => session.SaveAsync(Metadata(), PackageA, null));
        var result = await session.SaveAsync(Metadata(), PackageA, null);

        Assert.Equal(2, store.Uploads.Count);
        Assert.Equal(store.Uploads[0], store.Uploads[1]);
        Assert.Equal(store.Uploads[0], result.Saved.Id);
        Assert.Equal(DrawingLibrarySaveOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task FailedCreateThenRevise_DoesNotCarryTheNewIdOntoAnotherDrawing()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var session = Session(store);
        store.FailNextRequest = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => session.SaveAsync(Metadata(), PackageB, null));

        var result = await session.SaveAsync(Metadata(), PackageB, store.Current(match.Id));

        Assert.Equal((match.Id, 2), (result.Saved.Id, result.Saved.Revision));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task LostRevisionReply_IsReconciledOnRetry_WithoutASecondRevision()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);
        store.LoseNextReply = true;

        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(Metadata(), PackageB, match));
        store.Revise(match.Id, PackageA);
        var result = await session.SaveAsync(Metadata(), PackageB, match);

        Assert.Single(store.Updates);
        Assert.Equal((2, DrawingLibrarySaveOutcome.Revised), (result.Saved.Revision, result.Outcome));
        Assert.Equal(Sha(PackageB), result.Saved.PackageSha256);
        Assert.Equal(2, drawing.Library!.Revision);
    }

    [Fact]
    public async Task RevisionThatFailedBeforeStoring_RetriesTheSameGuard()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var session = Session(store);
        store.FailNextRequest = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => session.SaveAsync(Metadata(), PackageB, match));
        var result = await session.SaveAsync(Metadata(), PackageB, match);

        Assert.Equal(new[] { (match.Id, 1), (match.Id, 1) }, store.Updates);
        Assert.Equal(2, result.Saved.Revision);
    }

    [Fact]
    public async Task RevisionThatFailedWhileSomeoneElseSaved_ConflictsAndClearsThePendingSave()
    {
        var store = new FakeStore();
        var match = store.Seed(Metadata(), PackageA);
        var drawing = SyntheticDrawing();
        var session = Session(store, drawing);
        store.FailNextRequest = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => session.SaveAsync(Metadata(), PackageB, match));
        store.Revise(match.Id, PackageA, notes: "Someone else");

        await Assert.ThrowsAsync<DrawingLibraryConflictException>(() => session.SaveAsync(Metadata(), PackageB, match));

        Assert.False(session.HasPendingSave);
        Assert.Null(drawing.Library);
        Assert.Single(store.Updates);
    }

    [Fact]
    public async Task InvalidRequests_AreRefusedBeforeSending()
    {
        var store = new FakeStore();
        var session = Session(store);

        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(Edit(Metadata(), r => { r.Name = " "; }), PackageA, null));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(Edit(Metadata(), r => { r.Units = Units.Millimeters; }), PackageA, null));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(Metadata(), new byte[] { 1, 2, 3, 4 }, null));

        Assert.Empty(store.Uploads);
        Assert.False(session.HasPendingSave);
    }

    private static DrawingLibraryLink Link(string server, Guid id, int revision, CutType cutType, string notes) =>
        new(server, id, revision, cutType, 0.25, "Synthetic Steel", "Grade A", Units.Inches, notes, "");

    private static DrawingLibraryRecord Edit(DrawingLibraryRecord source, Action<DrawingLibraryRecord> change)
    {
        var copy = new DrawingLibraryRecord
        {
            Id = source.Id,
            Revision = source.Revision,
            Name = source.Name,
            Customer = source.Customer,
            MaterialName = source.MaterialName,
            MaterialGrade = source.MaterialGrade,
            Thickness = source.Thickness,
            Units = source.Units,
            CutType = source.CutType,
            Notes = source.Notes,
            Settings = source.Settings,
            Archived = source.Archived,
            PackageSha256 = source.PackageSha256,
            PackageSize = source.PackageSize,
            SavedAt = source.SavedAt,
            SavedBy = source.SavedBy,
        };
        change(copy);
        return copy;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>An in-memory library that filters like the server and can fail one request either side of storing.</summary>
    private sealed class FakeStore : IDrawingLibraryStore
    {
        private readonly Dictionary<Guid, List<DrawingLibraryRecord>> _revisions = new();
        private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Throw on the next write without storing anything.</summary>
        public bool FailNextRequest { get; set; }

        /// <summary>Store the next write, then throw as though its reply was lost.</summary>
        public bool LoseNextReply { get; set; }

        public List<Guid> Uploads { get; } = new();

        public List<(Guid Id, int ExpectedRevision)> Updates { get; } = new();

        public int Count => _revisions.Count;

        public DrawingLibraryRecord Current(Guid id) => _revisions[id][^1];

        public DrawingLibraryRecord Seed(DrawingLibraryRecord metadata, byte[] package)
        {
            var id = Guid.NewGuid();
            _revisions[id] = new List<DrawingLibraryRecord> { Stored(metadata, id, 1, package, archived: false) };
            return Current(id);
        }

        public void Revise(Guid id, byte[] package, string? notes = null)
        {
            var current = Current(id);
            _revisions[id].Add(Stored(Edit(current, r => { r.Notes = notes ?? current.Notes; }), id, current.Revision + 1, package, archived: false));
        }

        public void Archive(Guid id)
        {
            var current = Current(id);
            _revisions[id].Add(Stored(current, id, current.Revision + 1, null, archived: true, current.PackageSha256));
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Assert.Null(query.GetValidationError());
            var search = query.NormalizedSearch;
            var matches = _revisions.Values.Select(r => r[^1])
                .Where(r => query.IncludeArchived || !r.Archived)
                .Where(r => search.Length == 0 || new[] { r.Name, r.Customer, r.Notes }
                    .Any(text => text.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .Where(r => Filter(query.Customer, r.Customer) && Filter(query.MaterialName, r.MaterialName)
                    && Filter(query.MaterialGrade, r.MaterialGrade))
                .Where(r => query.Thickness is not { } t || r.Thickness == DrawingLibraryRules.NormalizeThickness(t))
                .Where(r => query.CutType is not { } c || r.CutType == c)
                .OrderByDescending(r => r.SavedAt)
                .ToList();
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = matches.Skip(query.Offset).Take(query.Limit).ToList(),
                Total = matches.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revisions.TryGetValue(id, out var list) ? list[^1] : null);

        public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
            int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
        {
            if (!_revisions.TryGetValue(id, out var list))
                return Task.FromResult<DrawingLibraryPage?>(null);
            var newestFirst = Enumerable.Reverse(list).ToList();
            return Task.FromResult<DrawingLibraryPage?>(new DrawingLibraryPage
            {
                Items = newestFirst.Skip(offset).Take(limit).ToList(),
                Total = list.Count,
                Offset = offset,
                Limit = limit,
            });
        }

        public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
            CancellationToken cancellationToken = default)
        {
            Uploads.Add(record.Id);
            FailBeforeStoring();
            if (_revisions.TryGetValue(record.Id, out var existing))
                throw new DrawingLibraryConflictException(existing[^1]);
            _revisions[record.Id] = new List<DrawingLibraryRecord> { Stored(record, record.Id, 1, package, archived: false) };
            return Reply(Current(record.Id));
        }

        public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
            int expectedRevision, CancellationToken cancellationToken = default)
        {
            Updates.Add((id, expectedRevision));
            FailBeforeStoring();
            var current = Current(id);
            if (current.Revision != expectedRevision || current.Archived)
                throw new DrawingLibraryConflictException(current);
            var next = Stored(record, id, current.Revision + 1, package, archived: false);
            if (SameDetails(current, next) && current.PackageSha256 == next.PackageSha256)
                return Reply(current);
            _revisions[id].Add(next);
            return Reply(next);
        }

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private void FailBeforeStoring()
        {
            if (!FailNextRequest)
                return;
            FailNextRequest = false;
            throw new HttpRequestException("Synthetic connection failure.");
        }

        private Task<DrawingLibraryRecord> Reply(DrawingLibraryRecord record)
        {
            if (!LoseNextReply)
                return Task.FromResult(record);
            LoseNextReply = false;
            throw new IOException("Synthetic lost reply.");
        }

        private DrawingLibraryRecord Stored(DrawingLibraryRecord metadata, Guid id, int revision, byte[]? package,
            bool archived, string? sha = null)
        {
            _clock = _clock.AddMinutes(1);
            var savedAt = _clock;
            return Edit(metadata, r =>
            {
                r.Id = id;
                r.Revision = revision;
                r.Thickness = DrawingLibraryRules.NormalizeThickness(metadata.Thickness);
                r.Archived = archived;
                r.PackageSha256 = sha ?? Sha(package!);
                r.SavedAt = savedAt;
            });
        }

        private static bool SameDetails(DrawingLibraryRecord a, DrawingLibraryRecord b) =>
            a.Name == b.Name && a.Customer == b.Customer && a.MaterialName == b.MaterialName
            && a.MaterialGrade == b.MaterialGrade && a.Thickness == b.Thickness && a.Units == b.Units
            && a.CutType == b.CutType && a.Notes == b.Notes && Equals(a.Settings, b.Settings);

        private static bool Filter(string filter, string value) =>
            string.IsNullOrWhiteSpace(filter) || SharedListNames.Key(filter) == SharedListNames.Key(value);
    }
}
