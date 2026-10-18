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

        Assert.True(session.IsUnchanged("Calibration", new DrawingNestingSettings()));
        Assert.False(session.IsUnchanged("Calibration", settings));
        var saved = await session.SaveAsync("Calibration", settings);

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

        Assert.NotNull(session.GetError("", invalid));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveAsync("", invalid));
        Assert.Empty(store.Updates);
    }

    [Fact]
    public void ArchivedDrawings_CannotBeEdited()
    {
        Assert.Throws<ArgumentException>(() => new DrawingLibraryEditSession(new RecordingStore(), Listed(archived: true), ""));
    }

    private sealed class RecordingStore : IDrawingLibraryStore
    {
        public List<(Guid Id, int Expected, DrawingLibraryRecord Record)> Updates { get; } = new();

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((id, expectedRevision, record));
            return Task.FromResult(new DrawingLibraryRecord
            {
                Id = id,
                Revision = expectedRevision + 1,
                Name = record.Name,
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
