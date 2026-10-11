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

app.MapGet("/healthz", (NestDatabase db) => db.IsHealthy()
    ? Results.Ok(new { status = "ok" })
    : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

// Full enumeration (backup manifests, restore checks and the container smoke).
app.MapGet("/api/nests", (NestDatabase db) => Results.Ok(db.List()));

// Bounded, SQL-filtered page for browsing.
app.MapGet("/api/nests/query", (HttpRequest request, NestDatabase db) =>
    NestQueryRequest.TryParse(request.Query, out var query, out var error)
        ? Results.Ok(db.Query(query))
        : Results.BadRequest(error));

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
app.MapGet("/api/nests/{id:guid}/revisions", (Guid id, HttpRequest request, NestDatabase db) =>
{
    var offset = 0;
    var limit = 100;
    foreach (var parameter in request.Query)
    {
        if (parameter.Value.Count != 1 || !int.TryParse(parameter.Value[0], out var value))
            return Results.BadRequest("Invalid history pagination.");
        switch (parameter.Key)
        {
            case "offset": offset = value; break;
            case "limit": limit = value; break;
            default: return Results.BadRequest("Unknown history parameter.");
        }
    }
    if (offset < 0 || limit is < 1 or > 200) return Results.BadRequest("Invalid history pagination.");
    return db.History(id, offset, limit) is { } page ? Results.Ok(page) : Results.NotFound();
});
app.MapGet("/api/nests/{id:guid}/revisions/{revision:int}/file", (Guid id, int revision, NestDatabase db) =>
    revision < 1 ? Results.BadRequest("Invalid revision.") :
    db.GetRevisionFile(id, revision) is { } file ? Results.File(file, "application/zip", $"{id}-r{revision}.nest") : Results.NotFound());

app.MapPut("/api/nests/{id:guid}/file", async (Guid id, HttpContext http, NestDatabase db) =>
{
    var precondition = ReadPrecondition(http.Request, out var expected);
    if (precondition is not null) return precondition;
    var (record, file, error) = await ReadMultipart(http);
    if (error is not null) return error;
    try
    {
        var updated = db.Update(id, record!, file!, expected);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    catch (RevisionConflictException conflict)
    {
        return Results.Json(conflict.Current, statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapPut("/api/nests/{id:guid}/metadata", async (Guid id, HttpRequest request, NestDatabase db) =>
{
    var error = ReadPrecondition(request, out var expected);
    if (error is not null) return error;
    NestRecord? record;
    try { record = await request.ReadFromJsonAsync<NestRecord>(); }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
    { return Results.BadRequest("Invalid metadata JSON."); }
    if (record is null) return Results.BadRequest("Missing metadata.");
    try
    {
        var updated = db.UpdateMetadata(id, record, expected);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    catch (RevisionConflictException conflict) { return Results.Json(conflict.Current, statusCode: 409); }
});
app.MapDelete("/api/nests/{id:guid}", (Guid id, HttpRequest request, NestDatabase db) =>
    BlockDeferredMutation(id, request, db));

app.Run();

static IResult? ReadPrecondition(HttpRequest request, out int revision)
{
    revision = 0;
    var values = request.Headers.IfMatch;
    if (values.Count == 0) return Results.Json("Upgrade/reload required: send If-Match with the current revision.", statusCode: 428);
    var text = values.Count == 1 ? values[0] : null;
    if (text is null || text.Length < 3 || text[0] != '"' || text[^1] != '"' ||
        !text[1..^1].All(c => c is >= '0' and <= '9') ||
        !int.TryParse(text[1..^1], out revision) || revision < 1)
        return Results.BadRequest("If-Match must contain one quoted positive integer revision.");
    return null;
}

static IResult BlockDeferredMutation(Guid id, HttpRequest request, NestDatabase db)
{
    var error = ReadPrecondition(request, out var expected);
    if (error is not null) return error;
    var current = db.Get(id);
    if (current is null) return Results.NotFound();
    if (current.Revision != expected) return Results.Json(current, statusCode: 409);
    return Results.Json("Versioned metadata and visibility changes are not available yet; no changes were made.", statusCode: 501);
}

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
