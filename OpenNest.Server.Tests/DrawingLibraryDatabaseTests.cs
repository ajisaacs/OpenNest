using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

public sealed class DrawingLibraryDatabaseTests
{
    [Fact]
    public void V3Upgrade_KeepsNestRowsByteIdenticalAndIsIdempotent()
    {
        using var factory = new ServerFactory();
        CreateVersion3Database(factory.DatabasePath);
        var before = NestDigest(factory.DatabasePath);

        for (var open = 0; open < 2; open++)
        {
            using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
                Assert.Empty(database.QueryDrawings(new DrawingLibraryQuery { IncludeArchived = true }).Items);
            Assert.Equal(before, NestDigest(factory.DatabasePath));
            Assert.Equal(10L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        }

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);
        var record = Record();
        upgraded.InsertDrawing(record.Id, record, Package());
        Assert.Equal(1, upgraded.GetDrawing(record.Id)!.Revision);
        Assert.Equal(2, upgraded.List().Count);
    }

    [Fact]
    public void V4Upgrade_KeepsEveryRevisionAndStoresEachPackageOnce()
    {
        using var factory = new ServerFactory();
        CreateVersion4Database(factory.DatabasePath);
        var before = NestDigest(factory.DatabasePath);
        var bracket = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var plate = Guid.Parse("00000000-0000-0000-0000-00000000000b");

        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            Assert.Equal(10L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
            Assert.Equal(before, NestDigest(factory.DatabasePath));
            // Two distinct blobs across four revisions: the bracket's notes edit and the
            // plate both reuse the bracket's first package.
            Assert.Equal(2L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_packages"));
            Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%_v4'"));

            var current = database.GetDrawing(bracket)!;
            Assert.Equal((3, "Synthetic bracket", "Edited", Sha256(V4PackageB)),
                (current.Revision, current.Name, current.Notes, current.PackageSha256));
            Assert.Equal(new DrawingNestingSettings(), current.Settings);
            var history = database.DrawingHistory(bracket, 0, 100)!;
            Assert.Equal(new[] { 3, 2, 1 }, history.Items.Select(r => r.Revision));
            Assert.Equal(new[] { "Edited", "Edited", "Original" }, history.Items.Select(r => r.Notes));
            Assert.Equal(V4PackageA, database.GetDrawingRevisionPackage(bracket, 1));
            Assert.Equal(V4PackageA, database.GetDrawingRevisionPackage(bracket, 2));
            Assert.Equal(V4PackageB, database.GetDrawingPackage(bracket));
            Assert.True(database.GetDrawing(plate)!.Archived);
            Assert.Equal(V4PackageA, database.GetDrawingPackage(plate));
            Assert.Single(database.QueryDrawings(new DrawingLibraryQuery { Customer = " synthetic  customer " }).Items);

            // The upgraded drawing keeps working: a settings-only revision adds no blob.
            var edit = database.GetDrawing(bracket)!;
            edit.Settings = new DrawingNestingSettings { CutFirst = true };
            Assert.Equal(4, database.UpdateDrawing(bracket, edit, null, 3)!.Revision);
            Assert.Equal(2L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM drawing_packages"));
        }

        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.True(reopened.GetDrawing(bracket)!.Settings.CutFirst);
    }

    [Fact]
    public void NewerSchema_IsRefused()
    {
        using var factory = new ServerFactory();
        using (new NestDatabase(factory.DatabasePath, pooling: false)) { }
        Execute(factory.DatabasePath, "PRAGMA user_version=11");

        Assert.Throws<InvalidOperationException>(() => new NestDatabase(factory.DatabasePath, pooling: false));
    }

    [Fact]
    public void FailedDrawingMigration_RollsBackAndCanRestart()
    {
        using var factory = new ServerFactory();
        CreateVersion3Database(factory.DatabasePath);
        var before = NestDigest(factory.DatabasePath);
        // A table holding an index's name makes the drawing schema fail part-way through.
        Execute(factory.DatabasePath, "CREATE TABLE ix_drawings_savedAt (fixture TEXT)");

        Assert.Throws<SqliteException>(() => new NestDatabase(factory.DatabasePath, pooling: false));

        Assert.Equal(3L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name='drawings'"));
        Assert.Equal(before, NestDigest(factory.DatabasePath));
        Execute(factory.DatabasePath, "DROP TABLE ix_drawings_savedAt");
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(2, database.List().Count);
    }

    [Fact]
    public void DirectWrites_EnforceTheSharedRules()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var record = Record();

        Assert.Throws<ArgumentException>(() => database.InsertDrawing(Guid.Empty, record, Package()));
        Assert.Throws<ArgumentException>(() => database.InsertDrawing(record.Id, record, new byte[] { 1, 2, 3, 4 }));
        record.CutType = (CutType)7;
        Assert.Throws<ArgumentException>(() => database.InsertDrawing(record.Id, record, Package()));
        Assert.Throws<ArgumentException>(() => database.QueryDrawings(new DrawingLibraryQuery { Limit = 501 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => database.DrawingHistory(record.Id, 0, 201));
        Assert.Empty(database.QueryDrawings(new DrawingLibraryQuery { IncludeArchived = true }).Items);
    }

    // Code-level ownership gate, as for the nest operations: hold the database's monitor
    // and observe a dedicated synchronous caller waiting on it.
    [Theory]
    [InlineData("GetDrawing")]
    [InlineData("GetDrawingPackage")]
    [InlineData("QueryDrawings")]
    [InlineData("InsertDrawing")]
    [InlineData("UpdateDrawing")]
    [InlineData("UpdateDrawingMetadata")]
    [InlineData("SetDrawingArchived")]
    [InlineData("DrawingHistory")]
    [InlineData("GetDrawingRevisionPackage")]
    public async Task Concurrency_EveryDrawingOperationWaitsForTheSameMonitor(string operation)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var record = Record();
        database.InsertDrawing(record.Id, record, Package());
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => completed.SetResult(Xunit.Record.Exception(() =>
        {
            switch (operation)
            {
                case "GetDrawing": database.GetDrawing(record.Id); break;
                case "GetDrawingPackage": database.GetDrawingPackage(record.Id); break;
                case "QueryDrawings": database.QueryDrawings(new DrawingLibraryQuery { Search = "bracket" }); break;
                case "InsertDrawing": database.InsertDrawing(Guid.NewGuid(), record, Package("other")); break;
                case "UpdateDrawing": database.UpdateDrawing(record.Id, record, Package("changed"), 1); break;
                case "UpdateDrawingMetadata": database.UpdateDrawing(record.Id, Record("Renamed", record.Id), null, 1); break;
                case "SetDrawingArchived": database.SetDrawingArchived(record.Id, true, 1); break;
                case "DrawingHistory": database.DrawingHistory(record.Id, 0, 100); break;
                case "GetDrawingRevisionPackage": database.GetDrawingRevisionPackage(record.Id, 1); break;
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
    public void Concurrency_AllDrawingSqlStepsOwnTheMonitor()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
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
            var record = Record();
            Action[] operations =
            [
                () => database.InsertDrawing(record.Id, record, Package()),
                () => database.GetDrawing(record.Id),
                () => database.GetDrawingPackage(record.Id),
                () => database.QueryDrawings(new DrawingLibraryQuery { Search = "Synthetic", CutType = CutType.Laser, Thickness = 0.25 }),
                () => database.UpdateDrawing(record.Id, record, Package("changed"), 1),
                () => database.UpdateDrawing(record.Id, Record("Renamed", record.Id), null, 2),
                () => database.SetDrawingArchived(record.Id, true, 3),
                () => database.SetDrawingArchived(record.Id, false, 4),
                () => database.DrawingHistory(record.Id, 0, 100),
                () => database.GetDrawingRevisionPackage(record.Id, 1),
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

    /// <summary>A real schema-v3 file: the drawing and set tables are additive, so dropping them restores v3 exactly.</summary>
    private static void CreateVersion3Database(string path)
    {
        using (var database = new NestDatabase(path, pooling: false))
        {
            var first = new NestRecord { Name = "Synthetic nest A", Customer = "Synthetic", Material = "Alloy", Thickness = 0.25 };
            var second = new NestRecord { Name = "Synthetic nest B", Comments = "Notes" };
            var a = database.Insert(Guid.NewGuid(), first, new byte[] { 0, 1, 255 });
            database.Insert(Guid.NewGuid(), second, new byte[] { 7, 8, 9 });
            first.Comments = "Edited";
            database.Update(a.Id, first, new byte[] { 2, 3 }, 1);
            database.SetDeleted(a.Id, true, 2);
            database.SetDeleted(a.Id, false, 3);
        }

        Execute(path, $"""
            {DropReleaseTables}
            DROP TABLE drawing_set_members; DROP TABLE drawing_sets; DROP TABLE drawing_set_revisions;
            DROP TABLE drawing_revisions; DROP TABLE drawings; DROP TABLE drawing_packages; PRAGMA user_version=3;
            """);
    }

    private static readonly byte[] V4PackageA = Package("v4 original");
    private static readonly byte[] V4PackageB = Package("v4 changed");

    /// <summary>
    /// A schema-v4 file in the v4 layout: the same columns in both drawing tables and the
    /// package blob copied into every revision. The bracket has a package, a notes edit
    /// and a new package; the archived plate shares the bracket's first package.
    /// </summary>
    private static void CreateVersion4Database(string path)
    {
        CreateVersion3Database(path);
        const string columns = """
            id TEXT NOT NULL, revision INTEGER NOT NULL, name TEXT NOT NULL, customer TEXT NOT NULL DEFAULT '',
            nameKey TEXT NOT NULL, customerKey TEXT NOT NULL DEFAULT '', materialName TEXT NOT NULL DEFAULT '',
            materialKey TEXT NOT NULL DEFAULT '', materialGrade TEXT NOT NULL DEFAULT '', gradeKey TEXT NOT NULL DEFAULT '',
            thickness REAL NOT NULL, units TEXT NOT NULL, cutType TEXT NOT NULL, notes TEXT NOT NULL DEFAULT '',
            archived INTEGER NOT NULL DEFAULT 0, packageSha256 TEXT NOT NULL, packageSize INTEGER NOT NULL,
            savedAt TEXT NOT NULL, savedBy TEXT NOT NULL DEFAULT '', package BLOB NOT NULL
            """;
        Execute(path, $"""
            CREATE TABLE drawings ({columns}, PRIMARY KEY (id));
            CREATE TABLE drawing_revisions ({columns});
            CREATE UNIQUE INDEX ix_drawing_revisions_key ON drawing_revisions(id, revision);
            CREATE INDEX ix_drawings_customer_name ON drawings(customerKey, nameKey);
            CREATE INDEX ix_drawings_material ON drawings(materialKey);
            CREATE INDEX ix_drawings_thickness ON drawings(thickness);
            CREATE INDEX ix_drawings_savedAt ON drawings(savedAt DESC);
            PRAGMA user_version=4;
            """);

        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        void Revision(string id, int revision, string name, string notes, bool archived, byte[] package)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO drawing_revisions VALUES ($id, $revision, $name, 'Synthetic customer', $nameKey,
                    'SYNTHETIC CUSTOMER', 'Synthetic Steel', 'SYNTHETIC STEEL', 'Grade A', 'GRADE A', 0.25, 'Inches',
                    'Laser', $notes, $archived, $sha, $size, '2026-01-01T00:00:00.0000000', 'Synthetic operator', $package);
                INSERT OR REPLACE INTO drawings SELECT * FROM drawing_revisions WHERE id=$id AND revision=$revision;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$revision", revision);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$nameKey", SharedListNames.Key(name));
            command.Parameters.AddWithValue("$notes", notes);
            command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
            command.Parameters.AddWithValue("$sha", Sha256(package));
            command.Parameters.AddWithValue("$size", package.LongLength);
            command.Parameters.AddWithValue("$package", package);
            command.ExecuteNonQuery();
        }

        Revision("00000000-0000-0000-0000-00000000000a", 1, "Synthetic bracket", "Original", false, V4PackageA);
        Revision("00000000-0000-0000-0000-00000000000a", 2, "Synthetic bracket", "Edited", false, V4PackageA);
        Revision("00000000-0000-0000-0000-00000000000a", 3, "Synthetic bracket", "Edited", false, V4PackageB);
        Revision("00000000-0000-0000-0000-00000000000b", 1, "Synthetic plate", "", true, V4PackageA);
    }

    private static string NestDigest(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        var text = new StringBuilder();
        foreach (var sql in new[] { "SELECT * FROM nests ORDER BY id", "SELECT * FROM nest_revisions ORDER BY id, revision" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
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
