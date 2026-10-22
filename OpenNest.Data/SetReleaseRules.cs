using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Bounds shared by the set release client and server. Whether the set revisions, drawings,
/// nests and nest revisions exist, and whether a created nest already belongs to a release, is
/// checked by the server.
/// </summary>
public static class SetReleaseRules
{
    /// <summary>Most kits of one set in one release.</summary>
    public const int MaxQuantity = 1000;

    /// <summary>Most sets in one release.</summary>
    public const int MaxSetLines = 50;

    /// <summary>Most drawings released on their own in one release, as many as one set may list.</summary>
    public const int MaxDrawingLines = DrawingSetRules.MaxMembers;

    /// <summary>Most of one drawing released on its own, as for one set member.</summary>
    public const int MaxDrawingQuantity = DrawingSetRules.MaxQuantity;

    /// <summary>One nest per material group.</summary>
    public const int MaxNests = DrawingSetRules.MaxMembers;

    /// <summary>Most drawing quantities recorded against one nest: every member of every set, and every drawing on its own.</summary>
    public const int MaxItemsPerNest = DrawingSetRules.MaxMembers * MaxSetLines + MaxDrawingLines;

    /// <summary>The first rule <paramref name="release"/> breaks, or null when it may be posted.</summary>
    public static string? GetError(SetReleaseRecord? release)
    {
        if (release is null)
            return "Missing release.";
        var error = DrawingLibraryRules.TextError("releasedBy", release.ReleasedBy, DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("notes", release.Notes, DrawingLibraryRules.MaxNotesLength)
            ?? SetsError(release.Sets, release.Drawings)
            ?? DrawingsError(release.Drawings);
        if (error is not null)
            return error;
        if (release.Nests is null || release.Nests.Count == 0)
            return "nests must list at least one nest.";
        if (release.Nests.Count > MaxNests)
            return $"A release may have at most {MaxNests} nests.";

        var sets = release.Sets.Select(s => s.SetId).ToHashSet();
        var drawings = release.Drawings.Select(d => d.DrawingId).ToHashSet();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < release.Nests.Count; i++)
        {
            var nest = release.Nests[i];
            if (nest is null)
                return $"nests[{i}] is required.";
            if (nest.NestId == Guid.Empty)
                return $"nests[{i}].nestId is required.";
            if (!seen.Add(nest.NestId))
                return $"nests[{i}] repeats nest {nest.NestId}.";
            error = RevisionsError(i, nest)
                ?? DrawingLibraryRules.TextError($"nests[{i}].materialName", nest.MaterialName, DrawingLibraryRules.MaxTextLength)
                ?? DrawingLibraryRules.TextError($"nests[{i}].materialGrade", nest.MaterialGrade, DrawingLibraryRules.MaxTextLength)
                ?? (!double.IsFinite(nest.Thickness) || nest.Thickness < 0
                    ? $"nests[{i}].thickness must be a finite number, zero or greater."
                    : null)
                ?? (!Enum.IsDefined(nest.Units) ? $"nests[{i}].units must be a supported unit name." : null)
                ?? (!Enum.IsDefined(nest.CutType) ? $"nests[{i}].cutType must be Laser, Waterjet or Plasma." : null)
                ?? ItemsError(i, nest.Items, sets, drawings);
            if (error is not null)
                return error;
        }

        return null;
    }

    /// <summary>The first rule <paramref name="request"/> breaks, or null when it may be sent.</summary>
    public static string? GetUndoError(SetReleaseUndoRequest? request)
    {
        if (request is null)
            return "Missing undo request.";
        if (request.SkipNests is null)
            return "skipNests must be a list.";
        if (request.SkipNests.Count > MaxNests)
            return $"skipNests may list at most {MaxNests} nests.";
        if (request.SkipNests.Contains(Guid.Empty))
            return "skipNests may not contain an empty id.";
        return DrawingLibraryRules.TextError("undoneBy", request.UndoneBy, DrawingLibraryRules.MaxTextLength);
    }

