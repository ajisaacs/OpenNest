using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Bounds shared by the set release client and server. Whether the set revision and
/// every nest exist, and whether a nest already belongs to a release, is checked by the server.
/// </summary>
public static class SetReleaseRules
{
    public const int MaxMultiplier = 1000;

    /// <summary>One nest per material group, so never more nests than a set has members.</summary>
    public const int MaxNests = DrawingSetRules.MaxMembers;

    /// <summary>The first rule <paramref name="release"/> breaks, or null when it may be posted.</summary>
    public static string? GetError(SetReleaseRecord? release)
    {
        if (release is null)
            return "Missing release.";
        if (release.SetRevision < 1)
            return "setRevision must be a positive revision.";
        if (release.Multiplier < 1 || release.Multiplier > MaxMultiplier)
            return $"multiplier must be between 1 and {MaxMultiplier}.";
        var error = DrawingLibraryRules.TextError("releasedBy", release.ReleasedBy, DrawingLibraryRules.MaxTextLength)
            ?? DrawingLibraryRules.TextError("notes", release.Notes, DrawingLibraryRules.MaxNotesLength);
        if (error is not null)
            return error;
        if (release.Nests is null || release.Nests.Count == 0)
            return "nests must list at least one nest.";
        if (release.Nests.Count > MaxNests)
            return $"A release may have at most {MaxNests} nests.";

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
            error = DrawingLibraryRules.TextError($"nests[{i}].materialName", nest.MaterialName, DrawingLibraryRules.MaxTextLength)
                ?? DrawingLibraryRules.TextError($"nests[{i}].materialGrade", nest.MaterialGrade, DrawingLibraryRules.MaxTextLength)
                ?? (!double.IsFinite(nest.Thickness) || nest.Thickness < 0
                    ? $"nests[{i}].thickness must be a finite number, zero or greater."
                    : null)
                ?? (!Enum.IsDefined(nest.Units) ? $"nests[{i}].units must be a supported unit name." : null)
                ?? (!Enum.IsDefined(nest.CutType) ? $"nests[{i}].cutType must be Laser, Waterjet or Plasma." : null);
            if (error is not null)
                return error;
        }

        return null;
    }
}
