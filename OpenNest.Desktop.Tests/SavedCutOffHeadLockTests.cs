namespace OpenNest.Desktop.Tests;

public class SavedCutOffHeadLockTests
{
    // As the WinForms app stores them in user-settings.json.
    private static Dictionary<string, string> WinFormsSaved() => new()
    {
        ["CutOffLockHeadHeight"] = "True",
        ["CutOffHeadLockOvercutInches"] = "2.25",
        ["LastFeedrate"] = "250",
    };

    [Fact]
    public void Read_TheWinFormsValues()
    {
        Assert.Equal(new SavedCutOffHeadLock(true, 2.25), SavedCutOffHeadLock.Read(WinFormsSaved()));
    }

    [Fact]
    public void WriteTo_GivesBackTheWinFormsTextUnchanged_AndKeepsOtherSettings()
    {
        var stored = WinFormsSaved();

        var written = new Dictionary<string, string>(stored);
        SavedCutOffHeadLock.Read(stored).WriteTo(written);

        Assert.Equal(stored, written);
    }

    [Fact]
    public void WriteTo_TheOvercutInRoundTripForm()
    {
        var written = new Dictionary<string, string>();

        SavedCutOffHeadLock.From(false, 1.0, Units.Millimeters).WriteTo(written);

        Assert.Equal("False", written["CutOffLockHeadHeight"]);
        Assert.Equal("0.03937007874015748", written["CutOffHeadLockOvercutInches"]);
        Assert.Equal(1.0 / 25.4, SavedCutOffHeadLock.Read(written).OvercutInches);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("maybe", "1.5in")]
    [InlineData("", "")]
    public void Read_MissingOrUnreadable_IsTheWinFormsDefaults(string? lockText, string? overcutText)
    {
        var settings = new Dictionary<string, string>();
        if (lockText != null)
            settings["CutOffLockHeadHeight"] = lockText;
        if (overcutText != null)
            settings["CutOffHeadLockOvercutInches"] = overcutText;

        Assert.Equal(new SavedCutOffHeadLock(false, 1.5), SavedCutOffHeadLock.Read(settings));
    }

    [Fact]
    public void ApplyTo_ConvertsTheInchesToAMillimeterNest()
    {
        var settings = new CutOffSettings();

        SavedCutOffHeadLock.Read(WinFormsSaved()).ApplyTo(settings, Units.Millimeters);

        Assert.True(settings.LockHeadHeight);
        Assert.Equal(2.25 * 25.4, settings.HeadLockOvercut, 9);
    }

    [Fact]
    public void ApplyTo_AnInchNestKeepsTheInches()
    {
        var settings = new CutOffSettings();

        SavedCutOffHeadLock.Read(WinFormsSaved()).ApplyTo(settings, Units.Inches);

        Assert.Equal(2.25, settings.HeadLockOvercut);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ApplyTo_AnOvercutThatIsNotPositive_Is1Point5Inches(double inches)
    {
        var settings = new CutOffSettings();

        new SavedCutOffHeadLock(true, inches).ApplyTo(settings, Units.Millimeters);

        Assert.Equal(1.5 * 25.4, settings.HeadLockOvercut, 9);
    }

    [Fact]
    public void From_AMillimeterNestStoresInches()
    {
        var saved = SavedCutOffHeadLock.From(true, 50.8, Units.Millimeters);

        Assert.Equal(new SavedCutOffHeadLock(true, 2.0), saved);
        Assert.Equal(new SavedCutOffHeadLock(true, 2.0), SavedCutOffHeadLock.From(true, 2.0, Units.Inches));
    }
}
