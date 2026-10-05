namespace OpenNest.Data;

/// <summary>
/// Normalized matching key for names in company lists (customers, materials).
/// </summary>
public static class SharedListNames
{
    /// <summary>
    /// Returns the name trimmed, with internal whitespace runs collapsed to one
    /// space, in invariant upper case. Names that differ only in spacing or case
    /// share a key. A null or blank name has the empty key.
    /// </summary>
    public static string Key(string? name) =>
        name == null
            ? ""
            : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
}
