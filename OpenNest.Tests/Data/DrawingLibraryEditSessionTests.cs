using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingLibraryEditSessionTests
{
    private static DrawingLibraryRecord Listed(bool archived = false) => new()
    {
        Id = Guid.NewGuid(),
        Revision = 4,
        Name = "Test square",
        Customer = "Synthetic customer",
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        Units = Units.Inches,
        CutType = CutType.Laser,
        Notes = "Calibration",
        Archived = archived,
        PackageSha256 = "abc123",
        SavedBy = "Someone else",
    };

    [Fact]
    public async Task SaveAsync_KeepsTheDetailsAndSendsNewSettingsAtTheListedRevision()
    {
        var listed = Listed();
        var store = new RecordingStore();
        var session = new DrawingLibraryEditSession(store, listed, "Synthetic operator");
        var settings = new DrawingNestingSettings { CutFirst = true };

        Assert.True(session.IsUnchanged(session.Details, "Calibration", new DrawingNestingSettings()));
        Assert.False(session.IsUnchanged(session.Details, "Calibration", settings));
        var saved = await session.SaveAsync(session.Details, "Calibration", settings);

        var (id, expected, record) = Assert.Single(store.Updates);
        Assert.Equal((listed.Id, 4), (id, expected));
        Assert.Equal((listed.Name, listed.Customer, listed.MaterialName, listed.MaterialGrade, listed.Thickness, listed.CutType),
            (record.Name, record.Customer, record.MaterialName, record.MaterialGrade, record.Thickness, record.CutType));
        Assert.Equal(("Calibration", "Synthetic operator"), (record.Notes, record.SavedBy));
        Assert.Equal(settings, record.Settings);
        Assert.Equal(5, saved.Revision);
    }

    [Fact]
    public async Task SaveAsync_RefusesInvalidSettingsWithoutSending()
    {
        var store = new RecordingStore();
        var session = new DrawingLibraryEditSession(store, Listed(), "");
        var invalid = new DrawingNestingSettings { StepAngle = -1 };

        Assert.NotNull(session.GetError(session.Details, "", invalid));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(session.Details, "", invalid));
        Assert.Empty(store.Updates);
    }

    [Fact]
    public async Task SaveAsync_SendsEditedDetailsTrimmedAsTheNextRevisionOfTheSamePackage()
    {
        var listed = Listed();
        var store = new RecordingStore();
        var session = new DrawingLibraryEditSession(store, listed, "Synthetic operator");
        var details = new DrawingLibraryDetails(" Test square B ", "Other customer", " Synthetic Stainless ", "304 ",
            0.1875, CutType.Waterjet);

        Assert.False(session.IsUnchanged(details, listed.Notes, listed.Settings));
        var saved = await session.SaveAsync(details, listed.Notes, listed.Settings);

        var (id, expected, record) = Assert.Single(store.Updates);
        Assert.Equal((listed.Id, 4), (id, expected));
        Assert.Equal(("Test square B", "Other customer", "Synthetic Stainless", "304", 0.1875, CutType.Waterjet),
            (record.Name, record.Customer, record.MaterialName, record.MaterialGrade, record.Thickness, record.CutType));
        // Units are never edited here: they give the stored geometry its meaning.
        Assert.Equal(listed.Units, record.Units);
        Assert.Equal(("Test square B", 0.1875), (saved.Name, saved.Thickness));
    }

    [Fact]
    public void Details_StartFromTheListedRecordAndOnlyRealChangesCount()
    {
        var listed = Listed();
        var session = new DrawingLibraryEditSession(new RecordingStore(), listed, "");

        Assert.Equal(new DrawingLibraryDetails(listed.Name, listed.Customer, listed.MaterialName, listed.MaterialGrade,
            listed.Thickness, listed.CutType), session.Details);
        // Surrounding spaces and sub-rounding thickness noise are not edits.
        Assert.True(session.IsUnchanged(session.Details with { Name = " Test square ", Thickness = 0.250001 },
            listed.Notes, listed.Settings));
        Assert.False(session.IsUnchanged(session.Details with { MaterialGrade = "Grade B" }, listed.Notes, listed.Settings));
    }

    [Fact]
    public async Task SaveAsync_RefusesABlankNameWithoutSending()
    {
        var store = new RecordingStore();
        var session = new DrawingLibraryEditSession(store, Listed(), "");
        var blank = session.Details with { Name = "  " };

        Assert.Equal("name is required.", session.GetError(blank, "", new DrawingNestingSettings()));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync(blank, "", new DrawingNestingSettings()));
        Assert.Empty(store.Updates);
    }

    [Fact]
    public async Task SaveAsync_RefusesAReplyWithOtherDetailsThanSent()
    {
        var store = new RecordingStore { DropDetails = true };
        var session = new DrawingLibraryEditSession(store, Listed(), "");

        await Assert.ThrowsAsync<IOException>(() =>
            session.SaveAsync(session.Details with { MaterialName = "Other" }, "", new DrawingNestingSettings()));
    }

    [Fact]
    public void ArchivedDrawings_CannotBeEdited()
    {
        Assert.Throws<ArgumentException>(() => new DrawingLibraryEditSession(new RecordingStore(), Listed(archived: true), ""));
    }

    private sealed class RecordingStore : IDrawingLibraryStore
    {
        public List<(Guid Id, int Expected, DrawingLibraryRecord Record)> Updates { get; } = new();

        /// <summary>Replies with the listed details instead of the sent ones.</summary>
        public bool DropDetails { get; init; }

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((id, expectedRevision, record));
            return Task.FromResult(new DrawingLibraryRecord
            {
                Id = id,
                Revision = expectedRevision + 1,
                Name = DropDetails ? "Test square" : record.Name,
                Customer = DropDetails ? "Synthetic customer" : record.Customer,
                MaterialName = DropDetails ? "Synthetic Steel" : record.MaterialName,
                MaterialGrade = DropDetails ? "Grade A" : record.MaterialGrade,
                Thickness = DropDetails ? 0.25 : record.Thickness,
                Units = record.Units,
                CutType = DropDetails ? CutType.Laser : record.CutType,
                Notes = record.Notes,
                Settings = record.Settings,
                PackageSha256 = "ABC123",
            });
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
