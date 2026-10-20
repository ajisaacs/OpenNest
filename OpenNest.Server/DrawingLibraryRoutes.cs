using System.Globalization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/drawings</c>: the shared drawing library. Geometry travels as an opaque
/// drawing package; existing-drawing writes follow the nest If-Match contract
/// (428 missing, 400 malformed, 409 stale or wrong state with the current record).
/// </summary>
internal static class DrawingLibraryRoutes
{
    // Metadata is a few kilobytes at most (notes are limited to 4000 characters).
    private const int MaxMetadataBytes = 64 * 1024;

    public static void MapDrawingLibrary(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/drawings/query", (HttpRequest request, NestDatabase db) =>
            DrawingLibraryRequest.TryParseQuery(request.Query, out var query, out var error)
                ? Results.Ok(db.QueryDrawings(query))
                : Results.BadRequest(error));

        app.MapGet("/api/drawings/{id:guid}", (Guid id, NestDatabase db) =>
            db.GetDrawing(id) is { } record ? Results.Ok(record) : Results.NotFound());

        app.MapGet("/api/drawings/{id:guid}/file", (Guid id, NestDatabase db) =>
            db.GetDrawingPackage(id) is { } package
                ? Results.File(package, "application/zip", $"{id}.nest")
                : Results.NotFound());

        app.MapGet("/api/drawings/{id:guid}/revisions", (Guid id, HttpRequest request, NestDatabase db) =>
            ReadHistory(id, request, db));

        app.MapGet("/api/drawings/{id:guid}/revisions/{revision:int}/file", (Guid id, int revision, NestDatabase db) =>
            revision < 1 ? Results.BadRequest("Invalid revision.") :
            db.GetDrawingRevisionPackage(id, revision) is { } package
                ? Results.File(package, "application/zip", $"{id}-r{revision.ToString(CultureInfo.InvariantCulture)}.nest")
                : Results.NotFound());

        // Multipart: "metadata" JSON part + "package" part. The client allocates the id.
        app.MapPost("/api/drawings", async (HttpContext http, NestDatabase db) =>
        {
            var (record, package, error) = await ReadUpload(http);
            if (error is not null) return error;
            if (record!.Id == Guid.Empty)
                return Results.BadRequest("id is required: allocate it before the first upload.");
            try { return Results.Ok(db.InsertDrawing(record.Id, record, package!)); }
            catch (DrawingConflictException conflict) { return Conflict(conflict); }
        });

        app.MapPut("/api/drawings/{id:guid}/file", async (Guid id, HttpContext http, NestDatabase db) =>
        {
            var precondition = IfMatch.Read(http.Request, out var expected);
            if (precondition is not null) return precondition;
            var (record, package, error) = await ReadUpload(http);
            if (error is not null) return error;
            return Update(id, record!, package, expected, db);
        });

        app.MapPut("/api/drawings/{id:guid}/metadata", async (Guid id, HttpRequest request, NestDatabase db) =>
        {
            var precondition = IfMatch.Read(request, out var expected);
            if (precondition is not null) return precondition;
            if (!request.HasJsonContentType())
                return Results.BadRequest("Expected application/json metadata.");
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
            if (json.Length > MaxMetadataBytes)
                return Results.BadRequest("Metadata is too large.");
            if (!DrawingLibraryRequest.TryReadMetadata(json, out var record, out var error))
                return Results.BadRequest(error);
            return Update(id, record, null, expected, db);
        });

        app.MapPost("/api/drawings/{id:guid}/archive", (Guid id, HttpRequest request, NestDatabase db) =>
            SetArchived(id, true, request, db));

        app.MapPost("/api/drawings/{id:guid}/restore", (Guid id, HttpRequest request, NestDatabase db) =>
            SetArchived(id, false, request, db));
    }

    private static IResult Update(Guid id, DrawingLibraryRecord record, byte[]? package, int expected, NestDatabase db)
    {
        if (record.Id != Guid.Empty && record.Id != id)
            return Results.BadRequest("Metadata id does not match the drawing.");
        try
        {
            var updated = db.UpdateDrawing(id, record, package, expected);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (DrawingConflictException conflict) { return Conflict(conflict); }
    }

    private static IResult SetArchived(Guid id, bool archived, HttpRequest request, NestDatabase db)
    {
        var precondition = IfMatch.Read(request, out var expected);
        if (precondition is not null) return precondition;
        try
        {
            var changed = db.SetDrawingArchived(id, archived, expected);
            return changed is null ? Results.NotFound() : Results.Ok(changed);
        }
        catch (DrawingConflictException conflict) { return Conflict(conflict); }
    }

    private static IResult Conflict(DrawingConflictException conflict) =>
        Results.Json(conflict.Current, statusCode: StatusCodes.Status409Conflict);

    private static IResult ReadHistory(Guid id, HttpRequest request, NestDatabase db)
    {
        if (!DrawingLibraryRequest.TryReadPaging(request.Query, out var offset, out var limit, out var error))
            return Results.BadRequest(error);
        return db.DrawingHistory(id, offset, limit) is { } page ? Results.Ok(page) : Results.NotFound();
    }

    // Returns (record, package, error); the package is checked before it is buffered.
    private static async Task<(DrawingLibraryRecord? Record, byte[]? Package, IResult? Error)> ReadUpload(HttpContext http)
    {
        if (!http.Request.HasFormContentType)
            return (null, null, Results.BadRequest("Expected multipart/form-data with metadata and package parts."));

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException || (ex is IOException && ex is not BadHttpRequestException))
        {
            return (null, null, Results.BadRequest("Malformed multipart body."));
        }

        if (!DrawingLibraryRequest.TryReadMetadata(form["metadata"].FirstOrDefault(), out var record, out var error))
            return (null, null, Results.BadRequest(error));

        var packagePart = form.Files["package"];
        if (packagePart is null || packagePart.Length == 0)
            return (null, null, Results.BadRequest("Missing or empty 'package' part."));
        if (packagePart.Length > DrawingLibraryRules.MaxPackageBytes)
            return (null, null, Results.BadRequest(DrawingLibraryRules.PackageTooLargeError));

        using var stream = packagePart.OpenReadStream();
        using var buffer = new MemoryStream((int)packagePart.Length);
        await stream.CopyToAsync(buffer);
        var package = buffer.ToArray();
        var packageError = DrawingLibraryRules.GetPackageError(package);
        return packageError is null ? (record, package, null) : (null, null, Results.BadRequest(packageError));
    }
}
