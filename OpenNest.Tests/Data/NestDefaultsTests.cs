using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class NestDefaultsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _path;

    public NestDefaultsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
        _path = Path.Combine(_testDir, "defaults.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var original = new NestDefaults
        {
            Units = Units.Millimeters,
            Size = new Size(1220, 2440),
            Quadrant = 3,
            PartSpacing = 2.5,
            EdgeSpacing = new Spacing(1.5, 3, 1.5, 3),
        };

        original.Save(_path);
        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Millimeters, loaded.Units);
        Assert.Equal(original.Size, loaded.Size);
        Assert.Equal(3, loaded.Quadrant);
        Assert.Equal(2.5, loaded.PartSpacing);
        Assert.Equal(original.EdgeSpacing.Left, loaded.EdgeSpacing.Left);
        Assert.Equal(original.EdgeSpacing.Bottom, loaded.EdgeSpacing.Bottom);
        Assert.Equal(original.EdgeSpacing.Right, loaded.EdgeSpacing.Right);
        Assert.Equal(original.EdgeSpacing.Top, loaded.EdgeSpacing.Top);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_testDir, "nested", "defaults.json");
        new NestDefaults().Save(nested);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Load_MissingFile_ReturnsFallback()
    {
        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Missing, status);
        AssertFallback(loaded);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsFallbackButReportsInvalid()
    {
        File.WriteAllText(_path, "{ this is not json");

        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Invalid, status);
        AssertFallback(loaded);
    }

    [SkippableFact]
    public void Load_ReadDeniedFile_ReturnsFallbackButReportsInvalid()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipException("Unix file modes deny the read on Linux/macOS");

        new NestDefaults { PartSpacing = 4 }.Save(_path);
        File.SetUnixFileMode(_path, UnixFileMode.None);
        Skip.If(CanRead(_path), "Process can read a mode-000 file (running as root)");

        // The file is visible to File.Exists, but reading it is denied.
        Assert.True(File.Exists(_path));
        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Invalid, status);
        AssertFallback(loaded);
    }

    [Fact]
    public void Load_PartialFile_MergesPerField()
    {
        File.WriteAllText(_path, """{ "units": "millimeters", "partSpacing": 4 }""");

        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Millimeters, loaded.Units);
        Assert.Equal(4, loaded.PartSpacing);
        // Untouched fields keep fallback values.
        Assert.Equal(new Size(100, 100), loaded.Size);
        Assert.Equal(1, loaded.Quadrant);
        Assert.Equal(new Spacing(1, 1, 1, 1), loaded.EdgeSpacing);
    }

    [Fact]
    public void Load_OutOfRangeValues_FallBackPerField()
    {
        File.WriteAllText(
            _path,
            """
            {
              "units": "furlongs",
              "size": { "width": -50, "length": 100 },
              "quadrant": 9,
              "partSpacing": -1,
              "edgeSpacing": { "left": 1, "bottom": -2, "right": 1, "top": 1 }
            }
            """
        );

        var loaded = NestDefaults.Load(_path, out var status);

        // The file parses; only the invalid values fall back, so no warning.
        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Inches, loaded.Units);
        Assert.Equal(new Size(100, 100), loaded.Size);
        Assert.Equal(1, loaded.Quadrant);
        Assert.Equal(1, loaded.PartSpacing);
        Assert.Equal(new Spacing(1, 1, 1, 1), loaded.EdgeSpacing);
    }

    [Fact]
    public void Load_NonFiniteValues_FallBack()
    {
        File.WriteAllText(_path, """{ "partSpacing": 1e400 }""");

        var loaded = NestDefaults.Load(_path);

        // 1e400 deserializes to Infinity, which is rejected.
        Assert.Equal(1, loaded.PartSpacing);
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("\"1\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"inches, millimeters\"")]
    [InlineData("1")]
    public void Load_UndefinedOrNumericUnits_FallBackPerField(string unitsJson)
    {
        File.WriteAllText(_path, $$"""{ "units": {{unitsJson}}, "partSpacing": 4 }""");

        var loaded = NestDefaults.Load(_path, out var status);

        // Only a defined unit name is accepted; the other fields still load.
        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Inches, loaded.Units);
        Assert.Equal(4, loaded.PartSpacing);
    }

    [Theory]
    [InlineData("""{ "partSpacing": 4 }""")]
    [InlineData("""{ "units": "7", "partSpacing": 4 }""")]
    [InlineData("""{ "units": "furlongs", "partSpacing": 4 }""")]
    [InlineData("""{ "units": null, "partSpacing": 4 }""")]
    public void Load_MissingOrInvalidUnits_UseCallerFallbackUnits(string json)
    {
        File.WriteAllText(_path, json);

        var loaded = NestDefaults.Load(_path, Units.Millimeters, out var status);

        // The caller's existing unit preference survives a readable file
        // that lacks a usable unit, and the valid fields still load.
        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Millimeters, loaded.Units);
        Assert.Equal(4, loaded.PartSpacing);
    }

    [Fact]
    public void Load_SavedUnits_OverrideCallerFallbackUnits()
    {
        new NestDefaults { Units = Units.Inches }.Save(_path);

        var loaded = NestDefaults.Load(_path, Units.Millimeters, out var status);

        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(Units.Inches, loaded.Units);
    }

    [Fact]
    public void Load_MissingOrCorruptFile_UsesCallerFallbackUnits()
    {
        var missing = NestDefaults.Load(_path, Units.Millimeters, out var missingStatus);
        File.WriteAllText(_path, "{ this is not json");
        var corrupt = NestDefaults.Load(_path, Units.Millimeters, out var corruptStatus);

        Assert.Equal(NestDefaultsStatus.Missing, missingStatus);
        Assert.Equal(Units.Millimeters, missing.Units);
        Assert.Equal(NestDefaultsStatus.Invalid, corruptStatus);
        Assert.Equal(Units.Millimeters, corrupt.Units);
    }

    [Fact]
    public void Load_UndefinedCallerFallbackUnits_UseBuiltInUnits()
    {
        var loaded = NestDefaults.Load(_path, (Units)7, out _);

        Assert.Equal(Units.Inches, loaded.Units);
    }

    [Fact]
    public void Load_UnknownFieldsAndFutureVersion_Ignored()
    {
        File.WriteAllText(
            _path,
            """
            {
              "version": 2,
              "units": "inches",
              "partSpacing": 2,
              "futureFeature": { "enabled": true },
              "material": { "name": "steel" }
            }
            """
        );

        var loaded = NestDefaults.Load(_path, out var status);

        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(2, loaded.PartSpacing);
    }

    [Fact]
    public void FromNest_CapturesUnitsAndPlateDefaults()
    {
        var nest = new Nest
        {
            Units = Units.Millimeters,
        };
        nest.PlateDefaults.Size = new Size(60, 120);
        nest.PlateDefaults.Quadrant = 2;
        nest.PlateDefaults.PartSpacing = 0.5;
        nest.PlateDefaults.EdgeSpacing = new Spacing(2, 2, 2, 2);

        var captured = NestDefaults.FromNest(nest);

        Assert.Equal(Units.Millimeters, captured.Units);
        Assert.Equal(new Size(60, 120), captured.Size);
        Assert.Equal(2, captured.Quadrant);
        Assert.Equal(0.5, captured.PartSpacing);
        Assert.Equal(new Spacing(2, 2, 2, 2), captured.EdgeSpacing);
    }

    [Fact]
    public void ApplyTo_SetsUnitsAndPlateDefaults_AndDoesNotAliasSourceNest()
    {
        var source = new Nest { Units = Units.Millimeters };
        source.PlateDefaults.Size = new Size(48, 96);
        source.PlateDefaults.EdgeSpacing = new Spacing(1.25, 1.25, 1.25, 1.25);
        var defaults = NestDefaults.FromNest(source);

        // Mutating the source nest afterwards must not change the capture.
        source.PlateDefaults.Size = new Size(1, 1);
        source.PlateDefaults.EdgeSpacing = new Spacing(9, 9, 9, 9);

        var target = new Nest();
        defaults.ApplyTo(target);

        // New and BOM-created nests both take their units from ApplyTo.
        Assert.Equal(Units.Millimeters, target.Units);
        Assert.Equal(new Size(48, 96), target.PlateDefaults.Size);
        Assert.Equal(
            new Spacing(1.25, 1.25, 1.25, 1.25),
            target.PlateDefaults.EdgeSpacing
        );

        // And the applied target owns its own values too.
        target.PlateDefaults.Size = new Size(2, 2);
        Assert.Equal(new Size(48, 96), defaults.Size);
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

    private static void AssertFallback(NestDefaults loaded)
    {
        Assert.Equal(Units.Inches, loaded.Units);
        Assert.Equal(new Size(100, 100), loaded.Size);
        Assert.Equal(1, loaded.Quadrant);
        Assert.Equal(1, loaded.PartSpacing);
        Assert.Equal(new Spacing(1, 1, 1, 1), loaded.EdgeSpacing);
    }
}
