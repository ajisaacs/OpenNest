using System.Globalization;
using OpenNest;

namespace OpenNest.Data;

/// <summary>Operator-facing wording for set releases, shared by the release views and Nest Info.</summary>
public static class SetReleaseText
{
    /// <summary>
    /// Where a nest came from, such as "Released from set 'Kit A' revision 3 ×2 by J. Smith",
    /// or "Merged from sets 'Kit A' revision 3 ×2 and 'Kit B' revision 1 ×1 ..." for a release
    /// that added to <paramref name="nestId"/> rather than creating it.
    /// </summary>
    public static string Origin(SetReleaseRecord release, Guid nestId = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var merged = release.Nests?.FirstOrDefault(n => n.NestId == nestId)?.Kind == SetReleaseNestKind.Merged;
        var sets = release.Sets ?? Array.Empty<SetReleaseLine>();
        var text = $"{(merged ? "Merged" : "Released")} from {(sets.Count == 1 ? "set" : "sets")} {Sets(sets)}";
        if (release.ReleasedAt != default)
            text += $" on {release.ReleasedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";
        if (!string.IsNullOrWhiteSpace(release.ReleasedBy))
            text += $" by {release.ReleasedBy}";
        return release.IsUndone ? $"{text} (undone)" : text;
    }

    /// <summary>The sets of a release, such as "'Kit A' revision 3 ×2 and 'Kit B' revision 1 ×1".</summary>
    public static string Sets(IReadOnlyList<SetReleaseLine> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var names = sets.Select(s => $"'{s.SetName}' revision {s.SetRevision} ×{s.Quantity}").ToList();
        return names.Count <= 1
            ? string.Concat(names)
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    /// <summary>
    /// How far the release's nests have got, such as "2 of 3 cut" or "1 of 2 cut, 1 deleted",
    /// or "Undone". Deleted nests are left out of the count.
    /// </summary>
    public static string Progress(SetReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.IsUndone)
            return "Undone";
        var nests = release.Nests ?? Array.Empty<SetReleaseNest>();
        var deleted = nests.Count(IsDeleted);
        var active = nests.Count - deleted;
        if (active == 0)
            return nests.Count == 1 ? "Its nest was deleted" : "All nests deleted";

        var cut = nests.Count(n => !IsDeleted(n) && n.Nest?.Status == NestStatus.HasBeenCut);
        var text = $"{cut} of {active} cut";
        return deleted == 0 ? text : $"{text}, {deleted} deleted";
    }

    /// <summary>
    /// Why an undo was refused, for the operator: the release was undone before, or the nests
    /// that changed since it, each with what happened to it, so they can be skipped.
    /// </summary>
    public static string UndoRefusal(SetReleaseUndoConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (conflict.Reason == SetReleaseUndoRefusal.AlreadyUndone)
            return "This release was already undone.";
        var lines = conflict.ChangedNests.Select(n =>
        {
            var name = string.IsNullOrWhiteSpace(n.Name) ? n.Id.ToString() : n.Name;
            return n.Deleted
                ? $"- {name}: deleted since the release"
                : $"- {name}: saved since the release (now revision {n.Revision}, {NestStatuses.DisplayName(n.Status)})";
        });
        return "These nests changed after the release, so undoing would overwrite that work:\n"
            + string.Join("\n", lines)
            + "\n\nUndo the rest and leave these as they are, or restore them from their history.";
    }

    /// <summary>The state of one nest of a release: its status name, or "Deleted".</summary>
    public static string NestState(SetReleaseNest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        if (IsDeleted(nest))
            return "Deleted";
        return nest.Nest is { } record ? NestStatuses.DisplayName(record.Status) : "";
    }

    /// <summary>The material group a release nest was made for, such as "Carbon Steel A36 0.25 in laser".</summary>
    public static string Group(SetReleaseNest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var parts = new[]
        {
            nest.MaterialName,
            nest.MaterialGrade,
            $"{nest.Thickness.ToString("0.####", CultureInfo.CurrentCulture)} {UnitsHelper.GetShortString(nest.Units)}",
            nest.CutType.ToString().ToLowerInvariant(),
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    // A nest the server no longer joins is as gone as a deleted one.
    private static bool IsDeleted(SetReleaseNest nest) => nest.Nest is null || nest.Nest.Deleted;
}
