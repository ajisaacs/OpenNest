namespace OpenNest.Desktop.Tests;

public class UpdateChannelTests
{
    private static readonly TimeSpan Indiana = TimeSpan.FromHours(-5);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, Indiana);

    [Fact]
    public void Read_AnOldCheckedBoxWithoutAnInterval_TakesEveryBuild_AsBefore()
    {
        var options = DesktopOptions.Read(new Dictionary<string, string>
        {
            ["UpdateIncludeDevBuilds"] = "True",
        });

        Assert.Equal(UpdateChannel.DevEveryBuild, options.UpdateChannel);
    }

    [Theory]
    [InlineData("False", "Daily", UpdateChannel.Stable)]
    [InlineData("True", "Daily", UpdateChannel.DevDaily)]
    [InlineData("True", "Weekly", UpdateChannel.DevWeekly)]
    [InlineData("True", "EveryBuild", UpdateChannel.DevEveryBuild)]
    [InlineData("True", "Hourly", UpdateChannel.DevEveryBuild)]
    public void Read_TheChannel(string include, string interval, UpdateChannel expected)
    {
        var options = DesktopOptions.Read(new Dictionary<string, string>
        {
            ["UpdateIncludeDevBuilds"] = include,
            ["UpdateDevBuildInterval"] = interval,
        });

        Assert.Equal(expected, options.UpdateChannel);
    }

    [Theory]
    [InlineData(UpdateChannel.DevDaily, "True", "Daily")]
    [InlineData(UpdateChannel.DevWeekly, "True", "Weekly")]
    [InlineData(UpdateChannel.DevEveryBuild, "True", "EveryBuild")]
    public void WriteTo_ADevelopmentChannel_StoresBothValues(UpdateChannel channel, string include, string interval)
    {
        var written = new Dictionary<string, string>();

        new DesktopOptions { UpdateChannel = channel }.WriteTo(written);

        Assert.Equal(include, written["UpdateIncludeDevBuilds"]);
        Assert.Equal(interval, written["UpdateDevBuildInterval"]);
        Assert.Equal(channel, DesktopOptions.Read(written).UpdateChannel);
    }

    [Fact]
    public void WriteTo_Stable_KeepsTheStoredInterval_ForWhenDevelopmentBuildsComeBack()
    {
        var written = new Dictionary<string, string> { ["UpdateDevBuildInterval"] = "Weekly" };

        new DesktopOptions { UpdateChannel = UpdateChannel.Stable }.WriteTo(written);

        Assert.Equal("False", written["UpdateIncludeDevBuilds"]);
        Assert.Equal("Weekly", written["UpdateDevBuildInterval"]);
    }

    [Fact]
    public void Choices_RoundTripThroughTheComboIndex()
    {
        Assert.Equal(
            new[] { UpdateChannel.Stable, UpdateChannel.DevDaily, UpdateChannel.DevWeekly, UpdateChannel.DevEveryBuild },
            UpdateChannelOption.Choices.Select(c => c.Channel));
        foreach (var (channel, _) in UpdateChannelOption.Choices)
            Assert.Equal(channel, UpdateChannelOption.At(UpdateChannelOption.IndexOf(channel)));
        Assert.Equal(UpdateChannel.Stable, UpdateChannelOption.At(-1));
    }

    [Theory]
    [InlineData(20, 7, 0, false)] // same day, later
    [InlineData(20, 23, 59, false)]
    [InlineData(21, 6, 55, true)] // next morning, earlier in the day than the install
    public void Daily_WaitsForTheNextCalendarDay(int day, int hour, int minute, bool due)
    {
        var since = At(20, 7, 5);

        Assert.Equal(due, DevBuildSchedule.BackgroundCheckDue(UpdateChannel.DevDaily, since, At(day, hour, minute)));
    }

    [Theory]
    [InlineData(26, 23, false)]
    [InlineData(27, 0, true)]
    public void Weekly_WaitsSevenCalendarDays(int day, int hour, bool due)
    {
        var since = At(20, 7, 5);

        Assert.Equal(due, DevBuildSchedule.BackgroundCheckDue(UpdateChannel.DevWeekly, since, At(day, hour)));
    }

    [Fact]
    public void Daily_CountsDaysInTheLocalOffset_NotUtc()
    {
        // 23:30 local on the 20th is already the 21st in UTC; 08:00 local on the 21st is due.
        var since = At(20, 23, 30).ToUniversalTime();

        Assert.False(DevBuildSchedule.BackgroundCheckDue(UpdateChannel.DevDaily, since, At(20, 23, 45)));
        Assert.True(DevBuildSchedule.BackgroundCheckDue(UpdateChannel.DevDaily, since, At(21, 8)));
    }

    [Theory]
    [InlineData(UpdateChannel.Stable)]
    [InlineData(UpdateChannel.DevEveryBuild)]
    public void StableAndEveryBuild_AlwaysCheck(UpdateChannel channel)
    {
        Assert.True(DevBuildSchedule.BackgroundCheckDue(channel, At(20, 7), At(20, 7, 1)));
    }

    [Theory]
    [InlineData(UpdateChannel.DevDaily)]
    [InlineData(UpdateChannel.DevWeekly)]
    public void NothingRecorded_Checks(UpdateChannel channel)
    {
        Assert.True(DevBuildSchedule.BackgroundCheckDue(channel, null, At(20, 7)));
    }

    [Fact]
    public void Observe_KeepsTheFirstRunTime_WhileTheVersionIsUnchanged()
    {
        var first = At(18, 9);

        Assert.Equal(("0.8.0-dev.900", first),
            DevBuildSchedule.Observe("0.8.0-dev.900", first, "0.8.0-dev.900", At(20, 7)));
    }

    [Theory]
    [InlineData("0.8.0-dev.899")]
    [InlineData("")]
    [InlineData(null)]
    public void Observe_StartsTheClock_OnANewVersion(string stored)
    {
        Assert.Equal(("0.8.0-dev.900", At(20, 7)),
            DevBuildSchedule.Observe(stored, At(18, 9), "0.8.0-dev.900", At(20, 7)));
    }

    [Fact]
    public void Observe_StartsTheClock_WhenNoTimeIsStored()
    {
        Assert.Equal(("0.8.0-dev.900", At(20, 7)),
            DevBuildSchedule.Observe("0.8.0-dev.900", null, "0.8.0-dev.900", At(20, 7)));
    }

    [Fact]
    public void Since_RoundTripsThroughItsStoredText()
    {
        var since = At(20, 7, 5);

        var text = DevBuildSchedule.FormatSince(since);

        Assert.Equal("2026-11-20T12:05:00.0000000+00:00", text);
        Assert.Equal(since, DevBuildSchedule.ParseSince(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("yesterday")]
    public void Since_BlankOrUnreadable_IsNothingRecorded(string text)
    {
        Assert.Null(DevBuildSchedule.ParseSince(text));
    }
}
