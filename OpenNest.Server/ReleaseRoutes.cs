using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/releases</c>: append-only records of drawing set releases into nests, and their
/// undo. A release is posted after every nest it names is uploaded; its id is allocated by
/// the client, so a post whose reply was lost can be sent again (409 with the stored release).
/// </summary>
internal static class ReleaseRoutes
{
    public static void MapReleases(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/releases", async (HttpRequest request, NestDatabase db) =>
        {
            var (json, error) = await DrawingSetRoutes.ReadJson(request, "release");
            if (error is not null) return error;
            if (!ReleaseRequest.TryReadRelease(json, out var release, out var invalid))
                return Results.BadRequest(invalid);
            if (release.Id == Guid.Empty)
                return Results.BadRequest("id is required: allocate it before posting the release.");
            try { return Results.Ok(db.InsertRelease(release)); }
            catch (SetReleaseConflictException conflict)
            {
                return Results.Json(conflict.Current, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ArgumentException rejected) { return Results.BadRequest(rejected.Message); }
        });

        app.MapGet("/api/releases/query", (HttpRequest request, NestDatabase db) =>
            ReleaseRequest.TryParseQuery(request.Query, out var setId, out var nestId, out var offset, out var limit, out var error)
                ? Results.Ok(db.QueryReleases(setId, nestId, offset, limit))
                : Results.BadRequest(error));

        app.MapGet("/api/releases/{id:guid}", (Guid id, NestDatabase db) =>
            db.GetRelease(id) is { } release ? Results.Ok(release) : Results.NotFound());

        app.MapPost("/api/releases/{id:guid}/undo", async (Guid id, HttpRequest request, NestDatabase db) =>
        {
            var (json, error) = await DrawingSetRoutes.ReadJson(request, "undo request");
            if (error is not null) return error;
            if (!ReleaseRequest.TryReadUndo(json, out var undo, out var invalid))
                return Results.BadRequest(invalid);
            try
            {
                var undone = db.UndoRelease(id, undo);
                return undone is null ? Results.NotFound() : Results.Ok(undone);
            }
            catch (ReleaseUndoRefusedException conflict)
            {
                return Results.Json(conflict.Conflict, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ArgumentException rejected) { return Results.BadRequest(rejected.Message); }
        });
    }
}
