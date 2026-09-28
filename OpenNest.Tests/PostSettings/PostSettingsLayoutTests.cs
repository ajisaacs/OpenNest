using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using OpenNest.Posts.CincinnatiCIFiber;
using OpenNest.PostSettings;
using Xunit;

namespace OpenNest.Tests.PostSettings;

public class PostSettingsLayoutTests
{
    [Fact]
    public void CIFiberConfig_BuildsOrderedSections_CoveringEveryEditableProperty()
    {
        var sections = PostSettingsLayout.TryBuild(typeof(CIFiberPostConfig));

        Assert.NotNull(sections);
        Assert.Equal(
            new[] { "Machine", "Material", "Program output", "Macros" },
            sections.Select(s => s.Name)
        );
        Assert.All(sections, s => Assert.NotEmpty(s.Description));

        var shown = sections.SelectMany(s => s.Fields).Select(f => f.Name).ToList();
        var editable = typeof(CIFiberPostConfig)
            .GetProperties()
            .Where(p => p.GetSetMethod() != null)
            .Select(p => p.Name)
            .ToList();
        Assert.Equal(editable.OrderBy(n => n), shown.OrderBy(n => n));
        Assert.Equal(shown.Count, shown.Distinct().Count());

        var machine = sections[0].Fields;
        Assert.Equal(
            new[] { "ConfigurationName", "MaxTableX", "MaxTableY" },
            machine.Select(f => f.Name)
        );
        Assert.Equal(PostSettingKind.Decimal, machine[1].Kind);
        Assert.Equal("Maximum table X", machine[1].Label);
        Assert.Contains("0 disables", machine[1].Description);

        var materials = sections[1].Fields[0];
        Assert.Equal(PostSettingKind.StringMap, materials.Kind);
        Assert.Equal("Material name", materials.KeyHeader);
        Assert.Equal("Machine code", materials.ValueHeader);

        Assert.All(
            sections.SelectMany(s => s.Fields),
            f => Assert.False(string.IsNullOrWhiteSpace(f.Description), f.Name)
        );
    }

    [Fact]
    public void FieldsRoundTripValuesThroughTheConfig()
    {
        var config = new CIFiberPostConfig();
        var fields = PostSettingsLayout
            .TryBuild(typeof(CIFiberPostConfig))
            .SelectMany(s => s.Fields)
            .ToDictionary(f => f.Name);

        fields["MaxTableX"].SetValue(config, 120.5);
        fields["PostedAccuracy"].SetValue(config, 4);
        fields["SkipScribe"].SetValue(config, false);

        Assert.Equal(120.5, config.MaxTableX);
        Assert.Equal(4, config.PostedAccuracy);
        Assert.False(config.SkipScribe);
        Assert.Equal(0, fields["PostedAccuracy"].DecimalPlaces);
        Assert.Equal(6, fields["PostedAccuracy"].Maximum);
    }

    [Fact]
    public void UnannotatedConfig_FallsBackToPropertyGrid()
    {
        Assert.Null(PostSettingsLayout.TryBuild(typeof(PlainConfig)));
    }

    [Fact]
    public void UnsupportedPropertyType_FallsBackToPropertyGrid()
    {
        Assert.Null(PostSettingsLayout.TryBuild(typeof(NestedConfig)));
    }

    [Fact]
    public void UnmarkedEditableProperty_LandsInOtherSection()
    {
        var sections = PostSettingsLayout.TryBuild(typeof(PartlyMarkedConfig));

        Assert.Equal(new[] { "Main", PostSettingsLayout.OtherSection }, sections.Select(s => s.Name));
        Assert.Equal("Extra", Assert.Single(sections[1].Fields).Name);
        Assert.Equal(PostSettingKind.Choice, sections[1].Fields[0].Kind);
    }

    [Fact]
    public void BuildMap_TrimsSkipsBlankRowsAndKeepsComparer()
    {
        var map = PostSettingsLayout.BuildMap(
            new[]
            {
                Row(" Mild Steel ", " MSN "),
                Row("", ""),
                Row("Stainless", "SS"),
            },
            StringComparer.OrdinalIgnoreCase
        );

        Assert.Equal(2, map.Count);
        Assert.Equal("MSN", map["mild steel"]);
        Assert.Equal("SS", map["Stainless"]);
    }

    [Fact]
    public void BuildMap_RejectsDuplicateAndMissingNames()
    {
        var duplicate = Assert.Throws<FormatException>(() =>
            PostSettingsLayout.BuildMap(
                new[] { Row("Mild Steel", "MSN"), Row("mild steel", "X") },
                StringComparer.OrdinalIgnoreCase
            )
        );
        Assert.Contains("Row 2", duplicate.Message);

        var missing = Assert.Throws<FormatException>(() =>
            PostSettingsLayout.BuildMap(new[] { Row(" ", "SS") }, StringComparer.Ordinal)
        );
        Assert.Contains("Row 1", missing.Message);
    }

    private static KeyValuePair<string, string> Row(string key, string value) => new(key, value);

    private sealed class PlainConfig
    {
        [DisplayName("Name")]
        public string Name { get; set; } = "";
    }

    private sealed class NestedConfig
    {
        [PostSetting("Main")]
        public string Name { get; set; } = "";

        public PlainConfig Child { get; set; } = new();
    }

    private enum Mode
    {
        A,
        B,
    }

    private sealed class PartlyMarkedConfig
    {
        public Mode Extra { get; set; }

        [PostSetting("Main")]
        public string Name { get; set; } = "";
    }
}
