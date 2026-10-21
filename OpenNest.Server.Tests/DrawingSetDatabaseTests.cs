using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

public sealed class DrawingSetDatabaseTests
{
    [Fact]
    public void V6Upgrade_AddsSetTablesAndKeepsEveryOtherRowAndIsIdempotent()
    {
        using var factory = new ServerFactory();
        var drawing = CreateVersion6Database(factory.DatabasePath);
        var before = Digest(factory.DatabasePath);

        for (var open = 0; open < 2; open++)
        {
            using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
                Assert.Empty(database.QueryDrawingSets(new DrawingSetQuery { IncludeArchived = true }).Items);
            Assert.Equal(before, Digest(factory.DatabasePath));
            Assert.Equal(10L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        }

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set(members: (drawing, 2));
        Assert.Equal(1, upgraded.InsertDrawingSet(set.Id, set).Revision);
        Assert.Equal(drawing, upgraded.GetDrawingSet(set.Id)!.Members.Single().Drawing!.Id);
    }

    [Fact]
    public void FailedSetMigration_RollsBackAndCanRestart()
    {
        using var factory = new ServerFactory();
        CreateVersion6Database(factory.DatabasePath);
        var before = Digest(factory.DatabasePath);
        // A table holding an index's name makes the set schema fail part-way through.
        Execute(factory.DatabasePath, "CREATE TABLE ix_drawing_set_members_drawing (fixture TEXT)");

        Assert.Throws<SqliteException>(() => new NestDatabase(factory.DatabasePath, pooling: false));

        Assert.Equal(6L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'drawing_set%' OR name LIKE 'set_release%'"));
        Assert.Equal(before, Digest(factory.DatabasePath));
        Execute(factory.DatabasePath, "DROP TABLE ix_drawing_set_members_drawing");
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(10L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
    }

    [Fact]
    public void EveryChange_AppendsAnImmutableRevisionWithItsOwnMembers()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var a = AddDrawing(database, "Synthetic A");
        var b = AddDrawing(database, "Synthetic B");
        var set = Set(members: [(a, 2), (b, 1)]);

        database.InsertDrawingSet(set.Id, set);
        var edit = Set("Renamed kit", set.Id, (b, 5));
        Assert.Equal(2, database.UpdateDrawingSet(set.Id, edit, 1)!.Revision);
        Assert.True(database.SetDrawingSetArchived(set.Id, true, 2)!.Archived);
        var restored = database.SetDrawingSetArchived(set.Id, false, 3)!;

        Assert.Equal((4, "Renamed kit", false), (restored.Revision, restored.Name, restored.Archived));
        Assert.Equal(new[] { (b, 5) }, MembersOf(restored));
        var history = database.DrawingSetHistory(set.Id, 0, 100)!;
        Assert.Equal(new[] { 4, 3, 2, 1 }, history.Items.Select(r => r.Revision));
        Assert.Equal(new[] { false, true, false, false }, history.Items.Select(r => r.Archived));
        Assert.Equal(new[] { 1, 1, 1, 2 }, history.Items.Select(r => r.MemberCount));
        Assert.All(history.Items, r => Assert.Empty(r.Members));
        var first = database.GetDrawingSetRevision(set.Id, 1)!;
        Assert.Equal(("Synthetic kit", 2), (first.Name, first.MemberCount));
        Assert.Equal(new[] { (a, 2), (b, 1) }, MembersOf(first));
        Assert.Null(database.GetDrawingSetRevision(set.Id, 5));
    }

    [Fact]
    public void IdenticalSave_IsANoOpButMemberOrderIsAChange()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var a = AddDrawing(database, "Synthetic A");
        var b = AddDrawing(database, "Synthetic B");
        var set = Set(members: [(a, 2), (b, 1)]);
        database.InsertDrawingSet(set.Id, set);

        set.SavedBy = "Another operator";
        Assert.Equal(1, database.UpdateDrawingSet(set.Id, set, 1)!.Revision);
        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_set_revisions"));

        var reordered = Set(members: [(b, 1), (a, 2)]);
        var saved = database.UpdateDrawingSet(set.Id, reordered, 1)!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(new[] { (b, 1), (a, 2) }, MembersOf(saved));
    }

    [Fact]
    public void Members_JoinTheDrawingsCurrentRevisionIncludingArchived()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var set = Set(members: (drawing, 3));
        database.InsertDrawingSet(set.Id, set);

        var edit = database.GetDrawing(drawing)!;
        edit.MaterialName = "Synthetic Aluminum";
        database.UpdateDrawing(drawing, edit, Package("revised"), 1);
        database.SetDrawingArchived(drawing, true, 2);

        foreach (var read in new[] { database.GetDrawingSet(set.Id)!, database.GetDrawingSetRevision(set.Id, 1)! })
        {
            var joined = read.Members.Single().Drawing!;
            Assert.Equal((3, "Synthetic Aluminum", true), (joined.Revision, joined.MaterialName, joined.Archived));
            Assert.Equal(database.GetDrawing(drawing)!.PackageSha256, joined.PackageSha256);
        }
        Assert.Equal(1, database.GetDrawingSet(set.Id)!.Revision);
    }

