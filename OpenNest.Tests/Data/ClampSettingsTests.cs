using System.Text.Json;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

/// <summary>
/// The machine clamp settings store (%APPDATA%\OpenNest\clamps.json): off by default, a
/// never-throwing read that degrades value by value, and no effect until enabled.
/// </summary>
public class ClampSettingsTests : IDisposable
{
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
    private readonly string _path;

    public ClampSettingsTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "clamps.json");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        if (!OperatingSystem.IsWindows() && File.Exists(_path))
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.Delete(_directory, recursive: true);
    }

    private static ClampSettings Machine(bool enabled = true) => new()
    {
        Enabled = enabled,
        Width = 1.5,
        Depth = 0.625,
        Centers = MachineCenters,
    };

    private static void AssertFallback(ClampSettings settings)
    {
        Assert.False(settings.Enabled);
        Assert.Equal(Units.Inches, settings.Units);
        Assert.Equal(1.5, settings.Width);
        Assert.Equal(0.625, settings.Depth);
        Assert.Empty(settings.Centers);
        Assert.Null(settings.RailFor(Units.Inches));
    }

    [Fact]
    public void DefaultPathUsesTheApplicationDataOpenNestDirectory()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest", "clamps.json"),
            ClampSettings.DefaultPath);
    }

    [Fact]
    public void MissingFileKeepsClampsOff()
    {
        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Missing, status);
        AssertFallback(loaded);
        Assert.Equal(ClampSettingsStatus.Missing, LoadStatus(""));
    }

    [Fact]
    public void SaveThenLoadRoundTripsAsCamelCaseJson()
    {
        Machine().Save(_path);

        var loaded = ClampSettings.Load(_path, out var status);
        using var json = JsonDocument.Parse(File.ReadAllText(_path));

        Assert.Equal(ClampSettingsStatus.Ok, status);
        Assert.True(loaded.Enabled);
        Assert.Equal(Units.Inches, loaded.Units);
        Assert.Equal(1.5, loaded.Width);
        Assert.Equal(0.625, loaded.Depth);
        Assert.Equal(MachineCenters, loaded.Centers);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal("inches", root.GetProperty("units").GetString());
        Assert.Equal(8, root.GetProperty("centers").GetArrayLength());
    }

    [Fact]
    public void SaveCreatesTheSettingsDirectory()
    {
        var nested = Path.Combine(_directory, "AppData", "OpenNest", "clamps.json");

        Machine().Save(nested);

        Assert.True(ClampSettings.Load(nested).Enabled);
    }

    [Fact]
    public void NothingChangesUntilEnabled()
    {
        var nest = new Nest();
        Machine(enabled: false).ApplyToNewNest(nest);

        Assert.Null(Machine(enabled: false).RailFor(Units.Inches));
        Assert.Null(nest.PlateDefaults.ClampRail);
        Assert.Empty(nest.CreatePlate().ClampZones);

        // Enabled without any clamp center is still no clamps.
        var empty = Machine();
        empty.Centers = Array.Empty<double>();
        Assert.Null(empty.RailFor(Units.Inches));
    }

    [Fact]
    public void EnabledSettingsGiveANewNestTheMachineRail()
    {
        var nest = new Nest { Units = Units.Inches };
        nest.PlateDefaults.Size = new Size(48, 60);

        Machine().ApplyToNewNest(nest);
        var plate = nest.CreatePlate();

        Assert.Equal(new ClampRail(1.5, 0.625, MachineCenters), nest.PlateDefaults.ClampRail);
        Assert.Equal(new[] { 5.25, 20.25, 35.25, 50.25 }, plate.ClampZones.Select(z => z.Bounds.X));
    }

    [Fact]
    public void RailIsConvertedToTheNestsUnits()
    {
        var metric = Machine().RailFor(Units.Millimeters)!;
        Assert.Equal(1.5 * 25.4, metric.Width, 12);
        Assert.Equal(0.625 * 25.4, metric.Depth, 12);
        Assert.Equal(6 * 25.4, metric.Centers[0], 12);

        var settings = new ClampSettings
        {
            Enabled = true,
            Units = Units.Millimeters,
            Width = 38.1,
            Depth = 15.875,
            Centers = new[] { 152.4 },
        };
        var inches = settings.RailFor(Units.Inches)!;
        Assert.Equal(1.5, inches.Width, 12);
        Assert.Equal(6, inches.Centers[0], 12);
        Assert.Equal(38.1, settings.RailFor(Units.Millimeters)!.Width);
    }

    [Fact]
    public void PartialFileMergesOverTheDefaults()
    {
        File.WriteAllText(_path, """{ "enabled": true, "centers": [21, 6] }""");

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Ok, status);
        Assert.True(loaded.Enabled);
        Assert.Equal(1.5, loaded.Width);
        Assert.Equal(0.625, loaded.Depth);
        Assert.Equal(new[] { 6.0, 21 }, loaded.Centers);
    }

    [Fact]
    public void InvalidValuesFallBackIndividuallyAndAreReported()
    {
        File.WriteAllText(_path, """
            {
              "enabled": "yes",
              "units": "7",
              "width": -1,
              "depth": "deep",
              "centers": [6, "x", -3, 21, 1e400, 21],
              "future": { "ignored": true }
            }
            """);

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Partial, status);
        Assert.False(loaded.Enabled);
        Assert.Equal(Units.Inches, loaded.Units);
        Assert.Equal(1.5, loaded.Width);
        Assert.Equal(0.625, loaded.Depth);
        Assert.Equal(new[] { 6.0, 21 }, loaded.Centers);
    }

    [Theory]
    [InlineData("""{ "centers": 6 }""")]
    [InlineData("""{ "centers": [6, -1] }""")]
    [InlineData("""{ "centers": [6, "21"] }""")]
    [InlineData("""{ "units": ["inches"] }""")]
    [InlineData("""{ "enabled": 1 }""")]
    [InlineData("""{ "width": 0 }""")]
    public void AnySingleInvalidValueIsReported(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Equal(ClampSettingsStatus.Partial, LoadStatus(_path));
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("units")]
    [InlineData("width")]
    [InlineData("depth")]
    [InlineData("centers")]
    public void ExplicitNullIsAnIgnoredValueAndIsReported(string field)
    {
        File.WriteAllText(_path, $$"""{ "enabled": true, "centers": [6], "{{field}}": null }""");

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Partial, status);
        Assert.Equal(1.5, loaded.Width);
        Assert.Equal(0.625, loaded.Depth);
    }

    [Fact]
    public void MetricFileWithoutSizesGetsThePhysicalDefaultClamp()
    {
        File.WriteAllText(_path, """{ "enabled": true, "units": "millimeters", "centers": [152.4] }""");

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Ok, status);
        Assert.Equal(1.5 * 25.4, loaded.Width, 9);
        Assert.Equal(0.625 * 25.4, loaded.Depth, 9);
        Assert.Equal(1.5, loaded.RailFor(Units.Inches)!.Width, 9);
    }

    [Fact]
    public void MetricFileWithAnInvalidSizeFallsBackToThePhysicalDefault()
    {
        File.WriteAllText(_path, """{ "enabled": true, "units": "millimeters", "width": -1, "depth": 15, "centers": [152.4] }""");

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Partial, status);
        Assert.Equal(38.1, loaded.Width, 9);
        Assert.Equal(15, loaded.Depth);
    }

    [Theory]
    [InlineData("""{ "enabled": true, "width": 1e308, "centers": [6] }""")]
    [InlineData("""{ "enabled": true, "depth": 1e308, "centers": [6] }""")]
    [InlineData("""{ "enabled": true, "centers": [6, 1e308] }""")]
    public void ValuesThatCannotConvertBetweenUnitsAreIgnored(string json)
    {
        File.WriteAllText(_path, json);

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Partial, status);
        var nest = new Nest { Units = Units.Millimeters };
        loaded.ApplyToNewNest(nest); // must not throw while creating a nest
        Assert.Equal(new[] { 6 * 25.4 }, nest.PlateDefaults.ClampRail!.Centers);
    }

    [Fact]
    public void AnInvalidCenterIsDroppedButTheRestOfTheRailStillLoads()
    {
        File.WriteAllText(_path, """{ "enabled": true, "centers": [6, "21", 36] }""");

        var loaded = ClampSettings.Load(_path, out var status);

        // The operator must be warned: the rail is now missing a physical clamp.
        Assert.Equal(ClampSettingsStatus.Partial, status);
        Assert.True(loaded.Enabled);
        Assert.Equal(new[] { 6.0, 36 }, loaded.Centers);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[6, 21, 36]")]
    [InlineData("null")]
    [InlineData("")]
    public void UnparseableFileKeepsClampsOffAndIsReported(string json)
    {
        File.WriteAllText(_path, json);

        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Invalid, status);
        AssertFallback(loaded);
    }

    [SkippableFact]
    public void UnreadableFileKeepsClampsOffAndIsReported()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipException("Unix file modes deny the read on Linux/macOS");

        Machine().Save(_path);
        File.SetUnixFileMode(_path, UnixFileMode.None);
        Skip.If(CanRead(_path), "Process can read a mode-000 file (running as root)");

        Assert.True(File.Exists(_path));
        var loaded = ClampSettings.Load(_path, out var status);

        Assert.Equal(ClampSettingsStatus.Invalid, status);
        AssertFallback(loaded);
    }

    [Fact]
    public void FromRailCapturesANestRailForMachineDefaults()
    {
        var rail = new ClampRail(38.1, 15.875, new[] { 152.4, 533.4 });

        var captured = ClampSettings.FromRail(rail, Units.Millimeters, enabled: true);

        Assert.True(captured.Enabled);
        Assert.Equal(Units.Millimeters, captured.Units);
        Assert.Equal(38.1, captured.Width);
        Assert.Equal(15.875, captured.Depth);
        Assert.Equal(new[] { 152.4, 533.4 }, captured.Centers);
        Assert.Equal(rail, captured.RailFor(Units.Millimeters));

        var none = ClampSettings.FromRail(null, Units.Inches, enabled: false);
        AssertFallback(none);
    }

    private static ClampSettingsStatus LoadStatus(string path)
    {
        ClampSettings.Load(path, out var status);
        return status;
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
