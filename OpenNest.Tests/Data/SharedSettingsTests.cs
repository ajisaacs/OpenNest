using System.Text.Json;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.Data;

/// <summary>
/// The documents every workstation shares through the nest server: their key and document
/// rules, and the JSON forms of clamps, nest defaults and post settings, which must read back
/// exactly what their files hold.
/// </summary>
public class SharedSettingsTests
{
    [Theory]
    [InlineData("machine", "D3F0B2A4-0D7B-4B8C-9A55-3C8B7B0F1E22", "d3f0b2a4-0d7b-4b8c-9a55-3c8b7b0f1e22")]
    [InlineData("machine", "00000000-0000-0000-0000-000000000000", null)]
    [InlineData("machine", "CL-980", null)]
    [InlineData("post", "OpenNest.Posts.CincinnatiCIFiber.CIFiberPostProcessor", "OpenNest.Posts.CincinnatiCIFiber.CIFiberPostProcessor")]
    [InlineData("post", "../escape", null)]
    [InlineData("post", "", null)]
    [InlineData("clamps", "default", "default")]
    [InlineData("clamps", "other", null)]
    [InlineData("nest-defaults", "default", "default")]
    [InlineData("unknown", "default", null)]
    public void NormalizeKey_AcceptsOnlyEachKindsKeys(string kind, string key, string? expected) =>
        Assert.Equal(expected, SharedSettingRules.NormalizeKey(kind, key));

    [Fact]
    public void GetError_RequiresAJsonObjectAndAMatchingMachineId()
    {
        var machine = new MachineConfig { Name = "Synthetic laser" };
        var json = SharedMachineProvider.Serialize(machine);

        Assert.Null(SharedSettingRules.GetError("machine", machine.Id.ToString(), Write(json)));
        Assert.NotNull(SharedSettingRules.GetError("machine", Guid.NewGuid().ToString(), Write(json)));
        Assert.NotNull(SharedSettingRules.GetError("post", "Synthetic.Post", Write("[1, 2]")));
        Assert.NotNull(SharedSettingRules.GetError("post", "Synthetic.Post", Write("{ not json")));
        Assert.NotNull(SharedSettingRules.GetError("post", "Synthetic.Post", Write("")));
        Assert.NotNull(SharedSettingRules.GetError("post", "Synthetic.Post", Write(new string(' ', SharedSettingRules.MaxJsonLength + 1))));
        Assert.Null(SharedSettingRules.GetError("post", "Synthetic.Post", Write("{}")));
    }

    [Fact]
    public void ClampSettings_ParseReadsWhatToJsonWrites()
    {
        var settings = new ClampSettings
        {
            Enabled = true,
            Units = Units.Millimeters,
            Width = 38,
            Depth = 16,
            RemovalMinutes = 0.5,
            Centers = [150, 530],
            FarEndCenters = [200],
        };

        var read = ClampSettings.Parse(settings.ToJson(), out var status);

        Assert.Equal(ClampSettingsStatus.Ok, status);
        Assert.Equal(settings.ToJson(), read.ToJson());
        ClampSettings.Parse("{ not json", out var invalid);
        Assert.Equal(ClampSettingsStatus.Invalid, invalid);
    }

    [Fact]
    public void NestDefaults_ParseReadsWhatToJsonWrites()
    {
        var defaults = new NestDefaults
        {
            Units = Units.Millimeters,
            Size = new Size(1500, 3000),
            Quadrant = 3,
            PartSpacing = 6,
            EdgeSpacing = new Spacing(5, 6, 7, 8),
            MachineId = Guid.NewGuid(),
        };

        var read = NestDefaults.Parse(defaults.ToJson(), Units.Inches, out var status);

        Assert.Equal(NestDefaultsStatus.Ok, status);
        Assert.Equal(defaults.ToJson(), read.ToJson());
        Assert.Equal(Units.Inches, NestDefaults.Parse("{ not json", Units.Inches, out var invalid).Units);
        Assert.Equal(NestDefaultsStatus.Invalid, invalid);
    }

    public static TheoryData<IJsonConfigPostProcessor, IJsonConfigPostProcessor, Action<object>> Posts => new()
    {
        {
            new CincinnatiPostProcessor(new CincinnatiPostConfig { ConfigurationName = "Synthetic CL" }),
            new CincinnatiPostProcessor(new CincinnatiPostConfig()),
            config => Assert.Equal("Synthetic CL", ((CincinnatiPostConfig)config).ConfigurationName)
        },
        {
            new CIFiberPostProcessor(new CIFiberPostConfig { ConfigurationName = "Synthetic Fiber" }),
            new CIFiberPostProcessor(new CIFiberPostConfig()),
            config => Assert.Equal("Synthetic Fiber", ((CIFiberPostConfig)config).ConfigurationName)
        },
        {
            new GravographISPostProcessor(new GravographISPostConfig { Engrave = new LayerCutConfig { Depth = 0.125 } }),
            new GravographISPostProcessor(new GravographISPostConfig()),
            config => Assert.Equal(0.125, ((GravographISPostConfig)config).Engrave.Depth)
        },
    };

    [Theory]
    [MemberData(nameof(Posts))]
    public void PostSettings_LoadConfigReadsWhatSerializeConfigWrites(
        IJsonConfigPostProcessor source, IJsonConfigPostProcessor target, Action<object> check)
    {
        var json = source.SerializeConfig();

        target.LoadConfig(json);

        check(target.Config);
        Assert.Equal(json, target.SerializeConfig());
    }

    [Theory]
    [MemberData(nameof(Posts))]
    public void PostSettings_UnusableJsonKeepsTheCurrentSettings(
        IJsonConfigPostProcessor source, IJsonConfigPostProcessor _, Action<object> check)
    {
        Assert.ThrowsAny<JsonException>(() => source.LoadConfig("null"));
        Assert.ThrowsAny<JsonException>(() => source.LoadConfig("{ not json"));

        check(source.Config);
    }

    private static SharedSettingWrite Write(string json) => new() { Json = json, SavedBy = "tester" };
}