    [Fact]
    public void SetsContaining_ReadsCurrentRevisionsOnlyAndIncludesArchivedSets()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var a = AddDrawing(database, "Synthetic A");
        var b = AddDrawing(database, "Synthetic B");
        var kept = Set("b kit", members: (a, 1));
        var moved = Set("a kit", members: (a, 1));
        var archived = Set("c kit", members: [(a, 1), (b, 1)]);
        foreach (var set in new[] { kept, moved, archived })
            database.InsertDrawingSet(set.Id, set);
        database.UpdateDrawingSet(moved.Id, Set("a kit", moved.Id, (b, 1)), 1);
        database.SetDrawingSetArchived(archived.Id, true, 1);

        var usedA = database.DrawingSetsContaining(a, 0, 100)!;
        Assert.Equal(new[] { "b kit", "c kit" }, usedA.Items.Select(s => s.Name));
        Assert.Equal(2, usedA.Total);
        Assert.Equal(new[] { "a kit", "c kit" }, database.DrawingSetsContaining(b, 0, 100)!.Items.Select(s => s.Name));
        Assert.Equal(new[] { "c kit" }, database.DrawingSetsContaining(a, 1, 1)!.Items.Select(s => s.Name));
        Assert.Null(database.DrawingSetsContaining(Guid.NewGuid(), 0, 100));
    }

    [Fact]
    public void Writes_AreGuardedByRevisionAndArchivedState()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var set = Set(members: (drawing, 1));
        database.InsertDrawingSet(set.Id, set);

        Assert.Equal(1, Assert.Throws<DrawingSetConflictException>(() => database.InsertDrawingSet(set.Id, Set("Other", set.Id))).Current.Revision);
        Assert.Throws<DrawingSetConflictException>(() => database.UpdateDrawingSet(set.Id, Set("Stale", set.Id), 2));
        Assert.Throws<DrawingSetConflictException>(() => database.SetDrawingSetArchived(set.Id, false, 1));
        database.SetDrawingSetArchived(set.Id, true, 1);
        var refused = Assert.Throws<DrawingSetConflictException>(() => database.UpdateDrawingSet(set.Id, Set("Archived", set.Id), 2));
        Assert.True(refused.Current.Archived);
        Assert.Single(refused.Current.Members);
        Assert.Null(database.UpdateDrawingSet(Guid.NewGuid(), set, 1));
        Assert.Null(database.SetDrawingSetArchived(Guid.NewGuid(), true, 1));
        Assert.Null(database.DrawingSetHistory(Guid.NewGuid(), 0, 100));
        Assert.Equal(2L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_set_revisions"));
    }

    [Fact]
    public void DirectWrites_EnforceTheSharedRulesWithNoState()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var tooMany = Enumerable.Range(0, DrawingSetRules.MaxMembers + 1).Select(_ => (Guid.NewGuid(), 1)).ToArray();

        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.Empty, Set()));
        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(" ")));
        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(members: (drawing, 0))));
        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(members: (drawing, DrawingSetRules.MaxQuantity + 1))));
        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(members: [(drawing, 1), (drawing, 2)])));
        Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(members: tooMany)));
        var unknown = Assert.Throws<ArgumentException>(() => database.InsertDrawingSet(Guid.NewGuid(), Set(members: [(drawing, 1), (Guid.NewGuid(), 1)])));
        Assert.Contains("is not in the library", unknown.Message);
        Assert.Throws<ArgumentException>(() => database.QueryDrawingSets(new DrawingSetQuery { Limit = 501 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => database.DrawingSetHistory(Guid.NewGuid(), 0, 201));
        Assert.Throws<ArgumentOutOfRangeException>(() => database.DrawingSetsContaining(drawing, -1, 10));

        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_set_revisions"));
        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_set_members"));

        // The boundaries themselves are accepted, including an empty set.
        Assert.Equal(0, database.InsertDrawingSet(Guid.NewGuid(), Set()).MemberCount);
        Assert.Equal(DrawingSetRules.MaxQuantity,
            database.InsertDrawingSet(Guid.NewGuid(), Set(members: (drawing, DrawingSetRules.MaxQuantity))).Members.Single().Quantity);
    }

    // Code-level ownership gate, as for the drawing operations.
    [Theory]
    [InlineData("GetDrawingSet")]
    [InlineData("GetDrawingSetRevision")]
    [InlineData("QueryDrawingSets")]
    [InlineData("InsertDrawingSet")]
    [InlineData("UpdateDrawingSet")]
    [InlineData("SetDrawingSetArchived")]
    [InlineData("DrawingSetHistory")]
    [InlineData("DrawingSetsContaining")]
    public async Task Concurrency_EverySetOperationWaitsForTheSameMonitor(string operation)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var set = Set(members: (drawing, 1));
        database.InsertDrawingSet(set.Id, set);
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => completed.SetResult(Xunit.Record.Exception(() =>
        {
            switch (operation)
            {
                case "GetDrawingSet": database.GetDrawingSet(set.Id); break;
                case "GetDrawingSetRevision": database.GetDrawingSetRevision(set.Id, 1); break;
                case "QueryDrawingSets": database.QueryDrawingSets(new DrawingSetQuery { Search = "kit" }); break;
                case "InsertDrawingSet": database.InsertDrawingSet(Guid.NewGuid(), Set(members: (drawing, 1))); break;
                case "UpdateDrawingSet": database.UpdateDrawingSet(set.Id, Set("Renamed", set.Id, (drawing, 2)), 1); break;
                case "SetDrawingSetArchived": database.SetDrawingSetArchived(set.Id, true, 1); break;
                case "DrawingSetHistory": database.DrawingSetHistory(set.Id, 0, 100); break;
                case "DrawingSetsContaining": database.DrawingSetsContaining(drawing, 0, 100); break;
                default: throw new InvalidOperationException($"Unknown test operation: {operation}");
            }
        })))
        { IsBackground = true };

        bool observedWaiting;
        bool completedWhileHeld;
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
    public void Concurrency_AllSetSqlStepsOwnTheMonitor()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var connection = (SqliteConnection)typeof(NestDatabase)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var steps = 0;
        var unownedSteps = 0;
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1, _ =>
        {
            steps++;
            if (!Monitor.IsEntered(sync))
                unownedSteps++;
            return 0;
        }, null);
        try
        {
            var set = Set(members: (drawing, 1));
            Action[] operations =
            [
                () => database.InsertDrawingSet(set.Id, set),
                () => database.GetDrawingSet(set.Id),
                () => database.GetDrawingSetRevision(set.Id, 1),
                () => database.QueryDrawingSets(new DrawingSetQuery { Search = "Synthetic", Customer = "synthetic customer" }),
                () => database.UpdateDrawingSet(set.Id, Set("Renamed", set.Id, (drawing, 2)), 1),
                () => database.SetDrawingSetArchived(set.Id, true, 2),
                () => database.SetDrawingSetArchived(set.Id, false, 3),
                () => database.DrawingSetHistory(set.Id, 0, 100),
                () => database.DrawingSetsContaining(drawing, 0, 100),
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

    /// <summary>
    /// A real schema-v6 file with nests and a library drawing: the set and release tables are additive,
    /// so dropping them restores v6 exactly. Returns the drawing's id.
    /// </summary>
    private static Guid CreateVersion6Database(string path)
    {
        Guid drawing;
        using (var database = new NestDatabase(path, pooling: false))
        {
            var nest = new NestRecord { Name = "Synthetic nest", Customer = "Synthetic", Material = "Alloy", Thickness = 0.25 };
            var stored = database.Insert(Guid.NewGuid(), nest, new byte[] { 0, 1, 255 });
            nest.Comments = "Edited";
            database.Update(stored.Id, nest, new byte[] { 2, 3 }, 1);
            database.AllocateNestNumber(2026);
            drawing = AddDrawing(database);
            var edit = database.GetDrawing(drawing)!;
            edit.Notes = "Edited";
            database.UpdateDrawing(drawing, edit, null, 1);
        }

        Execute(path, $"""
            {DropReleaseTables}
            DROP TABLE drawing_set_members; DROP TABLE drawing_sets; DROP TABLE drawing_set_revisions; PRAGMA user_version=6;
            """);
        return drawing;
    }

    // Every pre-v7 table, row by row, so the upgrade is shown to leave them byte-identical.
    private static string Digest(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        var text = new StringBuilder();
        foreach (var table in new[] { "nests", "nest_revisions", "nest_sequences", "drawing_packages", "drawing_revisions", "drawings" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY 1, 2";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                text.Append(table).Append(':');
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    text.Append(reader.GetName(i)).Append('=')
                        .Append(value is byte[] bytes ? Convert.ToHexString(SHA256.HashData(bytes)) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
                        .Append(';');
                }
                text.Append('\n');
            }
        }
        return text.ToString();
    }

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
}
