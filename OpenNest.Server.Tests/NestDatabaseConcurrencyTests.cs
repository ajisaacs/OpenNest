using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestDatabaseConcurrencyTests
{
    // Code-level ownership gate: hold the database's monitor and observe a dedicated
    // synchronous caller waiting on it, not a sleep or a stress-run timing assumption.
    [Theory]
    [InlineData("List")]
    [InlineData("Get")]
    [InlineData("GetFile")]
    [InlineData("Insert")]
    [InlineData("UpdateFile")]
    [InlineData("UpdateMetadata")]
    [InlineData("Delete")]
    [InlineData("Health")]
    [InlineData("Dispose")]
    public async Task Concurrency_EveryOperationWaitsForTheSameMonitor(string operation)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath);
        var id = Guid.NewGuid();
        var record = new NestRecord { Name = "Monitor ownership fixture" };
        database.Insert(id, record, new byte[] { 1, 2, 3 });
        var field = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var sync = field.GetValue(database)!;
        Assert.NotNull(sync);
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            completed.SetResult(Record.Exception(() =>
            {
                switch (operation)
                {
                    case "List": database.List(); break;
                    case "Get": database.Get(id); break;
                    case "GetFile": database.GetFile(id); break;
                    case "Insert": database.Insert(Guid.NewGuid(), record, new byte[] { 4 }); break;
                    case "UpdateFile": database.Update(id, record, new byte[] { 5 }); break;
                    case "UpdateMetadata": database.Update(id, record, null); break;
                    case "Delete": database.Delete(id); break;
                    case "Health": Assert.True(database.IsHealthy()); break;
                    case "Dispose": database.Dispose(); break;
                    default: throw new InvalidOperationException($"Unknown test operation: {operation}");
                }
            }));
        })
        { IsBackground = true };

        var observedWaiting = false;
        var completedWhileHeld = false;
        lock (sync)
        {
            worker.Start();
            observedWaiting = SpinWait.SpinUntil(() => completed.Task.IsCompleted ||
                (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10));
            completedWhileHeld = completed.Task.IsCompleted;
        }

        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Operation did not finish after releasing the monitor.");
        Assert.True(observedWaiting, "Caller never reached the operation within the watchdog.");
        Assert.False(completedWhileHeld, operation + " bypassed the shared monitor.");
        Assert.Null(await completed.Task);
    }

    [Fact]
    public void Concurrency_AllSqlStepsIncludingReadbacks_OwnTheMonitor()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath);
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(database)!;
        var connection = (SqliteConnection)typeof(NestDatabase)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var steps = 0;
        var unownedSteps = 0;
        // Observe every native VM step, including iteration of readers and nested
        // write readbacks. No assertion/exception is thrown across the native callback.
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1, _ =>
        {
            steps++;
            if (!Monitor.IsEntered(sync))
                unownedSteps++;
            return 0;
        }, null);
        try
        {
            var id = Guid.NewGuid();
            var record = Fixture(0, 0);
            Action[] operations =
            [
                () => database.Insert(id, record, new byte[] { 1 }),
                () => database.List(),
                () => database.Get(id),
                () => database.GetFile(id),
                () => database.Update(id, record, new byte[] { 2 }),
                () => database.Update(id, record, null),
                () => Assert.True(database.IsHealthy()),
                () => database.Delete(id),
            ];
            foreach (var operation in operations)
            {
                var before = steps;
                operation();
                Assert.True(steps > before, "Operation did not execute the observed SQLite VM.");
                Assert.Equal(0, unownedSteps);
            }
        }
        finally
        {
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 0, null, null);
        }
    }

    [Fact]
    public async Task Concurrency_DatabaseReadersAndWriters_PreserveExactRecords()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath);
        using var start = new Barrier(4);
        var workers = Enumerable.Range(0, 4).Select(lane => Task.Factory.StartNew(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
            var records = new List<(NestRecord Record, byte[] File)>();
            for (var index = 0; index < 12; index++)
            {
                var id = Guid.NewGuid();
                var record = Fixture(lane, index);
                var file = new byte[] { (byte)lane, (byte)index, 0, 255 };
                var saved = database.Insert(id, record, file);
                AssertStoredRecord(record, id, file, saved);
                AssertRecord(saved, database.Get(id)!);
                Assert.Contains(database.List(), r => r.Id == id);
                Assert.Equal(file, database.GetFile(id));
                record.Comments = "Archive updated";
                file = file.Concat(new byte[] { 42 }).ToArray();
                saved = database.Update(id, record, file)!;
                AssertStoredRecord(record, id, file, saved);
                record.Comments = "Metadata only";
                saved = database.Update(id, record, null)!;
                AssertStoredRecord(record, id, file, saved);
                Assert.Equal(file.LongLength, saved.FileSize);
                Assert.Equal(file, database.GetFile(id));
                var transient = Guid.NewGuid();
                database.Insert(transient, record, new byte[] { 9 });
                Assert.True(database.Delete(transient));
                Assert.Null(database.Get(transient));
                records.Add((saved, file));
            }
            return records;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        var expected = (await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(60))).SelectMany(r => r).ToArray();
        Assert.Equal(expected.Select(r => r.Record.Id).Order(), database.List().Select(r => r.Id).Order());
        foreach (var (record, file) in expected)
        {
            AssertRecord(record, database.Get(record.Id)!);
            Assert.Equal(file, database.GetFile(record.Id));
        }
    }

    [Fact]
    public async Task Concurrency_MultipleHttpClients_PreserveMetadataAndSyntheticArchives()
    {
        using var factory = new ServerFactory();
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, 8).Select(async lane =>
        {
            using var client = factory.CreateClient();
            using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
            await start.Task;
            var records = new List<(NestRecord Record, byte[] File)>();
            for (var index = 0; index < 4; index++)
            {
                var nest = NestApiTests.SyntheticNest();
                nest.Name = $"Synthetic concurrent nest {lane}-{index}";
                var session = new NestSaveSession();
                var file = NestApiTests.Archive(nest);
                var saved = await session.SaveAsync(repository, client.BaseAddress.ToString(), nest, file,
                    cancellationToken: watchdog.Token);
                AssertStoredRecord(NestRecordFactory.FromNest(nest, saved.Id, file.LongLength), saved.Id, file, saved);
                AssertRecord(saved, (await repository.GetMetadataAsync(saved.Id, watchdog.Token))!);
                Assert.Contains(await repository.ListAsync(watchdog.Token), r => r.Id == saved.Id);
                nest.Notes = $"Archive change {lane}-{index}";
                file = NestApiTests.Archive(nest);
                var updated = await session.SaveAsync(repository, client.BaseAddress.ToString(), nest, file,
                    cancellationToken: watchdog.Token);
                AssertStoredRecord(NestRecordFactory.FromNest(nest, saved.Id, file.LongLength), saved.Id, file, updated);
                updated.Comments = $"Metadata only {lane}-{index}";
                saved = await repository.UpdateMetadataAsync(updated.Id, updated, watchdog.Token);
                AssertStoredRecord(updated, updated.Id, file, saved);
                Assert.Equal(file.LongLength, saved.FileSize);
                Assert.Equal(file, await repository.GetFileAsync(saved.Id, watchdog.Token));
                using var health = await client.GetAsync("/healthz", watchdog.Token);
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                records.Add((saved, file));
            }
            return records;
        }).ToArray();
        start.SetResult();

        var expected = (await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(70))).SelectMany(r => r).ToArray();
        using var verifierClient = factory.CreateClient();
        using var verifier = new RemoteNestRepository(verifierClient, verifierClient.BaseAddress!.ToString());
        var listed = await verifier.ListAsync(watchdog.Token);
        Assert.Equal(expected.Select(r => r.Record.Id).Order(), listed.Select(r => r.Id).Order());
        foreach (var (record, file) in expected)
        {
            AssertRecord(record, listed.Single(r => r.Id == record.Id));
            AssertRecord(record, (await verifier.GetMetadataAsync(record.Id, watchdog.Token))!);
            Assert.Equal(file, await verifier.GetFileAsync(record.Id, watchdog.Token));
        }
    }

    private static NestRecord Fixture(int lane, int index) => new()
    {
        Name = $"Synthetic database nest {lane}-{index}",
        Customer = "Synthetic customer",
        DateCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        DateModified = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
        Material = "Synthetic alloy",
        Thickness = 0.25,
        Status = NestStatus.ToBeCut,
        PlateCount = lane + 1,
        PartCount = index + 1,
        Comments = "Initial archive",
        MadeBy = "Synthetic author",
    };

    private static void AssertStoredRecord(NestRecord metadata, Guid id, byte[] file, NestRecord actual)
    {
        var expected = JsonSerializer.Deserialize<NestRecord>(JsonSerializer.Serialize(metadata))!;
        expected.Id = id;
        expected.FileSize = file.LongLength;
        expected.SavedAt = actual.SavedAt;
        Assert.NotEqual(default, actual.SavedAt);
        AssertRecord(expected, actual);
    }

    private static void AssertRecord(NestRecord expected, NestRecord actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
