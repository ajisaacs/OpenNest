namespace OpenNest.Data;

/// <summary>
/// Bounds shared by the drawing set client and server. The client checks them before
/// sending; the server refuses anything outside them with no state change. Whether each
/// member's drawing exists is checked by the server.
/// </summary>
public static class DrawingSetRules
{
    public const int MaxMembers = 500;

    public const int MaxQuantity = 100_000;

    /// <summary>Largest accepted set JSON body (1 MiB): room for members echoed with their drawings.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    /// <summary>The first rule <paramref name="record"/> breaks, or null when it may be saved.</summary>
    public static string? GetError(DrawingSetRecord? record)
    {
        if (record is null)
            return "Missing set.";
        if (string.IsNullOrWhiteSpace(record.Name))
            return "name is required.";
        var error = DrawingLibraryRules.TextError("name", record.Name, DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("customer", record.Customer, DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("savedBy", record.SavedBy, DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("notes", record.Notes, DrawingLibraryRules.MaxNotesLength);
        if (error is not null)
            return error;
        if (record.Members is null)
            return "members is required.";
        if (record.Members.Count > MaxMembers)
            return $"A set may have at most {MaxMembers} members.";

        var seen = new HashSet<Guid>();
        for (var i = 0; i < record.Members.Count; i++)
        {
            var member = record.Members[i];
            if (member is null)
                return $"members[{i}] is required.";
            if (member.DrawingId == Guid.Empty)
                return $"members[{i}].drawingId is required.";
            if (member.Quantity < 1 || member.Quantity > MaxQuantity)
                return $"members[{i}].quantity must be between 1 and {MaxQuantity}.";
            if (!seen.Add(member.DrawingId))
                return $"members[{i}] repeats drawing {member.DrawingId}: use one member with a larger quantity.";
        }

        return null;
    }
}
