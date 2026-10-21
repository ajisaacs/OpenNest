using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;

namespace OpenNest.Server.Tests;

/// <summary>Neutral, code-built drawing set fixtures for the server tests.</summary>
internal static class DrawingSetFixtures
{
    public static DrawingSetRecord Set(string name = "Synthetic kit", Guid? id = null, params (Guid Drawing, int Quantity)[] members) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Customer = "Synthetic customer",
        Notes = "Synthetic set notes",
        SavedBy = "Synthetic operator",
        Members = members.Select(m => new DrawingSetMember { DrawingId = m.Drawing, Quantity = m.Quantity }).ToList(),
    };

    /// <summary>Stores a library drawing directly and returns its id.</summary>
    public static Guid AddDrawing(NestDatabase database, string name = "Synthetic bracket")
    {
        var record = Record(name);
        database.InsertDrawing(record.Id, record, Package(name));
        return record.Id;
    }

    public static async Task<Guid> AddDrawing(HttpClient client, string name = "Synthetic bracket", Action<DrawingLibraryRecord>? edit = null)
    {
        var record = Record(name);
        edit?.Invoke(record);
        return (await Upload(client, record, Package(name))).Id;
    }

    /// <summary>Drops the v9 release tables, for fixtures that rebuild a schema older than v7.</summary>
    public const string DropReleaseTables = """
        DROP TABLE release_nest_items; DROP TABLE release_nests; DROP TABLE release_sets; DROP TABLE releases;
        """;

    /// <summary>Puts back the v7/v8 release tables in place of the v9 ones, for fixtures that rebuild v7 or v8.</summary>
    public const string ReleaseTablesBackToV8 = DropReleaseTables + """
        CREATE TABLE set_releases (
            id          TEXT NOT NULL PRIMARY KEY,
            setId       TEXT NOT NULL,
            setRevision INTEGER NOT NULL,
            multiplier  INTEGER NOT NULL,
            notes       TEXT NOT NULL DEFAULT '',
            releasedAt  TEXT NOT NULL,
            releasedBy  TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (setId, setRevision) REFERENCES drawing_set_revisions(id, revision));
        CREATE TABLE set_release_nests (
            releaseId     TEXT NOT NULL REFERENCES set_releases(id),
            position      INTEGER NOT NULL,
            nestId        TEXT NOT NULL REFERENCES nests(id),
            materialName  TEXT NOT NULL DEFAULT '',
            materialGrade TEXT NOT NULL DEFAULT '',
            thickness     REAL NOT NULL,
            units         TEXT NOT NULL,
            cutType       TEXT NOT NULL,
            PRIMARY KEY (releaseId, position));
        CREATE INDEX ix_set_releases_set ON set_releases(setId, releasedAt DESC);
        CREATE UNIQUE INDEX ix_set_release_nests_nest ON set_release_nests(nestId);
        """;

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static StringContent Json(DrawingSetRecord record) => Json(JsonSerializer.Serialize(record, Options));

    public static async Task<DrawingSetRecord> Create(HttpClient client, DrawingSetRecord record)
    {
        using var response = await client.PostAsync("/api/sets", Json(record));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DrawingSetRecord>(Options))!;
    }

    public static async Task<DrawingSetRecord> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<DrawingSetRecord>(Options))!;

    public static IEnumerable<(Guid, int)> MembersOf(DrawingSetRecord record) =>
        record.Members.Select(m => (m.DrawingId, m.Quantity));

    /// <summary>Stores a draft nest directly and returns its id.</summary>
    public static Guid AddNest(NestDatabase database, string name = "Synthetic nest") =>
        database.Insert(Guid.NewGuid(), new NestRecord { Name = name, Status = NestStatus.Draft }, new byte[] { 1, 2, 3 }).Id;

    public static async Task<Guid> AddNest(HttpClient client, string name = "Synthetic nest", Guid? id = null)
    {
        var record = new NestRecord { Id = id ?? Guid.NewGuid(), Name = name, Status = NestStatus.Draft };
        using var body = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(record, Options), Encoding.UTF8, "application/json"), "metadata" },
            { new ByteArrayContent(TestNests.Archive(1, 2, 3)), "file", "synthetic.nest" },
        };
        using var response = await client.PostAsync("/api/nests", body);
        response.EnsureSuccessStatusCode();
        return record.Id;
    }

    /// <summary>
    /// Two kits of <paramref name="setRevision"/> of <paramref name="setId"/>, creating one nest
    /// per id (each at revision 1, as <see cref="AddNest(NestDatabase, string)"/> stores it), each
    /// in its own material.
    /// </summary>
    public static SetReleaseRecord Release(Guid setId, int setRevision, params Guid[] nests) => new()
    {
        Id = Guid.NewGuid(),
        Sets = new[] { new SetReleaseLine { SetId = setId, SetRevision = setRevision, Quantity = 2 } },
        Notes = "Synthetic release",
        ReleasedBy = "Synthetic operator",
        Nests = nests.Select((nest, i) => new SetReleaseNest
        {
            NestId = nest,
            Kind = SetReleaseNestKind.Created,
            RevisionAfter = 1,
            MaterialName = "Synthetic Steel " + i,
            MaterialGrade = "Grade A",
            Thickness = 0.25,
            Units = Units.Inches,
            CutType = CutType.Laser,
        }).ToList(),
    };

    /// <summary>Turns nest <paramref name="index"/> of <paramref name="release"/> into a merge from one revision to another.</summary>
    public static SetReleaseRecord Merged(this SetReleaseRecord release, int index, int before, int after)
    {
        release.Nests[index].Kind = SetReleaseNestKind.Merged;
        release.Nests[index].RevisionBefore = before;
        release.Nests[index].RevisionAfter = after;
        return release;
    }

    /// <summary>Saves the nest again with new bytes, as an operator or a merge would, and returns the new revision.</summary>
    public static int Resave(NestDatabase database, Guid nest, byte last = 9)
    {
        var current = database.Get(nest)!;
        return database.Update(nest, current, new byte[] { 1, 2, last }, current.Revision)!.Revision;
    }
}
