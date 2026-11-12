using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

public class SavedCutTimeSettingsTests
{
    private static readonly Guid MachineId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    // As the WinForms app stores them in user-settings.json (System.Configuration's invariant text).
    private static Dictionary<string, string> WinFormsSaved() => new()
    {
        ["LastFeedrate"] = "250",
        ["LastHoleFeedrate"] = "120.5",
        ["LastRapidFeedrate"] = "1000",
        ["LastPierceTime"] = "0.75",
        ["CutTimeSkipEtch"] = "False",
        ["CutTimeMachine"] = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        ["MainWindowState"] = "Maximized",
    };

    [Fact]
    public void Read_TheWinFormsValues()
    {
        var saved = SavedCutTimeSettings.Read(WinFormsSaved());

        Assert.Equal(new SavedCutTimeSettings(250m, 120.5m, 1000m, 0.75m, false, MachineId.ToString()), saved);
    }

    [Fact]
    public void WriteTo_GivesBackTheWinFormsTextUnchanged_AndKeepsOtherSettings()
    {
        var stored = WinFormsSaved();

        var written = new Dictionary<string, string>(stored);
        SavedCutTimeSettings.Read(stored).WriteTo(written);

        Assert.Equal(stored, written);
    }

    [Fact]
    public void TheUserSettingsFile_ReadsAndWritesBackTheWinFormsEntries()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("opennest-desktop-cut-time-").FullName, "user-settings.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "LastFeedrate": "250",
                  "LastHoleFeedrate": "120.5",
                  "LastRapidFeedrate": "1000",
                  "LastPierceTime": "0.75",
                  "CutTimeSkipEtch": "False",
                  "CutTimeMachine": "none",
                  "MainWindowState": "Maximized"
                }
                """);
            var before = UserSettingsFile.Read(path);

            var saved = SavedCutTimeSettings.Read(before);
            Assert.True(UserSettingsFile.Update(path, saved.WriteTo));

            Assert.Equal(250m, saved.Feedrate);
            Assert.Equal(SavedCutTimeSettings.FullSpeed, saved.Machine);
            Assert.Equal(before, UserSettingsFile.Read(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Read_NothingSaved_IsTheWinFormsDefaults()
    {
        var saved = SavedCutTimeSettings.Read(new Dictionary<string, string>());

        Assert.Equal(new SavedCutTimeSettings(1m, 0m, 1m, 0m, true, ""), saved);
        Assert.True(saved.IsUnset);
    }

    [Fact]
    public void Read_UnreadableValues_AreEachTheirWinFormsDefault()
    {
        var settings = new Dictionary<string, string>
        {
            ["LastFeedrate"] = "fast",
            ["LastHoleFeedrate"] = "",
            ["LastRapidFeedrate"] = "1,000", // not invariant text
            ["LastPierceTime"] = "0.5",
            ["CutTimeSkipEtch"] = "yes",
        };

        var saved = SavedCutTimeSettings.Read(settings);

        Assert.Equal(new SavedCutTimeSettings(1m, 0m, 1m, 0.5m, true, ""), saved);
    }

    [Fact]
    public void ToCutParameters_NeverSaved_UsesTheCutParameterDefaults()
    {
        var parameters = SavedCutTimeSettings.Unsaved.ToCutParameters(Units.Millimeters, null);

        var defaults = CutParameters.Default;
        Assert.Equal(defaults.Feedrate, parameters.Feedrate);
        Assert.Equal(defaults.Feedrate, parameters.HoleFeedrate);
        Assert.Equal(defaults.RapidTravelRate, parameters.RapidTravelRate);
        Assert.Equal(defaults.PierceTime, parameters.PierceTime);
        Assert.True(parameters.SkipScribe);
        Assert.Equal(Units.Millimeters, parameters.Units);
        Assert.Null(parameters.Motion);
    }

    [Fact]
    public void ToCutParameters_OnlyAllThreeUnsavedValuesCountAsNeverSaved()
    {
        var saved = SavedCutTimeSettings.Unsaved with { PierceSeconds = 0.25m };

        var parameters = saved.ToCutParameters(Units.Inches, null);

        Assert.False(saved.IsUnset);
        Assert.Equal(1, parameters.Feedrate);
        Assert.Equal(1, parameters.RapidTravelRate);
        Assert.Equal(TimeSpan.FromSeconds(0.25), parameters.PierceTime);
    }

    [Fact]
    public void ToCutParameters_SavedValues_AndNoHoleFeedUsesTheFeed()
    {
        var saved = SavedCutTimeSettings.Read(WinFormsSaved()) with { HoleFeedrate = 0m };

        var parameters = saved.ToCutParameters(Units.Inches, null);

        Assert.Equal(250, parameters.Feedrate);
        Assert.Equal(250, parameters.HoleFeedrate);
        Assert.Equal(1000, parameters.RapidTravelRate);
        Assert.Equal(TimeSpan.FromSeconds(0.75), parameters.PierceTime);
        Assert.False(parameters.SkipScribe);
    }

    [Fact]
    public void ToCutParameters_TakesTheMachinesLimitsInTheNestsUnits()
    {
        var machine = new MachineConfig { Units = UnitSystem.Inches };

        var parameters = SavedCutTimeSettings.Read(WinFormsSaved()).ToCutParameters(Units.Millimeters, machine);

        var expected = machine.GetMotionOrPlaceholder().ToProfile(UnitSystem.Inches, Units.Millimeters);
        Assert.NotNull(parameters.Motion);
        Assert.Equal(expected.CutAcceleration, parameters.Motion.CutAcceleration);
        Assert.Equal(120.5, parameters.HoleFeedrate);
    }

    [Fact]
    public void With_StoresWhatTheWinFormsAppStores()
    {
        var parameters = new CutParameters
        {
            Feedrate = 100.0,
            HoleFeedrate = null,
            RapidTravelRate = 300.25,
            PierceTime = TimeSpan.FromSeconds(0.5),
            SkipScribe = true,
        };

        var written = new Dictionary<string, string>();
        SavedCutTimeSettings.Read(WinFormsSaved()).With(parameters).WriteTo(written);

        Assert.Equal("100", written["LastFeedrate"]);
        Assert.Equal("0", written["LastHoleFeedrate"]);
        Assert.Equal("300.25", written["LastRapidFeedrate"]);
        Assert.Equal("0.5", written["LastPierceTime"]);
        Assert.Equal("True", written["CutTimeSkipEtch"]);
        Assert.Equal(MachineId.ToString(), written["CutTimeMachine"]);
    }

    private static List<MachineConfig> Machines() =>
    [
        new MachineConfig { Name = "Zeta" },
        new MachineConfig { Id = MachineId, Name = "Mid" },
        new MachineConfig { Name = "alpha" },
    ];

    [Fact]
    public void ChooseMachine_TheSavedId()
    {
        Assert.Equal("Mid", SavedCutTimeSettings.ChooseMachine(Machines(), MachineId.ToString()).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void ChooseMachine_NoChoiceOrAGoneMachine_IsTheFirstByName(string saved)
    {
        Assert.Equal("alpha", SavedCutTimeSettings.ChooseMachine(Machines(), saved).Name);
    }

    [Fact]
    public void ChooseMachine_FullSpeedIsNoMachine()
    {
        Assert.Null(SavedCutTimeSettings.ChooseMachine(Machines(), "none"));
        Assert.Equal("none", SavedCutTimeSettings.MachineChoice(null));
        Assert.Equal(MachineId.ToString(), SavedCutTimeSettings.MachineChoice(Machines()[1]));
    }
}
