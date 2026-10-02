using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;
using OpenNest.Server;

var builder = WebApplication.CreateBuilder(args);

// Same wire contract as RemoteNestRepository: camelCase, enum-as-string.
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
};
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    foreach (var converter in jsonOptions.Converters)
        options.SerializerOptions.Converters.Add(converter);
});

// Storage path: --database <path> or OPENNEST_DB, default ./data/nests.db (a Docker volume).
var databasePath =
    args.FirstOrDefault(a => a.StartsWith("--database=", StringComparison.Ordinal))?.Split('=', 2)[1]
    ?? Environment.GetEnvironmentVariable("OPENNEST_DB")
    ?? Path.Combine("data", "nests.db");
builder.Services.AddSingleton(_ => new NestDatabase(databasePath));

// Listen port: --urls or ASPNETCORE_URLS; Dockerfile defaults to 8090.
var app = builder.Build();

// Open the database now so an unusable data path fails startup, not the first request.
app.Services.GetRequiredService<NestDatabase>();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/nests", (NestDatabase db) => Results.Ok(db.List()));

app.MapGet("/api/nests/{id:guid}", (Guid id, NestDatabase db) =>
    db.Get(id) is { } record ? Results.Ok(record) : Results.NotFound());

app.MapGet("/api/nests/{id:guid}/file", (Guid id, NestDatabase db) =>
    db.GetFile(id) is { } file ? Results.File(file, "application/zip", $"{id}.nest") : Results.NotFound());

// POST/PUT multipart: "metadata" JSON part + "file" part (the .nest archive).
app.MapPost("/api/nests", async (HttpContext http, NestDatabase db) =>
{
    var (record, file, error) = await ReadMultipart(http);
    if (error is not null)
        return error;

    var id = record!.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
    return Results.Ok(db.Insert(id, record, file!));
});
app.MapPut("/api/nests/{id:guid}/file", async (Guid id, HttpContext http, NestDatabase db) =>
{
    var (record, file, error) = await ReadMultipart(http);
    if (error is not null)
        return error;

    var updated = db.Update(id, record!, file);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPut("/api/nests/{id:guid}/metadata", async (Guid id, HttpContext http, NestDatabase db) =>
{
    NestRecord? record;
    try
    {
        record = await http.Request.ReadFromJsonAsync<NestRecord>();
    }
    catch (JsonException)
    {
        return Results.BadRequest("Invalid metadata JSON.");
    }

    if (record is null)
        return Results.BadRequest("Missing metadata.");

    // Keep the stored archive's size; metadata updates never touch the file.
    var updated = db.Update(id, record, file: null);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/nests/{id:guid}", (Guid id, NestDatabase db) =>
    db.Delete(id) ? Results.NoContent() : Results.NotFound());

app.Run();

// Shared multipart reader for upload endpoints. Returns a (record, file, error) triple.
static async Task<(NestRecord? Record, byte[]? File, IResult? Error)> ReadMultipart(HttpContext http)
{
    if (!http.Request.HasFormContentType)
        return (null, null, Results.BadRequest("Expected multipart/form-data with metadata and file parts."));

    IFormCollection form;
    try
    {
        form = await http.Request.ReadFormAsync();
    }
    catch (Exception ex) when (ex is InvalidDataException || (ex is IOException && ex is not BadHttpRequestException))
    {
        // A truncated or malformed multipart body is a client error. BadHttpRequestException
        // (for example, an oversized body) keeps the status code Kestrel assigns it.
        return (null, null, Results.BadRequest("Malformed multipart body."));
    }

    var metadataPart = form["metadata"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(metadataPart))
        return (null, null, Results.BadRequest("Missing 'metadata' part."));

    NestRecord? record;
    try
    {
        record = JsonSerializer.Deserialize<NestRecord>(metadataPart, MultipartJson.Options);
    }
    catch (JsonException)
    {
        return (null, null, Results.BadRequest("Invalid metadata JSON."));
    }

    if (record is null)
        return (null, null, Results.BadRequest("Missing metadata."));

    var filePart = form.Files["file"];
    if (filePart is null || filePart.Length == 0)
        return (null, null, Results.BadRequest("Missing or empty 'file' part."));

    using var stream = filePart.OpenReadStream();
    using var buffer = new MemoryStream((int)filePart.Length);
    await stream.CopyToAsync(buffer);
    return (record, buffer.ToArray(), null);
}

internal static class MultipartJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

// Entry-point type for in-memory integration tests (WebApplicationFactory<Program>).
public partial class Program { }
