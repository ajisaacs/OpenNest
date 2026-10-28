namespace OpenNest.Server;

/// <summary>
/// <c>/updates/{channel}/</c>: the desktop app's Velopack feed, mirrored from GitHub by
/// <see cref="UpdateMirror"/>. Velopack's <c>SimpleWebSource</c> reads
/// <c>releases.win.json</c> (ignoring the query it appends) and then the package it names. A
/// channel with nothing mirrored yet answers 503 so clients fall back to GitHub for that check.
/// </summary>
internal static class UpdateRoutes
{
    public static void MapUpdates(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/updates", (UpdateMirror mirror) => Results.Ok(mirror.GetStatus()));

        app.MapGet("/updates/{channel}/{file}", (string channel, string file, UpdateMirror mirror) =>
        {
            if (!UpdateMirror.IsChannel(channel))
                return Results.NotFound();

            if (file == UpdateMirror.FeedFileName)
            {
                return mirror.GetFeed(channel) is { } feed
                    ? Results.Bytes(feed, "application/json")
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return mirror.GetPackagePath(channel, file) is { } path
                ? Results.File(path, "application/octet-stream", enableRangeProcessing: true)
                : Results.NotFound();
        });
    }
}
