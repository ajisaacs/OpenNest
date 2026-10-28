using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestMaterialGradeTests
{
    [Fact]
    public void Grade_RoundTripsThroughInsertUpdateAndHistory()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var id = Guid.NewGuid();

        var created = database.Insert(id, Record("Job", "Stainless", "304"), [1]);
        Assert.Equal("304", created.MaterialGrade);

        var updated = database.UpdateMetadata(id, Record("Job", "Stainless", "316"), created.Revision)!;
        Assert.Equal("316", updated.MaterialGrade);
        Assert.Equal(created.Revision + 1, updated.Revision);

        var history = database.History(id, 0, 10)!;
        Assert.Equal(["316", "304"], history.Items.Select(r => r.MaterialGrade));
    }

    [Fact]
    public void GradeOnlyChange_IsNotANoOp()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var id = Guid.NewGuid();
        var created = database.Insert(id, Record("Job", "Stainless", "304"), [1]);

        var unchanged = database.UpdateMetadata(id, Record("Job", "Stainless", "304"), created.Revision)!;
        var changed = database.UpdateMetadata(id, Record("Job", "Stainless", "316"), created.Revision)!;

        Assert.Equal(created.Revision, unchanged.Revision);
        Assert.Equal(created.Revision + 1, changed.Revision);
    }

    [Fact]
    public void Query_SearchesAndSortsByGrade()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        database.Insert(Guid.NewGuid(), Record("A", "Aluminum", "6061"), [1]);
        database.Insert(Guid.NewGuid(), Record("B", "Aluminum", "5052"), [1]);
        database.Insert(Guid.NewGuid(), Record("C", "Stainless", "304"), [1]);

        var found = database.Query(new NestQuery { Search = "5052" });
        Assert.Equal("B", Assert.Single(found.Items).Name);

        var sorted = database.Query(new NestQuery { Sort = NestSortField.MaterialGrade, Descending = false });
        Assert.Equal(["C", "B", "A"], sorted.Items.Select(r => r.Name));
    }

    [Fact]
    public void Version7Database_GainsBlankGradeAndKeepsRecords()
    {
        using var factory = new ServerFactory();
        var id = Guid.NewGuid();
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
            database.Insert(id, Record("Legacy", "Stainless", "304"), [1]);
        Execute(factory.DatabasePath, $"""
            {DrawingSetFixtures.ReleaseTablesBackToV8}
            ALTER TABLE nests DROP COLUMN materialGrade;
            ALTER TABLE nest_revisions DROP COLUMN materialGrade;
            PRAGMA user_version=7;
            """);

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        var record = upgraded.Get(id)!;
        Assert.Equal("Stainless", record.Material);
        Assert.Equal("", record.MaterialGrade);
        Assert.Equal("", Assert.Single(upgraded.History(id, 0, 10)!.Items).MaterialGrade);
        Assert.Equal(14L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
    }

    private static NestRecord Record(string name, string material, string grade) => new()
    {
        Name = name,
        Material = material,
        MaterialGrade = grade,
        Thickness = 0.25,
        DateCreated = new DateTime(2026, 1, 1),
        DateModified = new DateTime(2026, 1, 2),
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
}
