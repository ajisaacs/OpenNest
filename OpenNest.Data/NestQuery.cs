namespace OpenNest.Data;

/// <summary>
/// One bounded page request for browsing saved nests (<c>GET /api/nests/query</c>).
/// The server filters and pages in SQL; the client and server share these bounds.
/// </summary>
public sealed class NestQuery
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int MaxSearchLength = 200;

    /// <summary>
    /// Case-insensitive substring matched against name, customer, material, made by,
    /// comments and status (stored and display names). Blank means no filter.
    /// </summary>
    public string Search { get; init; } = "";

    /// <summary>Column to order by; ties are broken by id in the same direction.</summary>
    public NestSortField Sort { get; init; } = NestSortField.SavedAt;

    /// <summary>Largest/newest/Z first when true (the default, newest saved first).</summary>
    public bool Descending { get; init; } = true;

    /// <summary>Number of matching records to skip, zero or greater.</summary>
    public int Offset { get; init; }

    /// <summary>Maximum records returned, 1 to <see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>The search text as matched: trimmed, never null.</summary>
    public string NormalizedSearch => (Search ?? "").Trim();

    /// <summary>The first violated bound, or null when the query may be sent.</summary>
    public string? GetValidationError()
    {
        if (!Enum.IsDefined(Sort))
            return "sort must be a supported column.";
        if (Offset < 0)
            return "offset must be zero or greater.";
        if (Limit < 1 || Limit > MaxLimit)
            return $"limit must be between 1 and {MaxLimit}.";
        if (NormalizedSearch.Length > MaxSearchLength)
            return $"search must be at most {MaxSearchLength} characters.";
        return null;
    }
}
