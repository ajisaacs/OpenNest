using System.Net;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Server.Tests;

/// <summary>
/// <c>GET /api/nests/stock-item-codes</c> lists the ledger item codes of open nests from codes
/// stored at each archive write, so Auto Nest can leave them out before ML holds exist.
/// </summary>
public sealed class NestStockCodesTests
{
    [Fact]
    public async Task OpenNests_ListTheirCodesOncePerNest()
    {
        using var session = new Session();
        var first = await session.SaveAsync("Nest A", NestStatus.Quote, ["SHEET-1", "SHEET-1"], ["DROP-7"]);
        var second = await session.SaveAsync("Nest B", NestStatus.ToBeCut, ["SHEET-1"]);
        await session.SaveAsync("Uncoded", NestStatus.Quote);

        var uses = await session.Repository.GetStockCodesInUseAsync(Guid.Empty);

        Assert.Equal(
            [("DROP-7", first.Id, "Nest A"), ("SHEET-1", first.Id, "Nest A"), ("SHEET-1", second.Id, "Nest B")],
            uses.Select(u => (u.ItemCode, u.NestId, u.NestName)).ToArray());
        Assert.Equal(NestStatus.ToBeCut, uses[2].Status);
    }

    [Fact]
    public async Task Exclude_LeavesOutTheNestBeingEdited()
    {
        using var session = new Session();
        var editing = await session.SaveAsync("Editing", NestStatus.Quote, ["SHEET-1"]);
        await session.SaveAsync("Other", NestStatus.Quote, ["SHEET-2"]);

        var uses = await session.Repository.GetStockCodesInUseAsync(editing.Id);

        Assert.Equal(["SHEET-2"], uses.Select(u => u.ItemCode));
    }

    [Fact]
    public async Task CutAndDeletedNests_AreNotOpen()
    {
        using var session = new Session();
        var draft = await session.SaveAsync("Draft", NestStatus.Draft, ["D-1"]);
        var cut = await session.SaveAsync("Cut", NestStatus.Quote, ["C-1"]);
        var deleted = await session.SaveAsync("Deleted", NestStatus.Quote, ["X-1"]);

        cut.Status = NestStatus.HasBeenCut;
        await session.Repository.UpdateMetadataAsync(cut.Id, cut, cut.Revision);
        await session.Repository.DeleteAsync(deleted.Id, deleted.Revision);

        Assert.Equal(["D-1"], (await session.Repository.GetStockCodesInUseAsync(Guid.Empty)).Select(u => u.ItemCode));
        Assert.Equal(draft.Id, Assert.Single(await session.Repository.GetStockCodesInUseAsync(Guid.Empty)).NestId);

        // Recovering a nest brings its codes back without rereading the archive.
        var gone = (await session.Repository.GetDeletedMetadataAsync(deleted.Id))!;
        await session.Repository.RecoverAsync(deleted.Id, gone.Revision);
        Assert.Equal(["D-1", "X-1"], (await session.Repository.GetStockCodesInUseAsync(Guid.Empty)).Select(u => u.ItemCode));
    }

    [Fact]
    public async Task SavingAndRestoring_ReplaceTheStoredCodes()
    {
        using var session = new Session();
        var record = await session.SaveAsync("Nest", NestStatus.Quote, ["OLD-1"]);

        var nest = Nest("Nest", NestStatus.Quote, ["NEW-1"]);
        var updated = await session.Repository.UpdateFileAsync(record.Id, NestApiTests.Archive(nest),
            Record(record.Id, nest), record.Revision);
        Assert.Equal(["NEW-1"], (await session.Repository.GetStockCodesInUseAsync(Guid.Empty)).Select(u => u.ItemCode));

        await session.Repository.RestoreAsync(record.Id, 1, updated.Revision);
        Assert.Equal(["OLD-1"], (await session.Repository.GetStockCodesInUseAsync(Guid.Empty)).Select(u => u.ItemCode));
    }

    [Fact]
    public async Task InvalidExclude_IsABadRequest()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/nests/stock-item-codes?exclude=nope");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OlderServer_IsNotSupported()
    {
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri("http://old/") };
        using var repository = new RemoteNestRepository(http, "http://old/");

        await Assert.ThrowsAsync<NotSupportedException>(() => repository.GetStockCodesInUseAsync(Guid.Empty));
    }

    [Fact]
    public void V13Upgrade_ReadsTheCodesOfEveryStoredNest()
    {
        using var factory = new ServerFactory();
        var open = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            var nest = Nest("Open", NestStatus.Quote, ["SHEET-1"]);
            database.Insert(open, Record(open, nest), NestApiTests.Archive(nest));
            var gone = Nest("Gone", NestStatus.Quote, ["SHEET-2"]);
            var record = database.Insert(deleted, Record(deleted, gone), NestApiTests.Archive(gone));
            database.SetDeleted(deleted, true, record.Revision);
        }
        Execute(factory.DatabasePath, "DROP TABLE nest_stock_codes; PRAGMA user_version=13;");

        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            Assert.Equal(["SHEET-1"], database.StockCodesInUse(null).Select(u => u.ItemCode));
            var record = database.Get(deleted, deleted: true)!;
            database.SetDeleted(deleted, false, record.Revision);
            Assert.Equal(["SHEET-1", "SHEET-2"], database.StockCodesInUse(null).Select(u => u.ItemCode));
        }
        Assert.Equal(14L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
    }

    private static Nest Nest(string name, NestStatus status, params string[][] plateCodes)
    {
        var nest = NestApiTests.SyntheticNest();
        nest.Name = name;
        nest.Status = status;
        var drawing = nest.Drawings.Single();
        nest.Plates.Clear();
        foreach (var codes in plateCodes.DefaultIfEmpty(Array.Empty<string>()))
        {
            var plate = new Plate(new Size(48, 96)) { Quantity = System.Math.Max(codes.Length, 1) };
            plate.Parts.Add(new Part(drawing, new Vector(2, 3)));
            plate.StockItemCodes = codes;
            nest.Plates.Add(plate);
        }
        return nest;
    }

    private static NestRecord Record(Guid id, Nest nest) => new()
    {
        Id = id,
        Name = nest.Name,
        Status = nest.Status,
        DateCreated = nest.DateCreated,
        DateModified = nest.DateCreated,
    };

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private sealed class Session : IDisposable
    {
        private readonly ServerFactory factory = new();
        private readonly HttpClient client;

        public Session()
        {
            client = factory.CreateClient();
            Repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        }

        public RemoteNestRepository Repository { get; }

        public Task<NestRecord> SaveAsync(string name, NestStatus status, params string[][] plateCodes)
        {
            var nest = Nest(name, status, plateCodes);
            return Repository.UploadAsync(NestApiTests.Archive(nest), Record(Guid.NewGuid(), nest));
        }

        public void Dispose()
        {
            Repository.Dispose();
            client.Dispose();
            factory.Dispose();
        }
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
