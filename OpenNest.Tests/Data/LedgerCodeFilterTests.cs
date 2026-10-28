using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

/// <summary>
/// Codes other open nests are coded with leave Auto Nest's ledger rows: every copy of a code,
/// with its quantity and location, and rows left empty disappear.
/// </summary>
public class LedgerCodeFilterTests
{
    [Fact]
    public void ABundleLosesEveryCopyOfItsCode_AndAnEmptiedRowIsDropped()
    {
        var bundle = Row("ledger:1", ["B-1", "B-1", "B-1"]);
        var sheet = Row("ledger:2", ["S-1"]);
        var free = Row("ledger:3", ["S-2"]);

        var rows = LedgerCodeFilter.Exclude([bundle, sheet, free], ["B-1", "S-1"], out var removed);

        Assert.Equal(["ledger:3"], rows.Select(r => r.Id));
        Assert.Same(free, rows[0]);
        Assert.Equal(4, removed);
    }

    [Fact]
    public void DropsKeepTheirOtherCodesLocationsAndQuantity()
    {
        var drops = Row("ledger:1", ["D-1", "D-2", "D-3"], ("D-1", "R1"), ("D-2", "R2"), ("D-3", "R1")) with
        {
            Rank = 2,
            Label = "A36",
        };

        var row = Assert.Single(LedgerCodeFilter.Exclude([drops], ["D-2", "UNRELATED"], out var removed));

        Assert.Equal(1, removed);
        Assert.Equal(2, row.Quantity);
        Assert.Equal(["D-1", "D-3"], row.ItemCodes);
        Assert.Equal(["R1"], row.Locations);
        Assert.Equal(new[] { "D-1", "D-3" }, row.ItemLocations.Keys.Order());
        Assert.Equal(("ledger:1", 2, "A36"), (row.Id, row.Rank, row.Label));
        // The original row is left alone.
        Assert.Equal(3, drops.Quantity);
    }

    [Fact]
    public void NoCodesInUse_KeepsEveryRowAsItWas()
    {
        var rows = new[] { Row("ledger:1", ["S-1"]) };

        var kept = LedgerCodeFilter.Exclude(rows, Array.Empty<string>(), out var removed);

        Assert.Same(rows[0], Assert.Single(kept));
        Assert.Equal(0, removed);
    }

    [Fact]
    public void CodesMatchExactly()
    {
        var kept = LedgerCodeFilter.Exclude([Row("ledger:1", ["s-1"])], ["S-1"], out var removed);

        Assert.Single(kept);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void MappingSummary_CountsTheSheetsOnOtherNests()
    {
        var mapping = LedgerStockMapper.Map(
        [
            Item("B-1", LedgerItemKinds.FullSheetBundle, 3),
            Item("D-1", LedgerItemKinds.Drop, 1),
            Item("D-2", LedgerItemKinds.Drop, 1),
        ], Units.Inches);

        var filtered = LedgerCodeFilter.Exclude(mapping, ["B-1", "D-1"]);

        Assert.Equal(4, filtered.OnOtherNests);
        Assert.Equal("Ledger: 0 sheet sizes, 1 drop (4 on other nests)", filtered.Summary);
        Assert.Equal("Ledger: 1 sheet size, 2 drops", mapping.Summary);
    }

    [Fact]
    public void CodesInUse_NameEachNestOnceAndReportWhyTheyAreUnknown()
    {
        var known = LedgerCodesInUse.Known(
        [
            Use("S-1", "Nest B"),
            Use("S-1", "Nest A"),
            Use("S-2", "Nest A"),
        ]);

        Assert.True(known.IsKnown);
        Assert.Equal(["S-1", "S-2"], known.Codes.Order());
        Assert.Equal(["Nest A", "Nest B"], known.NestsUsing("S-1"));

        var unknown = LedgerCodesInUse.Unknown("nests are saved as files");
        Assert.False(unknown.IsKnown);
        Assert.Empty(unknown.Codes);
    }

    [Fact]
    public async Task ReadingCodes_NeverThrowsForAnExpectedFailure()
    {
        Assert.Equal("nests are saved as files", (await LedgerCodesInUse.ReadAsync(null, Guid.Empty, default)).Unavailable);
        Assert.Equal("the nest server needs an update",
            (await LedgerCodesInUse.ReadAsync(new Repository(_ => throw new NotSupportedException()), Guid.Empty, default)).Unavailable);
        Assert.Equal("the nest server could not be reached",
            (await LedgerCodesInUse.ReadAsync(new Repository(_ => throw new IOException("down")), Guid.Empty, default)).Unavailable);

        var slow = new Repository(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return [];
        });
        Assert.Equal("the nest server did not answer",
            (await LedgerCodesInUse.ReadAsync(slow, Guid.Empty, default, TimeSpan.FromMilliseconds(20))).Unavailable);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LedgerCodesInUse.ReadAsync(slow, Guid.Empty, cancel.Token));
    }

    [Fact]
    public async Task ReadingCodes_ExcludesTheNestBeingEdited()
    {
        var editing = Guid.NewGuid();
        Guid asked = default;
        var repository = new Repository(_ => Task.FromResult<IReadOnlyList<NestStockCodeUse>>([Use("S-1", "Other")]))
        {
            OnAsk = id => asked = id,
        };

        var codes = await LedgerCodesInUse.ReadAsync(repository, editing, default);

        Assert.Equal(editing, asked);
        Assert.Equal(["S-1"], codes.Codes);
    }

    internal static NestStockCodeUse Use(string code, string nest) =>
        new() { ItemCode = code, NestId = Guid.NewGuid(), NestName = nest, Status = NestStatus.Quote };

    private static LedgerStockRow Row(string id, string[] codes, params (string Code, string Location)[] locations) =>
        new(id, new Size(48, 96), codes.Length, IsDrop: codes.Distinct().Count() > 1, IsIrregular: false, codes,
            locations.Select(l => l.Location).Distinct().ToList())
        {
            ItemLocations = locations.ToDictionary(l => l.Code, l => l.Location, StringComparer.Ordinal),
        };

    private static LedgerStockItem Item(string code, string kind, int quantity) =>
        new(code, 7, kind, kind == LedgerItemKinds.Drop ? 24 : 48, kind == LedgerItemKinds.Drop ? 36 : 96, quantity,
            LedgerItemShapes.Rectangular, "R1", "Rack 1", null, null, DateTime.UtcNow);

    private sealed class Repository(Func<CancellationToken, Task<IReadOnlyList<NestStockCodeUse>>> read) : INestRepository
    {
        public Action<Guid>? OnAsk { get; init; }

        public Task<IReadOnlyList<NestStockCodeUse>> GetStockCodesInUseAsync(Guid excludeNestId, CancellationToken cancellationToken)
        {
            OnAsk?.Invoke(excludeNestId);
            return read(cancellationToken);
        }

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<NestRecord> UploadAsync(byte[] nestFile, NestRecord record, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] nestFile, NestRecord record, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
