using System.Globalization;
using OpenNest;

namespace OpenNest.Data;

/// <summary>Operator-facing wording for set releases, shared by the release views and Nest Info.</summary>
public static class SetReleaseText
{
    /// <summary>Where a nest came from, such as "Released from set 'Kit A' revision 3 ×2 by J. Smith".</summary>
    public static string Origin(SetReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var text = $"Released from set '{release.SetName}' revision {release.SetRevision} ×{release.Multiplier}";
        if (release.ReleasedAt != default)
            text += $" on {release.ReleasedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";
        return string.IsNullOrWhiteSpace(release.ReleasedBy) ? text : $"{text} by {release.ReleasedBy}";
    }

    /// <summary>
    /// How far the release's nests have got, such as "2 of 3 cut" or "1 of 2 cut, 1 deleted".
    /// Deleted nests are left out of the count.
    /// </summary>
    public static string Progress(SetReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var nests = release.Nests ?? Array.Empty<SetReleaseNest>();
        var deleted = nests.Count(IsDeleted);
        var active = nests.Count - deleted;
        if (active == 0)
            return nests.Count == 1 ? "Its nest was deleted" : "All nests deleted";

        var cut = nests.Count(n => !IsDeleted(n) && n.Nest?.Status == NestStatus.HasBeenCut);
        var text = $"{cut} of {active} cut";
        return deleted == 0 ? text : $"{text}, {deleted} deleted";
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
