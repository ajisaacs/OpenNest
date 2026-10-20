using System.Text;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/sets</c>: named, versioned lists of library drawings with quantities. Sets
/// travel as JSON; existing-set writes follow the drawing If-Match contract (428 missing,
/// 400 malformed, 409 stale or wrong state with the current record).
/// </summary>
internal static class DrawingSetRoutes
{
    public static void MapDrawingSets(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sets/query", (HttpRequest request, NestDatabase db) =>
            DrawingSetRequest.TryParseQuery(request.Query, out var query, out var error)
                ? Results.Ok(db.QueryDrawingSets(query))
                : Results.BadRequest(error));

        app.MapGet("/api/sets/{id:guid}", (Guid id, NestDatabase db) =>
            db.GetDrawingSet(id) is { } record ? Results.Ok(record) : Results.NotFound());

        app.MapGet("/api/sets/{id:guid}/revisions", (Guid id, HttpRequest request, NestDatabase db) =>
            !DrawingLibraryRequest.TryReadPaging(request.Query, out var offset, out var limit, out var error)
                ? Results.BadRequest(error)
                : db.DrawingSetHistory(id, offset, limit) is { } page ? Results.Ok(page) : Results.NotFound());

        app.MapGet("/api/sets/{id:guid}/revisions/{revision:int}", (Guid id, int revision, NestDatabase db) =>
            revision < 1 ? Results.BadRequest("Invalid revision.") :
            db.GetDrawingSetRevision(id, revision) is { } record ? Results.Ok(record) : Results.NotFound());

        app.MapGet("/api/drawings/{id:guid}/sets", (Guid id, HttpRequest request, NestDatabase db) =>
            !DrawingLibraryRequest.TryReadPaging(request.Query, out var offset, out var limit, out var error)
                ? Results.BadRequest(error)
                : db.DrawingSetsContaining(id, offset, limit) is { } page ? Results.Ok(page) : Results.NotFound());

        // JSON set with members; the client allocates the id.
        app.MapPost("/api/sets", async (HttpRequest request, NestDatabase db) =>
        {
            var (record, error) = await ReadSet(request);
            if (error is not null) return error;
            if (record!.Id == Guid.Empty)
                return Results.BadRequest("id is required: allocate it before the first save.");
            try { return Results.Ok(db.InsertDrawingSet(record.Id, record)); }
            catch (DrawingSetConflictException conflict) { return Conflict(conflict); }
            catch (ArgumentException invalid) { return Results.BadRequest(invalid.Message); }
        });

        app.MapPut("/api/sets/{id:guid}", async (Guid id, HttpRequest request, NestDatabase db) =>
        {
            var precondition = IfMatch.Read(request, out var expected);
            if (precondition is not null) return precondition;
            var (record, error) = await ReadSet(request);
            if (error is not null) return error;
            if (record!.Id != Guid.Empty && record.Id != id)
                return Results.BadRequest("Set id does not match the route.");
            try
            {
                var updated = db.UpdateDrawingSet(id, record, expected);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (DrawingSetConflictException conflict) { return Conflict(conflict); }
            catch (ArgumentException invalid) { return Results.BadRequest(invalid.Message); }
        });

        app.MapPost("/api/sets/{id:guid}/archive", (Guid id, HttpRequest request, NestDatabase db) =>
            SetArchived(id, true, request, db));

        app.MapPost("/api/sets/{id:guid}/restore", (Guid id, HttpRequest request, NestDatabase db) =>
            SetArchived(id, false, request, db));
    }

    private static IResult SetArchived(Guid id, bool archived, HttpRequest request, NestDatabase db)
    {
        var precondition = IfMatch.Read(request, out var expected);
        if (precondition is not null) return precondition;
        try
        {
            var changed = db.SetDrawingSetArchived(id, archived, expected);
            return changed is null ? Results.NotFound() : Results.Ok(changed);
        }
        catch (DrawingSetConflictException conflict) { return Conflict(conflict); }
    }

    private static IResult Conflict(DrawingSetConflictException conflict) =>
        Results.Json(conflict.Current, statusCode: StatusCodes.Status409Conflict);

    private static async Task<(DrawingSetRecord? Record, IResult? Error)> ReadSet(HttpRequest request)
    {
        if (!request.HasJsonContentType())
            return (null, Results.BadRequest("Expected an application/json set."));
        if (request.ContentLength > DrawingSetRules.MaxBodyBytes)
            return (null, Results.BadRequest("The set is too large."));

        // Bounded read: a body without a length, or longer than it claims, stops at the limit.
        using var body = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
        {
            if (body.Length + read > DrawingSetRules.MaxBodyBytes)
                return (null, Results.BadRequest("The set is too large."));
            body.Write(chunk, 0, read);
        }

        var json = Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
        return DrawingSetRequest.TryReadSet(json, out var record, out var error)
            ? (record, null)
            : (null, Results.BadRequest(error));
    }
}