    // A release names at least one set or one drawing on its own.
    private static string? SetsError(IReadOnlyList<SetReleaseLine>? lines, IReadOnlyList<SetReleaseDrawing>? drawings)
    {
        if (lines is null)
            return "sets must be a list.";
        if (lines.Count == 0 && (drawings is null || drawings.Count == 0))
            return "sets or drawings must list at least one set or drawing.";
        if (lines.Count > MaxSetLines)
            return $"A release may have at most {MaxSetLines} sets.";

        var seen = new HashSet<Guid>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line is null)
                return $"sets[{i}] is required.";
            if (line.SetId == Guid.Empty)
                return $"sets[{i}].setId is required.";
            if (!seen.Add(line.SetId))
                return $"sets[{i}] repeats set {line.SetId}.";
            if (line.SetRevision < 1)
                return $"sets[{i}].setRevision must be a positive revision.";
            if (line.Quantity < 1 || line.Quantity > MaxQuantity)
                return $"sets[{i}].quantity must be between 1 and {MaxQuantity}.";
        }

        return null;
    }

    private static string? DrawingsError(IReadOnlyList<SetReleaseDrawing>? drawings)
    {
        if (drawings is null)
            return "drawings must be a list.";
        if (drawings.Count > MaxDrawingLines)
            return $"A release may have at most {MaxDrawingLines} drawings on their own.";

        var seen = new HashSet<Guid>();
        for (var i = 0; i < drawings.Count; i++)
        {
            var drawing = drawings[i];
            if (drawing is null)
                return $"drawings[{i}] is required.";
            if (drawing.DrawingId == Guid.Empty)
                return $"drawings[{i}].drawingId is required.";
            if (!seen.Add(drawing.DrawingId))
                return $"drawings[{i}] repeats drawing {drawing.DrawingId}.";
            if (drawing.Quantity < 1 || drawing.Quantity > MaxDrawingQuantity)
                return $"drawings[{i}].quantity must be between 1 and {MaxDrawingQuantity}.";
        }

        return null;
    }

    private static string? RevisionsError(int i, SetReleaseNest nest)
    {
        if (!Enum.IsDefined(nest.Kind))
            return $"nests[{i}].kind must be created or merged.";
        if (nest.RevisionAfter < 1)
            return $"nests[{i}].revisionAfter must be a positive revision.";
        if (nest.Kind == SetReleaseNestKind.Created)
            return nest.RevisionBefore is null ? null : $"nests[{i}].revisionBefore must be empty for a created nest.";
        if (nest.RevisionBefore is not { } before || before < 1)
            return $"nests[{i}].revisionBefore is required for a merged nest.";
        return nest.RevisionAfter > before ? null : $"nests[{i}].revisionAfter must be later than revisionBefore.";
    }

    // An item with no set is the quantity of a drawing released on its own.
    private static string? ItemsError(int i, IReadOnlyList<SetReleaseNestItem>? items, HashSet<Guid> sets,
        HashSet<Guid> drawings)
    {
        if (items is null)
            return $"nests[{i}].items is required.";
        if (items.Count > MaxItemsPerNest)
            return $"nests[{i}] may record at most {MaxItemsPerNest} items.";

        var seen = new HashSet<(Guid?, Guid)>();
        for (var j = 0; j < items.Count; j++)
        {
            var item = items[j];
            var name = $"nests[{i}].items[{j}]";
            if (item is null)
                return $"{name} is required.";
            if (item.SetId is { } setId && !sets.Contains(setId))
                return $"{name}.setId must be one of the release's sets.";
            if (item.DrawingId == Guid.Empty)
                return $"{name}.drawingId is required.";
            if (item.SetId is null && !drawings.Contains(item.DrawingId))
                return $"{name} has no setId, so drawingId must be one of the release's drawings.";
            if (!seen.Add((item.SetId, item.DrawingId)))
                return item.SetId is null
                    ? $"{name} repeats drawing {item.DrawingId}."
                    : $"{name} repeats drawing {item.DrawingId} of set {item.SetId}.";
            if (item.DrawingRevision < 1)
                return $"{name}.drawingRevision must be a positive revision.";
            if (item.Quantity < 1)
                return $"{name}.quantity must be at least 1.";
        }

        return null;
    }
}
