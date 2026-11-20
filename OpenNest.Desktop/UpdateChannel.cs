using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenNest.Desktop;

/// <summary>
/// Which releases an installed copy takes, as Tools &gt; Options &gt; Updates offers them.
/// </summary>
public enum UpdateChannel
{
    /// <summary>Full releases only.</summary>
    Stable,

    /// <summary>The development build, at most one new version a day.</summary>
    DevDaily,

    /// <summary>The development build, at most one new version a week.</summary>
    DevWeekly,

    /// <summary>Every development build as soon as it is published.</summary>
    DevEveryBuild,
}

/// <summary>
/// The Options update choice. It is stored as the <c>UpdateIncludeDevBuilds</c> setting older
/// copies already read, plus <see cref="IntervalSettingName"/>; a copy that only has the old
/// setting checked takes every development build, as it did before the choice existed.
/// </summary>
public static class UpdateChannelOption
{
    public const string IntervalSettingName = "UpdateDevBuildInterval";
    public const string EveryBuildText = "EveryBuild";
    public const string DailyText = "Daily";
    public const string WeeklyText = "Weekly";

    /// <summary>The choices in combo order.</summary>
    public static IReadOnlyList<(UpdateChannel Channel, string Text)> Choices { get; } = new[]
    {
        (UpdateChannel.Stable, "Stable releases only"),
        (UpdateChannel.DevDaily, "Development builds, at most once a day"),
        (UpdateChannel.DevWeekly, "Development builds, at most once a week"),
        (UpdateChannel.DevEveryBuild, "Every development build"),
    };

    /// <summary>True when the channel takes development builds.</summary>
    public static bool IncludesDevBuilds(UpdateChannel channel) => channel != UpdateChannel.Stable;

    /// <summary>
    /// The channel the two stored values describe; an unknown interval is every build.
    /// </summary>
    public static UpdateChannel Read(bool includeDevBuilds, string interval)
    {
        if (!includeDevBuilds)
            return UpdateChannel.Stable;

        return interval?.Trim() switch
        {
            DailyText => UpdateChannel.DevDaily,
            WeeklyText => UpdateChannel.DevWeekly,
            _ => UpdateChannel.DevEveryBuild,
        };
    }

    /// <summary>
    /// The stored interval of a development channel; null for <see cref="UpdateChannel.Stable"/>.
    /// </summary>
    public static string IntervalText(UpdateChannel channel) => channel switch
    {
        UpdateChannel.DevDaily => DailyText,
        UpdateChannel.DevWeekly => WeeklyText,
        UpdateChannel.DevEveryBuild => EveryBuildText,
        _ => null,
    };

    /// <summary>The combo index of <paramref name="channel"/>.</summary>
    public static int IndexOf(UpdateChannel channel)
    {
        for (var i = 0; i < Choices.Count; i++)
        {
            if (Choices[i].Channel == channel)
                return i;
        }
        return 0;
    }

    /// <summary>The channel at a combo index; no selection is the first choice.</summary>
    public static UpdateChannel At(int index) =>
        Choices[System.Math.Clamp(index, 0, Choices.Count - 1)].Channel;
}

/// <summary>
/// When the background update check runs on a once-a-day or once-a-week channel. The clock
/// starts on the day this copy first ran its current version, however that version arrived.
/// Help &gt; Check for Updates... never waits.
/// </summary>
public static class DevBuildSchedule
{
    public const string RunningVersionSettingName = "UpdateRunningVersion";
    public const string RunningSinceSettingName = "UpdateRunningSince";

    /// <summary>
    /// The version and first-run time to store at startup: the stored pair while the version is
    /// unchanged, otherwise <paramref name="runningVersion"/> from <paramref name="now"/>.
    /// </summary>
    public static (string Version, DateTimeOffset Since) Observe(
        string storedVersion,
        DateTimeOffset? storedSince,
        string runningVersion,
        DateTimeOffset now)
    {
        if (storedSince.HasValue
            && string.Equals(storedVersion, runningVersion, StringComparison.Ordinal))
            return (storedVersion, storedSince.Value);

        return (runningVersion, now);
    }

    /// <summary>
    /// True when the background check may look for an update: always on the stable and
    /// every-build channels and when nothing is recorded; once a day from the calendar day after
    /// the running version first ran; once a week from the seventh day after. Days are counted in
    /// <paramref name="now"/>'s offset, so a copy started a little earlier each morning still
    /// updates every day.
    /// </summary>
    public static bool BackgroundCheckDue(
        UpdateChannel channel, DateTimeOffset? runningSince, DateTimeOffset now)
    {
        if (runningSince is not { } since)
            return true;

        var sinceDay = since.ToOffset(now.Offset).Date;
        return channel switch
        {
            UpdateChannel.DevDaily => now.Date > sinceDay,
            UpdateChannel.DevWeekly => now.Date >= sinceDay.AddDays(7),
            _ => true,
        };
    }

    /// <summary>The stored first-run time; null when blank or unreadable.</summary>
    public static DateTimeOffset? ParseSince(string text) =>
        DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since)
            ? since
            : null;

    /// <summary>The stored text of a first-run time (round-trip, UTC).</summary>
    public static string FormatSince(DateTimeOffset since) =>
        since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
