using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingLibraryChoicesTests
{
    private static DrawingLibraryRecord Record(string customer, string material, string grade, double thickness,
        Units units = Units.Inches, bool archived = false) => new()
        {
            Id = Guid.NewGuid(),
            Revision = 1,
            Name = "Synthetic part",
            Customer = customer,
            MaterialName = material,
            MaterialGrade = grade,
            Thickness = thickness,
            Units = units,
            Archived = archived,
        };

    [Fact]
    public void Lists_KeepTheFirstSpellingOfEachName_Sorted_AndSkipBlanksAndArchivedDrawings()
    {
        var choices = DrawingLibraryChoices.From(new[]
        {
            Record("Synthetic  Co", "Synthetic Steel", "Grade A", 0.25),
            Record("synthetic co", "SYNTHETIC STEEL", "grade a", 0.25),
            Record("", "Alpha Alloy", "", 0.5),
            Record("Archived Co", "Archived Metal", "X", 1, archived: true),
        }, Units.Inches, new[] { new MaterialConfig { Name = "Synthetic Steel", Grade = "Grade C" }, new MaterialConfig { Name = "Beta Brass" } });

        Assert.Equal(new[] { "Synthetic Co" }, choices.Customers);
        Assert.Equal(new[] { "Alpha Alloy", "Beta Brass", "Synthetic Steel" }, choices.Materials);
        Assert.Equal(new[] { "Grade A", "Grade C" }, choices.GradesOf(" synthetic steel "));
        Assert.Empty(choices.GradesOf(""));
    }

    [Fact]
    public void Thicknesses_AreTheMaterialsInTheEditsUnits_NarrowedByAGivenGrade()
    {
        var choices = DrawingLibraryChoices.From(new[]
        {
            Record("", "Synthetic Steel", "Grade A", 0.25),
            Record("", "Synthetic Steel", "Grade B", 0.1875),
            Record("", "Synthetic Steel", "Grade B", 0.18750001),
            Record("", "Synthetic Steel", "Grade A", 6.35, Units.Millimeters),
            Record("", "Synthetic Steel", "Grade A", 0),
            Record("", "Other", "Grade A", 1),
        }, Units.Inches);

        Assert.Equal(new[] { 0.1875, 0.25 }, choices.ThicknessesOf("Synthetic Steel", ""));
        Assert.Equal(new[] { 0.1875 }, choices.ThicknessesOf("synthetic steel", "GRADE B"));
        Assert.Empty(choices.ThicknessesOf("", "Grade A"));
    }

    [Fact]
    public async Task EditSession_LoadsChoicesFromEveryPage_AndIncludesTheListedDrawing()
    {
        var listed = Record("Listed Co", "Listed Metal", "L1", 0.5);
        var records = Enumerable.Range(0, DrawingLibraryQuery.MaxLimit + 3)
            .Select(i => Record($"Customer {i:000}", "Synthetic Steel", "Grade A", 0.25))
            .ToList();
        var library = new PagedLibrary(records);

        var choices = await new DrawingLibraryEditSession(library, listed, "").LoadChoicesAsync();

        Assert.Equal(2, library.Queries.Count);
        Assert.All(library.Queries, q => Assert.False(q.IncludeArchived));
        Assert.Equal(records.Count + 1, choices.Customers.Count);
        Assert.Contains("Listed Metal", choices.Materials);
    }

    private sealed class PagedLibrary : IDrawingLibraryStore
    {
        private readonly List<DrawingLibraryRecord> _records;

        public PagedLibrary(List<DrawingLibraryRecord> records) => _records = records;

        public List<DrawingLibraryQuery> Queries { get; } = new();

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = _records.Skip(query.Offset).Take(query.Limit).ToArray(),
                Total = _records.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

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

        public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
