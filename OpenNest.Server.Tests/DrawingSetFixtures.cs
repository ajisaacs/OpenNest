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
            { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "synthetic.nest" },
        };
        using var response = await client.PostAsync("/api/nests", body);
        response.EnsureSuccessStatusCode();
        return record.Id;
    }

    /// <summary>A release of <paramref name="setRevision"/> making one nest per id, each in its own material.</summary>
    public static SetReleaseRecord Release(int setRevision, params Guid[] nests) => new()
    {
        Id = Guid.NewGuid(),
        SetRevision = setRevision,
        Multiplier = 2,
        Notes = "Synthetic release",
        ReleasedBy = "Synthetic operator",
        Nests = nests.Select((nest, i) => new SetReleaseNest
        {
            NestId = nest,
            MaterialName = "Synthetic Steel " + i,
            MaterialGrade = "Grade A",
            Thickness = 0.25,
            Units = Units.Inches,
            CutType = CutType.Laser,
        }).ToList(),
    };
}
