using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;

namespace OpenNest.Desktop.Tests;

public sealed class DesktopStartupTests : IDisposable
{
    private readonly string directory =
        Directory.CreateTempSubdirectory("opennest-desktop-startup-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string SettingsPath => Path.Combine(directory, "user-settings.json");

    private string DefaultsPath => Path.Combine(directory, "defaults.json");

    // BestFitCacheStore has no public constructor: Shared wraps the process-wide cache.
    private DesktopStartupResult Run() =>
        DesktopStartup.Run(SettingsPath, DefaultsPath, BestFitCacheStore.Shared);

    private void WriteSettings(Dictionary<string, string> values) =>
        Assert.True(UserSettingsFile.Write(SettingsPath, values));

    private string WriteTemplate()
    {
        var path = Path.Combine(directory, "template.nstdot");
        var nest = TestNests.OneSquare();
        nest.Units = Units.Millimeters;
        Assert.True(new NestWriter(nest).Write(path));
        return path;
    }

    [Fact]
    public void NoSettings_OpensANewNest_AsTheOptionDefaultsToOn()
    {
        var result = Run();

        Assert.True(result.CreateNewNest);
        Assert.False(result.ClearTemplateSetting);
        Assert.Null(result.TemplateWarning);
    }

    [Fact]
    public void NoSettings_LeavesTheSettingsFileUnwritten()
    {
        Run();

        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Run_NeverWritesTheSettingsFile_EvenWhenATemplateConverts()
    {
        WriteSettings(new()
        {
            [NestTemplateMigration.SettingName] = WriteTemplate(),
            ["SomethingElse"] = "kept",
        });
        var before = File.ReadAllBytes(SettingsPath);

        Run();

        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void OptionOff_OpensNoNewNest()
    {
        WriteSettings(new() { [DesktopOptions.CreateNewNestOnOpenName] = "False" });

        Assert.False(Run().CreateNewNest);
    }

    [Fact]
    public void OptionOn_OpensANewNest()
    {
        WriteSettings(new() { [DesktopOptions.CreateNewNestOnOpenName] = "True" });

        Assert.True(Run().CreateNewNest);
    }

    [Fact]
    public void ATemplate_IsConverted_AndItsSettingIsToBeCleared()
    {
        WriteSettings(new() { [NestTemplateMigration.SettingName] = WriteTemplate() });

        var result = Run();

        Assert.True(result.ClearTemplateSetting);
        Assert.Null(result.TemplateWarning);
        Assert.Equal(Units.Millimeters, NestDefaults.Load(DefaultsPath).Units);
    }

    [Fact]
    public void ExistingDefaults_AreKept_AndTheTemplateSettingIsToBeCleared()
    {
        File.WriteAllText(DefaultsPath, "{ \"kept\": true }");
        WriteSettings(new() { [NestTemplateMigration.SettingName] = WriteTemplate() });

        var result = Run();

        Assert.True(result.ClearTemplateSetting);
        Assert.Equal("{ \"kept\": true }", File.ReadAllText(DefaultsPath));
    }

    [Fact]
    public void AnAlreadyClearedSetting_IsNotClearedAgain()
    {
        WriteSettings(new() { [NestTemplateMigration.SettingName] = "" });

        Assert.False(Run().ClearTemplateSetting);
    }

    [Fact]
    public void AnUnreadableTemplate_Warns_KeepsTheSetting_AndStillOpensANewNest()
    {
        var path = Path.Combine(directory, "broken.nstdot");
        File.WriteAllText(path, "not a nest");
        WriteSettings(new() { [NestTemplateMigration.SettingName] = path });

        var result = Run();

        Assert.Contains(path, result.TemplateWarning);
        Assert.False(result.ClearTemplateSetting);
        Assert.True(result.CreateNewNest);
    }

    [Fact]
    public void ClearTemplateSetting_EmptiesOnlyThatSetting()
    {
        WriteSettings(new()
        {
            [NestTemplateMigration.SettingName] = "old.nstdot",
            ["SomethingElse"] = "kept",
        });

        Assert.True(DesktopStartup.ClearTemplateSetting(SettingsPath));

        var saved = UserSettingsFile.Read(SettingsPath);
        Assert.Equal("", saved[NestTemplateMigration.SettingName]);
        Assert.Equal("kept", saved["SomethingElse"]);
    }
}
