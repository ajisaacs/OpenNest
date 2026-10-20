namespace OpenNest.Data;

/// <summary>
/// One bounded page request for browsing drawing sets (<c>GET /api/sets/query</c>).
/// The server filters and pages in SQL; the client and server share these bounds.
/// </summary>
public sealed class DrawingSetQuery
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int MaxSearchLength = 200;

    /// <summary>
    /// Case-insensitive (ASCII) substring of name, customer or notes. Blank means no filter.
    /// </summary>
    public string Search { get; init; } = "";

    /// <summary>Exact customer, compared by <see cref="SharedListNames.Key"/>. Blank means any.</summary>
    public string Customer { get; init; } = "";

    public bool IncludeArchived { get; init; }

    /// <summary>Column to order by; ties are broken by id in the same direction.</summary>
    public DrawingSetSortField Sort { get; init; } = DrawingSetSortField.SavedAt;

    /// <summary>Largest/newest/Z first when true (the default, newest saved first).</summary>
    public bool Descending { get; init; } = true;

    public int Offset { get; init; }

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
        return DrawingLibraryRules.TextError("search", NormalizedSearch, MaxSearchLength)
            ?? DrawingLibraryRules.TextError("customer", Customer ?? "", DrawingLibraryRules.MaxTextLength);
    }
}
