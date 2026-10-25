using OpenNest.Data.MaterialLedger;

namespace OpenNest.Tests.Data;

public class MaterialLedgerSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-ledger-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(directory, "material-ledger.json");

    private static string Protect(string plain) => "P:" + new string(plain.Reverse().ToArray());

    private static string Unprotect(string text) =>
        text.StartsWith("P:") ? new string(text[2..].Reverse().ToArray()) : throw new FormatException("not ours");

    [Fact]
    public void RoundTripStoresOnlyTheProtectedKey()
    {
        new MaterialLedgerSettings { BaseUrl = " http://ledger:5000/ ", ApiKey = "secret-key" }.Save(SettingsPath, Protect);

        var json = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("secret-key", json);
        Assert.Contains("yek-terces", json);

        var loaded = MaterialLedgerSettings.Load(SettingsPath, Unprotect);
        Assert.Equal("http://ledger:5000", loaded.BaseUrl);
        Assert.Equal("secret-key", loaded.ApiKey);
        Assert.True(loaded.IsConfigured);
    }

    [Fact]
    public void KeyThatCannotBeUnprotectedLoadsEmpty()
    {
        new MaterialLedgerSettings { BaseUrl = "http://ledger", ApiKey = "secret-key" }.Save(SettingsPath, _ => "foreign blob");
        var loaded = MaterialLedgerSettings.Load(SettingsPath, Unprotect);
        Assert.Equal("http://ledger", loaded.BaseUrl);
        Assert.Equal("", loaded.ApiKey);
    }

    [Fact]
    public void MissingOrCorruptFileIsNotConfigured()
    {
        Assert.False(MaterialLedgerSettings.Load(SettingsPath, Unprotect).IsConfigured);
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{ not json");
        Assert.False(MaterialLedgerSettings.Load(SettingsPath, Unprotect).IsConfigured);
    }

    [Theory]
    [InlineData("", true, "")]
    [InlineData(" https://ledger/ ", true, "https://ledger")]
    [InlineData("ledger", false, "")]
    [InlineData("ftp://ledger", false, "")]
    public void UrlsAreValidatedAndNormalized(string input, bool valid, string expected)
    {
        Assert.Equal(valid, MaterialLedgerSettings.TryNormalizeUrl(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
