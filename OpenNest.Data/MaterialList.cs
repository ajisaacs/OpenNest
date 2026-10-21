namespace OpenNest.Data;

/// <summary>
/// Wire shape of one material in the nest server's company material list, with its
/// grades. Names keep the spelling first added; matching uses <see cref="SharedListNames.Key"/>.
/// </summary>
public sealed class MaterialListEntry
{
    public string Name { get; set; } = "";
    public List<string> Grades { get; set; } = new();
}

/// <summary>
/// Body of an add to the company material list: a material and, optionally, one of its
/// grades. Adding a name or grade that is already listed (by key) changes nothing.
/// </summary>
public sealed class MaterialListAddition
{
    public string? Name { get; set; }
    public string? Grade { get; set; }
}

public static class MaterialListRules
{
    /// <summary>The first rule <paramref name="addition"/> breaks, or null when it may be added.</summary>
    public static string? GetError(MaterialListAddition? addition)
    {
        if (addition is null)
            return "Missing material.";
        if (SharedListNames.Key(addition.Name).Length == 0)
            return "name is required.";
        return DrawingLibraryRules.TextError("name", addition.Name, DrawingLibraryRules.MaxTextLength)
            ?? (addition.Grade is null ? null
                : DrawingLibraryRules.TextError("grade", addition.Grade, DrawingLibraryRules.MaxTextLength));
    }

    /// <summary>The name as listed: trimmed, with internal whitespace runs collapsed to one space.</summary>
    public static string Spelling(string? name) =>
        name == null ? "" : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
