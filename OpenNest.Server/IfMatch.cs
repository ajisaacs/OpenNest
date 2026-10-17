namespace OpenNest.Server;

/// <summary>The revision precondition every existing-record mutation requires.</summary>
internal static class IfMatch
{
    /// <summary>
    /// Reads one quoted positive revision. Returns 428 when the header is missing and
    /// 400 when it is malformed; null means <paramref name="revision"/> holds the value.
    /// </summary>
    public static IResult? Read(HttpRequest request, out int revision)
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
}
