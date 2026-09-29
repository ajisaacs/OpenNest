using System.Text.Json;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class EngineSelectionSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
    private readonly string _path;

    public EngineSelectionSettingsTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "engine-selection.json");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void DefaultPath_UsesApplicationDataOpenNestDirectory()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest", "engine-selection.json"),
            EngineSelectionSettings.DefaultPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPluginNameAsCamelCaseJson()
    {
        new EngineSelectionSettings { EngineName = "Opus55NestingEngine" }.Save(_path);

        var loaded = EngineSelectionSettings.Load(_path);
        using var json = JsonDocument.Parse(File.ReadAllText(_path));

        Assert.Equal("Opus55NestingEngine", loaded.EngineName);
        Assert.Equal("Opus55NestingEngine", json.RootElement.GetProperty("engineName").GetString());
    }

    [Fact]
    public void Save_OverwritesPreviousSelection()
    {
        new EngineSelectionSettings { EngineName = "Opus55NestingEngine" }.Save(_path);
        new EngineSelectionSettings { EngineName = "Strip" }.Save(_path);

        Assert.Equal("Strip", EngineSelectionSettings.Load(_path).EngineName);
    }

    [Fact]
    public void Save_CreatesMissingParentDirectory()
    {
        var path = Path.Combine(_directory, "nested", "engine-selection.json");

        new EngineSelectionSettings { EngineName = "Strip" }.Save(path);

        Assert.Equal("Strip", EngineSelectionSettings.Load(path).EngineName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Save_EmptySelectionWritesDefault(string? engineName)
    {
        new EngineSelectionSettings { EngineName = engineName! }.Save(_path);

        using var json = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.Equal("Default", json.RootElement.GetProperty("engineName").GetString());
    }

    [Fact]
    public void Load_MissingFileReturnsDefaultWithoutCreatingFile()
    {
        var loaded = EngineSelectionSettings.Load(_path);

        Assert.Equal("Default", loaded.EngineName);
        Assert.False(File.Exists(_path));
        Assert.Equal("Default", loaded.Resolve(new[] { "Default" }, out var message));
        Assert.Null(message);
    }

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"engineName\":null}")]
    [InlineData("{\"engineName\":\" \"}")]
    [InlineData("{\"engineName\":42}")]
    public void Load_CorruptOrEmptySettingsReturnsDefault(string json)
    {
        File.WriteAllText(_path, json);

        Assert.Equal("Default", EngineSelectionSettings.Load(_path).EngineName);
        Assert.Equal(json, File.ReadAllText(_path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    public void Load_InvalidPathReturnsDefault(string? path)
    {
        Assert.Equal("Default", EngineSelectionSettings.Load(path!).EngineName);
    }

    [Fact]
    public void Load_UnreadablePathReturnsDefault()
    {
        Directory.CreateDirectory(_path);

        Assert.Equal("Default", EngineSelectionSettings.Load(_path).EngineName);
    }

    [Fact]
    public void Load_IgnoresUnknownFieldsAndTrimsSelection()
    {
        File.WriteAllText(_path,
            """{"EngineName":"  Opus55NestingEngine  ","futureSetting":true}""");

        Assert.Equal("Opus55NestingEngine", EngineSelectionSettings.Load(_path).EngineName);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Strip")]
    [InlineData("Vertical Remnant")]
    [InlineData("Horizontal Remnant")]
    [InlineData("Opus55NestingEngine")]
    public void Resolve_AvailableSelectionUsesRegisteredCasingWithoutWarning(string engineName)
    {
        var settings = new EngineSelectionSettings { EngineName = engineName.ToLowerInvariant() };

        var resolved = settings.Resolve(new[] { "Default", engineName }, out var message);

        Assert.Equal(engineName, resolved);
        Assert.Null(message);
    }

    [Fact]
    public void Resolve_MissingPluginFallsBackWithVisibleMessageWithoutOverwritingSavedChoice()
    {
        new EngineSelectionSettings { EngineName = "Opus55NestingEngine" }.Save(_path);
        var settings = EngineSelectionSettings.Load(_path);

        var resolved = settings.Resolve(new[] { "Default", "Strip" }, out var message);

        Assert.Equal("Default", resolved);
        Assert.Equal("Saved Auto Nest engine 'Opus55NestingEngine' is unavailable. Using Default.", message);
        Assert.Equal("Opus55NestingEngine", settings.EngineName);
        Assert.Equal("Opus55NestingEngine", EngineSelectionSettings.Load(_path).EngineName);
    }

    [Fact]
    public void Load_DoesNotResolveBeforePluginDiscovery()
    {
        new EngineSelectionSettings { EngineName = "Opus55NestingEngine" }.Save(_path);
        var settings = EngineSelectionSettings.Load(_path);
        var availableEngines = new List<string> { "Default" };

        // The host loads settings independently, then supplies the completed registry.
        availableEngines.Add("Opus55NestingEngine");
        var resolved = settings.Resolve(availableEngines, out var message);

        Assert.Equal("Opus55NestingEngine", resolved);
        Assert.Null(message);
    }

    [Fact]
    public void Save_UnwritablePathReportsFailureToCaller()
    {
        File.WriteAllText(_path, "not a directory");
        var path = Path.Combine(_path, "engine-selection.json");

        Assert.Throws<IOException>(() => new EngineSelectionSettings().Save(path));
    }
}
