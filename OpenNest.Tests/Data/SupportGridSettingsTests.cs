using OpenNest.Data;
using OpenNest.Support;

namespace OpenNest.Tests.Data;

/// <summary>
/// The machine support grid store (%APPDATA%\OpenNest\support-grid.json): off by default, a
/// never-throwing read that keeps each invalid value's default, and unit conversion for new nests.
/// </summary>
public class SupportGridSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
    private readonly string _path;

    public SupportGridSettingsTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "support-grid.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void MissingFile_IsOffAndGivesNewNestsNoGrid()
    {
        var settings = SupportGridSettings.Load(_path);
        var nest = new Nest();

        settings.ApplyToNewNest(nest);

        Assert.False(settings.Enabled);
        Assert.Null(nest.SupportBed);
    }

    [Fact]
    public void SavedSettingsRoundTripAndConvertToMillimeterNests()
    {
        new SupportGridSettings
        {
            Enabled = true,
            PitchX = 2,
            PitchY = 1,
            OffsetX = 0.5,
            OffsetY = -0.25,
            RegistrationTolerance = 0.125,
            StabilityMargin = 0.25,
        }.Save(_path);
        var nest = new Nest { Units = Units.Millimeters };

        SupportGridSettings.Load(_path).ApplyToNewNest(nest);

        var grid = Assert.IsType<SupportGrid>(nest.SupportBed);
        Assert.Equal(50.8, grid.PitchX, 9);
        Assert.Equal(25.4, grid.PitchY, 9);
        Assert.Equal(-6.35, grid.OffsetY, 9);
        Assert.Equal(6.35, grid.StabilityMargin, 9);
    }

    [Fact]
    public void SavedSlatsRoundTrip_AndKeepTheGridValuesForSwitchingBack()
    {
        new SupportGridSettings
        {
            Enabled = true,
            PitchX = 3,
            UseSlats = true,
            SlatAxis = SlatAxis.X,
            SlatPitch = 4,
            SlatThickness = 0.125,
            SlatOffset = 2,
        }.Save(_path);
        var settings = SupportGridSettings.Load(_path);
        var nest = new Nest { Units = Units.Millimeters };

        settings.ApplyToNewNest(nest);

        var slats = Assert.IsType<SupportSlats>(nest.SupportBed);
        Assert.Equal(SlatAxis.X, slats.Axis);
        Assert.Equal(101.6, slats.Pitch, 9);
        Assert.Equal(3.175, slats.Thickness, 9);
        Assert.Equal(50.8, slats.Offset, 9);
        Assert.Equal(3, settings.PitchX);

        var fromBed = SupportGridSettings.FromBed(slats, Units.Millimeters, enabled: true, settings);
        Assert.True(fromBed.UseSlats);
        Assert.Equal(76.2, fromBed.PitchX, 9);
    }

    [Fact]
    public void OlderFile_WithoutALayout_IsAGrid()
    {
        File.WriteAllText(_path, """{"enabled":true,"pitchX":2,"pitchY":2}""");

        var settings = SupportGridSettings.Load(_path);

        Assert.False(settings.UseSlats);
        Assert.Equal(0.125, settings.SlatThickness);
        Assert.IsType<SupportGrid>(settings.BedFor(Units.Inches));
    }

    [Fact]
    public void InvalidValues_KeepTheirDefaults_AndBadJsonLoadsAsOff()
    {
        File.WriteAllText(_path, """{"enabled":true,"pitchX":-1,"pitchY":"x","offsetX":0.5,"stabilityMargin":-2}""");
        var settings = SupportGridSettings.Load(_path);

        Assert.True(settings.Enabled);
        Assert.Equal(new SupportGridSettings().PitchX, settings.PitchX);
        Assert.Equal(new SupportGridSettings().PitchY, settings.PitchY);
        Assert.Equal(0.5, settings.OffsetX);
        Assert.Equal(new SupportGridSettings().StabilityMargin, settings.StabilityMargin);

        File.WriteAllText(_path, "{ not json");
        Assert.False(SupportGridSettings.Load(_path).Enabled);
    }
}
