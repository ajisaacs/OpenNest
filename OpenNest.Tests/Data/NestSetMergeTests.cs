using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestSetMergeTests
{
    private const string Server = "http://synthetic-server:5280";

    [Fact]
    public void Plan_TakesThisServersLinkedDrawings_AndSkipsTheRestWithAReason()
    {
        var a = SetReleasePlanTests.Drawing("SYN-A");
        var b = SetReleasePlanTests.Drawing("SYN-B");
        var nest = new Nest();
        nest.Drawings.Add(Linked("SYN-B", b, 3));
        nest.Drawings.Add(Linked("SYN-A", a, 2));
        nest.Drawings.Add(Drawing("SYN-LOOSE", 4));
        nest.Drawings.Add(Linked("SYN-OTHER", SetReleasePlanTests.Drawing("SYN-OTHER"), 1, server: "http://other-server"));
        nest.Drawings.Add(Linked("SYN-NONE", SetReleasePlanTests.Drawing("SYN-NONE"), 0));
        var cutoff = Drawing("SYN-CUTOFF", 1);
        cutoff.IsCutOff = true;
        nest.Drawings.Add(cutoff);

        // The trailing slash names the same server.
        var merge = NestSetMerge.Plan(nest, Server + "/");

        Assert.Equal(new[] { (a.Id, "SYN-A", 2), (b.Id, "SYN-B", 3) },
            merge.Candidates.Select(c => (c.DrawingId, c.Name, c.Quantity)));
        Assert.Equal(new[] { "SYN-LOOSE", "SYN-NONE", "SYN-OTHER" }, merge.Skipped.Select(s => s.Name));
        Assert.Contains("save it to the library first", merge.Skipped[0].Reason);
        Assert.Contains("no required quantity", merge.Skipped[1].Reason);
        Assert.Contains("http://other-server", merge.Skipped[2].Reason);
        Assert.Equal("- SYN-LOOSE: " + merge.Skipped[0].Reason, merge.DescribeSkipped().Split(Environment.NewLine)[0]);
    }

    [Fact]
    public void Plan_SumsCopiesOfOneLibraryDrawing_AndSkipsAQuantityPastTheSetLimit()
    {
        var a = SetReleasePlanTests.Drawing("SYN-A");
        var big = SetReleasePlanTests.Drawing("SYN-BIG");
        var nest = new Nest();
        nest.Drawings.Add(Linked("SYN-A", a, 2));
        nest.Drawings.Add(Linked("SYN-A COPY", a, 5));
        nest.Drawings.Add(Linked("SYN-BIG", big, DrawingSetRules.MaxQuantity));
        nest.Drawings.Add(Linked("SYN-BIG COPY", big, 1));

        var merge = NestSetMerge.Plan(nest, Server);

        var candidate = Assert.Single(merge.Candidates);
        Assert.Equal(("SYN-A", 7), (candidate.Name, candidate.Quantity));
        var skip = Assert.Single(merge.Skipped);
        Assert.Equal("SYN-BIG", skip.Name);
        Assert.Contains((DrawingSetRules.MaxQuantity + 1).ToString("N0"), skip.Reason);
    }

    [Fact]
    public async Task Apply_AppendsNewDrawings_KeepsExistingQuantities_AndWarnsWhatToCheck()
    {
        var kept = SetReleasePlanTests.Drawing("SYN-KEPT");
        var fresh = SetReleasePlanTests.Drawing("SYN-FRESH");
        var newer = SetReleasePlanTests.Drawing("SYN-NEWER");
        var archived = SetReleasePlanTests.Drawing("SYN-ARCHIVED", archived: true);
        var gone = SetReleasePlanTests.Drawing("SYN-GONE");
        var library = new Library(kept, fresh, newer, archived);
        var nest = new Nest();
        nest.Drawings.Add(Linked("SYN-KEPT", kept, 8));
        nest.Drawings.Add(Linked("SYN-FRESH", fresh, 3));
        nest.Drawings.Add(Linked("SYN-NEWER", newer, 1, revision: newer.Revision - 1));
        nest.Drawings.Add(Linked("SYN-ARCHIVED", archived, 1));
        nest.Drawings.Add(Linked("SYN-GONE", gone, 1));
        nest.Drawings.Add(Drawing("SYN-LOOSE", 1));
        var sets = new InMemoryDrawingSets();
        var set = await sets.CreateAsync(SetReleasePlanTests.Set((kept, 2)));
        var edit = new DrawingSetEditSession(sets, await sets.GetAsync(set.Id), "synthetic");

        var result = await NestSetMerge.Plan(nest, Server).ApplyAsync(library, edit);

        Assert.Equal(new[] { (kept.Id, 2), (archived.Id, 1), (fresh.Id, 3), (newer.Id, 1) },
            edit.Members.Select(m => (m.DrawingId, m.Quantity)));
        Assert.Same(fresh, edit.Members[2].Drawing);
        Assert.Equal(new[] { "SYN-ARCHIVED", "SYN-FRESH", "SYN-NEWER" }, result.Added.Select(a => a.Name));
        Assert.Equal(new NestSetKept("SYN-KEPT", 2, 8), Assert.Single(result.Kept));
        Assert.Equal(new[] { "SYN-LOOSE", "SYN-GONE" }, result.Skipped.Select(s => s.Name));
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("'SYN-ARCHIVED' is archived", result.Warnings[0]);
        Assert.Contains($"revision {newer.Revision} in the library; this nest has revision {newer.Revision - 1}", result.Warnings[1]);
        Assert.False(result.IsClean);
        // A member the set already lists is not read from the library.
        Assert.Equal(new[] { archived.Id, fresh.Id, gone.Id, newer.Id }, library.Reads);

        var text = result.Describe(set.Name);
        Assert.StartsWith($"Added 3 drawings to set '{set.Name}'.", text);
        Assert.Contains("- SYN-KEPT: set 2, nest 8", text);
        Assert.Contains("- SYN-GONE: no longer in the drawing library", text);
        Assert.Contains("Check:", text);
    }

    [Fact]
    public async Task Apply_ToANewSet_IsCleanWhenEveryDrawingIsCurrent()
    {
        var a = SetReleasePlanTests.Drawing("SYN-A");
        var nest = new Nest();
        nest.Drawings.Add(Linked("SYN-A", a, 4));
        var edit = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "synthetic");

        var result = await NestSetMerge.Plan(nest, Server).ApplyAsync(new Library(a), edit);

        Assert.True(result.IsClean);
        Assert.Equal((a.Id, 4), edit.Members.Select(m => (m.DrawingId, m.Quantity)).Single());
        Assert.Equal("Added 1 drawing to the new set.", result.Describe(null));
    }

    [Fact]
    public async Task Apply_LeavesTheEditorUnchanged_WhenALibraryReadFails()
    {
        var a = SetReleasePlanTests.Drawing("SYN-A");
        var b = SetReleasePlanTests.Drawing("SYN-B");
        var nest = new Nest();
        nest.Drawings.Add(Linked("SYN-A", a, 1));
        nest.Drawings.Add(Linked("SYN-B", b, 1));
        var edit = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "synthetic");
        var library = new Library(a, b) { FailOn = b.Id };

        await Assert.ThrowsAsync<IOException>(() => NestSetMerge.Plan(nest, Server).ApplyAsync(library, edit));

        Assert.Empty(edit.Members);
    }

    private static Drawing Drawing(string name, int required)
    {
        var drawing = new Drawing(name);
        drawing.Quantity.Required = required;
        return drawing;
    }

    private static Drawing Linked(string name, DrawingLibraryRecord record, int required, string server = Server,
        int? revision = null)
    {
        var drawing = Drawing(name, required);
        drawing.Library = new DrawingLibraryLink(server, record.Id, revision ?? record.Revision, record.CutType,
            record.Thickness, record.MaterialName, record.MaterialGrade, record.Units, record.Notes, "");
        return drawing;
    }

    private sealed class Library : IDrawingLibraryStore
    {
        private readonly Dictionary<Guid, DrawingLibraryRecord> _records;

        public Library(params DrawingLibraryRecord[] records) => _records = records.ToDictionary(r => r.Id);

        public List<Guid> Reads { get; } = new();

        public Guid FailOn { get; init; }

        public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Reads.Add(id);
            if (id == FailOn)
                throw new IOException("Synthetic connection refused.");
            return Task.FromResult(_records.GetValueOrDefault(id));
        }

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
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
