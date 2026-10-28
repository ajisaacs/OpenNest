using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// Nest records carry the archive's units, which their thickness is in, so a list of nests can
/// tell a millimeter nest from an inch one without reading each archive.
/// </summary>
public sealed class NestUnitsTests
{
    [Fact]
    public async Task Upload_TakesUnitsFromTheArchive_NotTheClientsClaim()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var nest = NestApiTests.SyntheticNest();
        nest.Units = Units.Millimeters;
        nest.Thickness = 6.35;

        using var created = await client.PostAsync("/api/nests",
            NestRevisionTests.Body(new NestRecord { Name = "Metric", Units = Units.Inches }, NestApiTests.Archive(nest)));
        var json = await created.Content.ReadAsStringAsync();
        var id = JsonDocument.Parse(json).RootElement.GetProperty("id").GetGuid();

        Assert.Equal("millimeters", JsonDocument.Parse(json).RootElement.GetProperty("units").GetString());
        using var listed = JsonDocument.Parse(await client.GetStringAsync("/api/nests/query"));
        var item = Assert.Single(listed.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("millimeters", item.GetProperty("units").GetString());
        Assert.Equal(6.35, item.GetProperty("thickness").GetDouble());
        using var record = JsonDocument.Parse(await client.GetStringAsync($"/api/nests/{id}"));
        Assert.Equal("millimeters", record.RootElement.GetProperty("units").GetString());
    }

    [Fact]
    public void MetadataOnlySave_KeepsTheArchivesUnits()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var id = Guid.NewGuid();
        var created = database.Insert(id, Record("Metric", Units.Millimeters), [1]);

        var updated = database.UpdateMetadata(id, Record("Metric renamed", Units.Inches), created.Revision)!;

        Assert.Equal(Units.Millimeters, updated.Units);
        Assert.Equal([Units.Millimeters, Units.Millimeters], database.History(id, 0, 10)!.Items.Select(r => r.Units));
    }

    [Fact]
    public void Version11Database_ReadsEachStoredArchivesUnits()
    {
        using var factory = new ServerFactory();
        var metric = Guid.NewGuid();
        var inch = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        var metricNest = NestApiTests.SyntheticNest();
        metricNest.Units = Units.Millimeters;
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            var created = database.Insert(metric, Record("Metric", Units.Inches), NestApiTests.Archive(metricNest));
            database.UpdateMetadata(metric, Record("Metric renamed", Units.Inches), created.Revision);
            database.Insert(inch, Record("Inch", Units.Inches), NestApiTests.Archive(NestApiTests.SyntheticNest()));
            database.Insert(unreadable, Record("Broken", Units.Inches), [1]);
        }
        Execute(factory.DatabasePath, """
            ALTER TABLE nests DROP COLUMN units;
            ALTER TABLE nest_revisions DROP COLUMN units;
            PRAGMA user_version=11;
            """);

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Equal(Units.Millimeters, upgraded.Get(metric)!.Units);
        Assert.Equal([Units.Millimeters, Units.Millimeters], upgraded.History(metric, 0, 10)!.Items.Select(r => r.Units));
        Assert.Equal(Units.Inches, upgraded.Get(inch)!.Units);
        Assert.Equal(Units.Inches, upgraded.Get(unreadable)!.Units);
        Assert.Equal(14L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
    }

    private static NestRecord Record(string name, Units units) => new()
    {
        Name = name,
        Material = "Steel",
        Thickness = 0.25,
        Units = units,
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
