using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/settings</c>: the settings every workstation shares (machines, post settings, clamps
/// and nest defaults), one JSON document per kind and key. Creating returns 201, or 200 when the
/// same document is already stored; changes and deletes follow the If-Match contract (428
/// missing, 400 malformed, 409 stale with the current document).
/// </summary>
internal static class SettingRoutes
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", (NestDatabase db) => Results.Ok(db.ListSharedSettings()));

        app.MapGet("/api/settings/{kind}/{key}", (string kind, string key, NestDatabase db) =>
            !SharedSettingKinds.IsKnown(kind) ? Results.BadRequest($"Unknown settings kind '{kind}'.") :
            db.GetSharedSetting(kind, key) is { } record ? Results.Ok(record) : Results.NotFound());

        app.MapPost("/api/settings/{kind}/{key}", async (string kind, string key, HttpRequest request, NestDatabase db) =>
        {
            var (write, error) = await ReadWrite(request, kind, key);
            if (error is not null) return error;
            try
            {
                var (record, created) = db.CreateSharedSetting(kind, key, write!);
                return created ? Results.Created($"/api/settings/{kind}/{record.Key}", record) : Results.Ok(record);
            }
            catch (SharedSettingConflictException conflict) { return Conflict(conflict); }
        });

        app.MapPut("/api/settings/{kind}/{key}", async (string kind, string key, HttpRequest request, NestDatabase db) =>
        {
            var precondition = IfMatch.Read(request, out var expected);
            if (precondition is not null) return precondition;
            var (write, error) = await ReadWrite(request, kind, key);
            if (error is not null) return error;
            try
            {
                var updated = db.UpdateSharedSetting(kind, key, write!, expected);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (SharedSettingConflictException conflict) { return Conflict(conflict); }
        });

        app.MapDelete("/api/settings/{kind}/{key}", (string kind, string key, string? savedBy, HttpRequest request, NestDatabase db) =>
        {
            var precondition = IfMatch.Read(request, out var expected);
            if (precondition is not null) return precondition;
            if (SharedSettingRules.NormalizeKey(kind, key) is null)
                return Results.BadRequest($"Invalid {kind} settings key.");
            if ((savedBy ?? "").Length > SharedSettingRules.MaxSavedByLength)
                return Results.BadRequest("savedBy is too long.");
            try
            {
                return db.DeleteSharedSetting(kind, key, savedBy ?? "", expected) ? Results.NoContent() : Results.NotFound();
            }
            catch (SharedSettingConflictException conflict) { return Conflict(conflict); }
        });
    }

    private static IResult Conflict(SharedSettingConflictException conflict) =>
        Results.Json(conflict.Current, statusCode: StatusCodes.Status409Conflict);

    private static async Task<(SharedSettingWrite? Write, IResult? Error)> ReadWrite(HttpRequest request, string kind, string key)
    {
        var (json, error) = await DrawingSetRoutes.ReadJson(request, "settings document");
        if (error is not null) return (null, error);

        SharedSettingWrite? write;
        try { write = JsonSerializer.Deserialize<SharedSettingWrite>(json!, WriteOptions); }
        catch (JsonException) { return (null, Results.BadRequest("Invalid settings JSON.")); }
        return SharedSettingRules.GetError(kind, key, write) is { } invalid
            ? (null, Results.BadRequest(invalid))
            : (write, null);
    }
}
