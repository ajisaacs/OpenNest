using System.Collections.Specialized;
using System.Configuration;
using System.Drawing;
using System.Reflection;
using OpenNest.Properties;

namespace OpenNest.WinForms.Tests;

public sealed class AppDataSettingsProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNestSettings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "user-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SettingsUseTheAppDataProvider()
    {
        var attribute = typeof(Settings).GetCustomAttribute<SettingsProviderAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(AppDataSettingsProvider).AssemblyQualifiedName, attribute.ProviderTypeName);
    }

    [Fact]
    public void SavedValuesReadBackInANewProvider()
    {
        var properties = Properties();
        var values = new AppDataSettingsProvider(SettingsPath, null).GetPropertyValues(new SettingsContext(), properties);
        values["IncludeDevBuilds"].PropertyValue = true;
        values["WindowLocation"].PropertyValue = new Point(40, 60);
        new AppDataSettingsProvider(SettingsPath, null).SetPropertyValues(new SettingsContext(), values);

        var reloaded = new AppDataSettingsProvider(SettingsPath, null).GetPropertyValues(new SettingsContext(), Properties());

        Assert.Equal(true, reloaded["IncludeDevBuilds"].PropertyValue);
        Assert.Equal(new Point(40, 60), reloaded["WindowLocation"].PropertyValue);
        Assert.Equal("", reloaded["FeedUrl"].PropertyValue);
    }

    [Fact]
    public void UntouchedDefaultsAreNotWritten()
    {
        var provider = new AppDataSettingsProvider(SettingsPath, null);
        var values = provider.GetPropertyValues(new SettingsContext(), Properties());
        _ = values["IncludeDevBuilds"].PropertyValue;
        values["FeedUrl"].PropertyValue = "http://feed";
        provider.SetPropertyValues(new SettingsContext(), values);

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("FeedUrl", json);
        Assert.DoesNotContain("IncludeDevBuilds", json);
        Assert.DoesNotContain("WindowLocation", json);
    }

    [Fact]
    public void EntriesThisBuildDoesNotKnowAreKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "FromNewerBuild": "x" }""");
        var provider = new AppDataSettingsProvider(SettingsPath, null);
        var values = provider.GetPropertyValues(new SettingsContext(), Properties());
        values["FeedUrl"].PropertyValue = "http://feed";

        provider.SetPropertyValues(new SettingsContext(), values);

        Assert.Contains("FromNewerBuild", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void LegacyValuesAreCarriedOverOnceWhenTheFileIsMissing()
    {
        var legacyCalls = 0;
        SettingsProvider Legacy()
        {
            legacyCalls++;
            return new FixedProvider(new() { ["IncludeDevBuilds"] = "True" });
        }

        var values = new AppDataSettingsProvider(SettingsPath, Legacy).GetPropertyValues(new SettingsContext(), Properties());
        var reloaded = new AppDataSettingsProvider(SettingsPath, Legacy).GetPropertyValues(new SettingsContext(), Properties());

        Assert.Equal(true, values["IncludeDevBuilds"].PropertyValue);
        Assert.Equal(true, reloaded["IncludeDevBuilds"].PropertyValue);
        Assert.Equal(1, legacyCalls);
    }

    [Fact]
    public void TheDefaultUserConfigProviderCanBeReadForCarryOver()
    {
        var context = new SettingsContext { ["GroupName"] = typeof(AppDataSettingsProviderTests).FullName, ["SettingsKey"] = "" };

        var values = new AppDataSettingsProvider(SettingsPath, () => new LocalFileSettingsProvider())
            .GetPropertyValues(context, Properties());

        Assert.Equal(false, values["IncludeDevBuilds"].PropertyValue);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void ACorruptFileReadsAsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "not json");

        var values = new AppDataSettingsProvider(SettingsPath, null).GetPropertyValues(new SettingsContext(), Properties());

        Assert.Equal(false, values["IncludeDevBuilds"].PropertyValue);
    }

    private static SettingsPropertyCollection Properties()
    {
        return new SettingsPropertyCollection
        {
            UserSetting("IncludeDevBuilds", typeof(bool), "False"),
            UserSetting("FeedUrl", typeof(string), ""),
            UserSetting("WindowLocation", typeof(Point), "0, 0"),
        };
    }

    private static SettingsProperty UserSetting(string name, Type type, string defaultValue)
    {
        var attributes = new SettingsAttributeDictionary { [typeof(UserScopedSettingAttribute)] = new UserScopedSettingAttribute() };
        return new SettingsProperty(name, type, null, false, defaultValue, SettingsSerializeAs.String, attributes, false, false);
    }

    /// <summary>Stands in for user.config: returns the given serialized values.</summary>
    private sealed class FixedProvider : SettingsProvider
    {
        private readonly Dictionary<string, string> _values;

        public FixedProvider(Dictionary<string, string> values) => _values = values;

        public override string ApplicationName { get; set; } = "Test";

        public override void Initialize(string name, NameValueCollection config) => base.Initialize("Fixed", config);

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty property in collection)
            {
                var value = new SettingsPropertyValue(property);
                if (_values.TryGetValue(property.Name, out var serialized))
                    value.SerializedValue = serialized;
                values.Add(value);
            }

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection) { }
    }
}
