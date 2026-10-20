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
}
