using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class UserSettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
    private readonly string _path;

    public UserSettingsFileTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "user-settings.json");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void DefaultPath_IsTheWinFormsSettingsFile()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest", "user-settings.json"),
            UserSettingsFile.DefaultPath);
    }

    [Fact]
    public void Read_MissingFile_IsEmptyAndCreatesNothing()
    {
        Assert.Empty(UserSettingsFile.Read(_path));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Read_ValuesTheWinFormsAppWrote()
    {
        File.WriteAllText(_path, """
            {
              "MainWindowSize": "1936, 1048",
              "MainWindowState": "Maximized",
              "LastFeedrate": "100"
            }
            """);

        var values = UserSettingsFile.Read(_path);

        Assert.Equal("1936, 1048", values["MainWindowSize"]);
        Assert.Equal("Maximized", values["MainWindowState"]);
        Assert.Equal("100", values["LastFeedrate"]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[\"a\"]")]
    [InlineData("")]
    public void Read_BrokenFile_IsEmptyAndLeavesTheFileAlone(string content)
    {
        File.WriteAllText(_path, content);

        Assert.Empty(UserSettingsFile.Read(_path));
        Assert.Equal(content, File.ReadAllText(_path));
    }

    [Fact]
    public void Read_SkipsEntriesThatAreNotText()
    {
        File.WriteAllText(_path, """{ "Kept": "x", "Number": 5, "Nested": { "a": "b" } }""");

        var values = UserSettingsFile.Read(_path);

        Assert.Equal(new Dictionary<string, string> { ["Kept"] = "x" }, values);
    }

    [Fact]
    public void Update_KeepsEntriesItDoesNotChange()
    {
        File.WriteAllText(_path, """{ "FromNewerBuild": "x", "MainWindowState": "Normal" }""");

        var updated = UserSettingsFile.Update(
            _path, values => values["MainWindowState"] = "Maximized");

        Assert.True(updated);

        var values = UserSettingsFile.Read(_path);
        Assert.Equal("x", values["FromNewerBuild"]);
        Assert.Equal("Maximized", values["MainWindowState"]);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Update_BrokenFile_WritesAReadableFile()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.True(UserSettingsFile.Update(_path, values => values["MainWindowState"] = "Normal"));

        Assert.Equal("Normal", UserSettingsFile.Read(_path)["MainWindowState"]);
    }

    [Fact]
    public void Update_CreatesTheFolder()
    {
        var path = Path.Combine(_directory, "new", "user-settings.json");

        Assert.True(UserSettingsFile.Update(path, values => values["A"] = "1"));

        Assert.Equal("1", UserSettingsFile.Read(path)["A"]);
    }

    [Fact]
    public void Write_FailureReturnsFalseWithoutThrowing()
    {
        // A file where the settings folder should be makes the write fail.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "");

        Assert.False(UserSettingsFile.Write(
            Path.Combine(blocker, "user-settings.json"),
            new Dictionary<string, string> { ["A"] = "1" }));
    }
}
