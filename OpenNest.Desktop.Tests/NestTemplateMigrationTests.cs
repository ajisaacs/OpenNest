using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;

namespace OpenNest.Desktop.Tests;

public sealed class NestTemplateMigrationTests : IDisposable
{
    private readonly string directory =
        Directory.CreateTempSubdirectory("opennest-desktop-template-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string DefaultsPath => Path.Combine(directory, "defaults.json");

    private readonly BestFitCacheStore store = BestFitCacheStore.Shared;

    private string WriteTemplate()
    {
        var path = Path.Combine(directory, "template.nstdot");
        var nest = TestNests.OneSquare();
        nest.Units = Units.Millimeters;
        Assert.True(new NestWriter(nest).Write(path));
        return path;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoTemplate_DoesNothing(string? templatePath)
    {
        var result = NestTemplateMigration.Run(templatePath!, DefaultsPath, store);

        Assert.Equal(new NestTemplateMigrationResult(false, null!), result);
        Assert.False(File.Exists(DefaultsPath));
    }

    [Fact]
    public void ATemplate_BecomesTheDefaultsFile_AndTheSettingIsCleared()
    {
        var settings = new Dictionary<string, string> { ["NestTemplatePath"] = WriteTemplate() };

        var result = NestTemplateMigration.Run(settings, DefaultsPath, store);

        Assert.True(result.ClearSetting);
        Assert.Null(result.Warning);
        Assert.Equal("", settings["NestTemplatePath"]);
        Assert.Equal(Units.Millimeters, NestDefaults.Load(DefaultsPath).Units);
    }

    [Fact]
    public void RunsOnce_AClearedSettingNeverConvertsAgain()
    {
        var settings = new Dictionary<string, string> { ["NestTemplatePath"] = WriteTemplate() };
        NestTemplateMigration.Run(settings, DefaultsPath, store);
        File.Delete(DefaultsPath);

        var again = NestTemplateMigration.Run(settings, DefaultsPath, store);

        Assert.False(again.ClearSetting);
        Assert.False(File.Exists(DefaultsPath));
    }

    [Fact]
    public void ExistingDefaults_AreKept_AndTheSettingIsCleared()
    {
        File.WriteAllText(DefaultsPath, "{ \"kept\": true }");

        var result = NestTemplateMigration.Run(WriteTemplate(), DefaultsPath, store);

        Assert.True(result.ClearSetting);
        Assert.Equal("{ \"kept\": true }", File.ReadAllText(DefaultsPath));
    }

    [Fact]
    public void AMissingTemplate_IsCleared()
    {
        var result = NestTemplateMigration.Run(Path.Combine(directory, "gone.nstdot"), DefaultsPath, store);

        Assert.Equal(new NestTemplateMigrationResult(true, null!), result);
        Assert.False(File.Exists(DefaultsPath));
    }

    [Fact]
    public void AnUnreadableTemplate_WarnsAndKeepsTheSetting()
    {
        var path = Path.Combine(directory, "broken.nstdot");
        File.WriteAllText(path, "not a nest");
        var settings = new Dictionary<string, string> { ["NestTemplatePath"] = path };

        var result = NestTemplateMigration.Run(settings, DefaultsPath, store);

        Assert.False(result.ClearSetting);
        Assert.Contains(path, result.Warning);
        Assert.Contains("Tools > Nest Defaults", result.Warning);
        Assert.Equal(path, settings["NestTemplatePath"]);
        Assert.False(File.Exists(DefaultsPath));
    }
}
