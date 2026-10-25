using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using OpenNest.Posts.Cincinnati;
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
            new[] { "Machine", "Sheets", "Material", "Program output", "Macros" },
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

        var sheets = sections[1].Fields;
        Assert.Equal(new[] { "OneProgramPerSheet", "PalletChangeCode" }, sheets.Select(f => f.Name));
        Assert.Equal(PostSettingKind.Boolean, sheets[0].Kind);

        var materials = sections[2].Fields[0];
        Assert.Equal(PostSettingKind.StringMap, materials.Kind);
        Assert.Equal("Material name", materials.KeyHeader);
        Assert.Equal("Machine code", materials.ValueHeader);

        Assert.All(
            sections.SelectMany(s => s.Fields),
            f => Assert.False(string.IsNullOrWhiteSpace(f.Description), f.Name)
        );
    }

    [Fact]
    public void CincinnatiConfig_BuildsOrderedSections_CoveringEveryEditableProperty()
    {
        var sections = PostSettingsLayout.TryBuild(typeof(CincinnatiPostConfig));

        Assert.NotNull(sections);
        Assert.Equal(
            new[]
            {
                "Program output", "Sheets", "Libraries", "Process", "Kerf",
                "Head raise", "Feedrates", "Sub-programs", "Variables",
            },
            sections.Select(s => s.Name)
        );
        Assert.All(sections, s => Assert.NotEmpty(s.Description));

        var fields = sections.SelectMany(s => s.Fields).ToList();
        var editable = typeof(CincinnatiPostConfig)
            .GetProperties()
            .Where(p => p.GetSetMethod() != null)
            .Select(p => p.Name);
        Assert.Equal(editable.OrderBy(n => n), fields.Select(f => f.Name).OrderBy(n => n));
        Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Description), f.Name));

        var byName = fields.ToDictionary(f => f.Name);
        Assert.True(byName["SafetyHeadraiseDistance"].IsOptional);
        Assert.Equal(PostSettingKind.Integer, byName["SafetyHeadraiseDistance"].Kind);
        Assert.True(byName["M47OverrideDistanceThreshold"].IsOptional);
        Assert.Equal(PostSettingKind.Decimal, byName["M47OverrideDistanceThreshold"].Kind);
        Assert.False(byName["PostedAccuracy"].IsOptional);

        var libraries = byName["MaterialLibraries"];
        Assert.Equal(PostSettingKind.Table, libraries.Kind);
        Assert.Equal(new[] { "Material", "Thickness", "Gas", "Library" }, libraries.Columns.Select(c => c.Label));
        Assert.Equal(PostSettingKind.Decimal, libraries.Columns[1].Kind);
        Assert.Equal(
            new[] { "Max radius", "Feedrate", "Variable" },
            byName["ArcFeedrateRanges"].Columns.Select(c => c.Label)
        );

        var selected = byName["SelectedLibrary"];
        Assert.Equal(PostSettingKind.Text, selected.Kind);
        Assert.Equal("MaterialLibraries", selected.SuggestionsTable);
        Assert.Contains(libraries.Columns, c => c.Name == selected.SuggestionsColumn);
        Assert.Equal("SelectedLibrary", sections[2].Fields[0].Name);
    }

    [Fact]
    public void BuildList_ParsesRowsInOrderAndSkipsBlankRows()
    {
        var field = CincinnatiField("ArcFeedrateRanges");

        var list = Assert.IsType<List<ArcFeedrateRange>>(
            PostSettingsLayout.BuildList(
                field,
                new[] { Cells(" 0.5 ", "0.25", "123"), Cells("", " ", null), Cells("2", "0.8", "124") },
                CultureInfo.InvariantCulture
            )
        );

        Assert.Equal(2, list.Count);
        Assert.Equal(0.5, list[0].MaxRadius);
        Assert.Equal(0.25, list[0].FeedratePercent);
        Assert.Equal(123, list[0].VariableNumber);
        Assert.Equal(2, list[1].MaxRadius);
    }

    [Fact]
    public void BuildList_KeepsTextCellsAndUsesTheGivenCulture()
    {
        var field = CincinnatiField("MaterialLibraries");

        var list = Assert.IsType<List<MaterialLibraryEntry>>(
            PostSettingsLayout.BuildList(
                field,
                new[] { Cells(" Aluminum ", "0,125", "N2", "AL125N2") },
                CultureInfo.GetCultureInfo("de-DE")
            )
        );

        var entry = Assert.Single(list);
        Assert.Equal("Aluminum", entry.Material);
        Assert.Equal(0.125, entry.Thickness);
        Assert.Equal("AL125N2", entry.Library);
    }

    [Fact]
    public void BuildList_RejectsMissingAndMalformedNumbersByRow()
    {
        var field = CincinnatiField("ArcFeedrateRanges");

        var malformed = Assert.Throws<FormatException>(() =>
            PostSettingsLayout.BuildList(
                field,
                new[] { Cells("1", "0.5", "123"), Cells("x", "0.5", "124") },
                CultureInfo.InvariantCulture
            )
        );
        Assert.Contains("Row 2", malformed.Message);
        Assert.Contains("Max radius", malformed.Message);

        var missing = Assert.Throws<FormatException>(() =>
            PostSettingsLayout.BuildList(field, new[] { Cells("1", "0.5", "") }, CultureInfo.InvariantCulture)
        );
        Assert.Contains("Row 1", missing.Message);
        Assert.Contains("Variable", missing.Message);

        Assert.Throws<FormatException>(() =>
            PostSettingsLayout.BuildList(field, new[] { Cells("1", "0.5", "1.5") }, CultureInfo.InvariantCulture)
        );
    }

    [Fact]
    public void TableOfUnsupportedRowType_FallsBackToPropertyGrid()
    {
        Assert.Null(PostSettingsLayout.TryBuild(typeof(NestedListConfig)));
    }

    private static PostSettingsField CincinnatiField(string name) =>
        PostSettingsLayout
            .TryBuild(typeof(CincinnatiPostConfig))
            .SelectMany(s => s.Fields)
            .Single(f => f.Name == name);

    private static IReadOnlyList<string> Cells(params string[] cells) => cells;

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

    private sealed class NestedListConfig
    {
        [PostSetting("Main")]
        public List<NestedConfig> Rows { get; set; } = new();
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
