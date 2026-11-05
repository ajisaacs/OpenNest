using System.Text.Json;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>Cutting settings (rounding, tabs, pierce) kept per machine thickness.</summary>
public class ThicknessCuttingSettingsTests
{
    private static readonly ThicknessCuttingConfig ThinTabs = new()
    {
        Tabs = true,
        TabBySize = true,
        TabSize = 0.03,
        TabHeldMaxLength = 4,
        TabSizeMinWidth = 0.5,
        TabSizeMaxWidth = 3,
        PierceClearance = 0.05,
        PierceDiameter = 0.08,
        RoundLeadInAngles = true,
        LeadInAngleIncrement = 15,
    };

    private static MachineConfig Machine(UnitSystem units = UnitSystem.Inches, double thin = 0.25, double thick = 0.5) => new()
    {
        Name = "Fiber 1",
        Units = units,
        Materials =
        {
            new MaterialConfig
            {
                Name = "Mild Steel",
                Grade = "A36",
                Thicknesses =
                {
                    new ThicknessConfig
                    {
                        Value = thin,
                        LeadIn = new LeadConfig { Type = "Line", Length = 0.125, Angle = 60 },
                        LeadOut = new LeadConfig { Type = "None" },
                        Cutting = ThinTabs,
                    },
                    new ThicknessConfig
                    {
                        Value = thick,
                        LeadIn = new LeadConfig { Type = "Line", Length = 0.25, Angle = 60 },
                        LeadOut = new LeadConfig { Type = "None" },
                        Cutting = new ThicknessCuttingConfig { Tabs = false },
                    },
                    new ThicknessConfig
                    {
                        Value = 1,
                        LeadIn = new LeadConfig { Type = "Line", Length = 0.25, Angle = 60 },
                        LeadOut = new LeadConfig { Type = "None" },
                    },
                },
            },
        },
    };

    // As the machine files are read: camel case, enums by name.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static Nest NestOf(double thickness, Units units = Units.Inches) =>
        new() { Units = units, Thickness = thickness, Material = new Material("Mild Steel", "A36", 0.2836) };

    private static CuttingParameters Fallback() => new()
    {
        SupportTabs = true,
        TabConfig = new NormalTab { Size = 0.1 },
        TabSizeMaxWidth = 9,
        PierceClearance = 0.2,
    };

    [Fact]
    public void ApplyTo_GivesEachThicknessItsOwnTabs()
    {
        var thin = Fallback();
        var thick = Fallback();

        Assert.True(MachineCuttingDefaults.ApplyTo(thin, Machine(), NestOf(0.25)));
        Assert.True(MachineCuttingDefaults.ApplyTo(thick, Machine(), NestOf(0.5)));

        Assert.True(thin.SupportTabs);
        Assert.True(thin.TabBySize);
        Assert.Equal(0.03, thin.TabConfig.Size);
        Assert.Equal(4, thin.TabHeldMaxLength);
        Assert.Equal(0.5, thin.TabSizeMinWidth);
        Assert.Equal(3, thin.TabSizeMaxWidth);
        Assert.Equal(0.05, thin.PierceClearance);
        Assert.Equal(0.08, thin.PierceDiameter);
        Assert.True(thin.RoundLeadInAngles);
        Assert.Equal(15, thin.LeadInAngleIncrement);
        Assert.False(thick.SupportTabs);
    }

    [Fact]
    public void ApplyTo_ThicknessWithoutCuttingSettingsKeepsThisPcsSettings()
    {
        var parameters = Fallback();

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(), NestOf(1)));

        Assert.True(parameters.SupportTabs);
        Assert.Equal(0.1, parameters.TabConfig.Size);
        Assert.Equal(9, parameters.TabSizeMaxWidth);
        Assert.Equal(0.2, parameters.PierceClearance);
        Assert.Equal(0.25, ((LineLeadIn)parameters.ExternalLeadIn).Length);
    }

    [Fact]
    public void ApplyTo_ConvertsTheThicknessLengthsToTheNestsUnits()
    {
        var parameters = Fallback();

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(), NestOf(6.35, Units.Millimeters)));

        Assert.Equal(0.03 * 25.4, parameters.TabConfig.Size, 9);
        Assert.Equal(3 * 25.4, parameters.TabSizeMaxWidth, 9);
        Assert.Equal(0.08 * 25.4, parameters.PierceDiameter, 9);
        Assert.Equal(15, parameters.LeadInAngleIncrement);
    }

    [Fact]
    public void ApplyTo_RetiresAFallbackTabEveryPart()
    {
        var parameters = Fallback();
        parameters.TabsEnabled = true;

        MachineCuttingDefaults.ApplyTo(parameters, Machine(), NestOf(0.5));

        Assert.False(parameters.TabsEnabled);
    }

    [Fact]
    public void WriteTo_StoresTheDialogsSettingsOnTheThickness()
    {
        var machine = Machine();
        var nest = NestOf(1);
        var parameters = Fallback();
        MachineCuttingDefaults.ApplyTo(parameters, machine, nest);
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 };
        parameters.InternalLeadIn = new ArcLeadIn { Radius = 0.1 };
        parameters.SupportTabs = false;
        parameters.PierceDiameter = 0.07;

        Assert.True(MachineCuttingDefaults.WriteTo(machine, nest, parameters));

        var thickness = machine.Materials[0].Thicknesses[2];
        Assert.Equal("Line", thickness.LeadIn.Type);
        Assert.Equal(0.3, thickness.LeadIn.Length);
        Assert.Equal(45, thickness.LeadIn.Angle);
        Assert.Equal("Arc", thickness.InsideLeadIn.Type);
        Assert.Equal(0.1, thickness.InsideLeadIn.Radius);
        Assert.NotNull(thickness.Cutting);
        Assert.False(thickness.Cutting.Tabs);
        Assert.Equal(0.07, thickness.Cutting.PierceDiameter);

        var reread = new CuttingParameters { SupportTabs = true };
        MachineCuttingDefaults.ApplyTo(reread, machine, nest);
        Assert.False(reread.SupportTabs);
        Assert.Equal(0.3, ((LineLeadIn)reread.ExternalLeadIn).Length);
        Assert.Equal(0.1, ((ArcLeadIn)reread.InternalLeadIn).Radius);
    }

    [Fact]
    public void WriteTo_InsideCutsMatchingTheOutsideOnesKeepFollowingThem()
    {
        var machine = Machine();
        var nest = NestOf(0.25);
        var parameters = new CuttingParameters();
        MachineCuttingDefaults.ApplyTo(parameters, machine, nest);

        Assert.True(MachineCuttingDefaults.WriteTo(machine, nest, parameters));

        var thickness = machine.Materials[0].Thicknesses[0];
        Assert.Null(thickness.InsideLeadIn);
        Assert.Null(thickness.InsideLeadOut);
    }

    [Fact]
    public void WriteTo_ConvertsToTheMachinesUnitsAndKeepsLeadsItCannotHold()
    {
        var machine = Machine(UnitSystem.Millimeters, thin: 6.35);
        var nest = NestOf(0.25);
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLineLeadIn { Length1 = 0.2, Length2 = 0.1 },
            ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 90 },
            InternalLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 60 },
            TabConfig = new NormalTab { Size = 0.04 },
            SupportTabs = true,
        };

        Assert.True(MachineCuttingDefaults.WriteTo(machine, nest, parameters));

        var thickness = machine.Materials[0].Thicknesses[0];
        Assert.Equal("Line", thickness.LeadIn.Type);
        Assert.Equal(0.125, thickness.LeadIn.Length);
        Assert.Equal(2.54, thickness.LeadOut.Length, 9);
        Assert.Equal(0.04 * 25.4, thickness.Cutting.TabSize, 9);
    }

    [Fact]
    public void WriteTo_RefusesAThicknessTheMachineDoesNotHave()
    {
        var machine = Machine();

        Assert.False(MachineCuttingDefaults.WriteTo(machine, NestOf(0.75), new CuttingParameters()));
    }

    [Fact]
    public void CuttingOf_AndToCuttingConfig_RoundTrip()
    {
        var copy = MachineCuttingDefaults.ToCuttingConfig(MachineCuttingDefaults.CuttingOf(ThinTabs));

        Assert.Equal(JsonSerializer.Serialize(ThinTabs), JsonSerializer.Serialize(copy));
        Assert.Equal(0.03, MachineCuttingDefaults.CuttingOf(null).TabConfig.Size);
    }

    [Fact]
    public void MachineJson_KeepsFieldsANewerOpenNestWrote()
    {
        var json = SharedMachineProvider.Serialize(Machine());
        var text = json
            .Replace("\"value\": 0.25,", "\"value\": 0.25,\n\"futureThickness\": { \"a\": 1 },")
            .Replace("\"tabs\": true,", "\"tabs\": true,\n\"futureTab\": 7,");
        Assert.NotEqual(json, text);

        var machine = JsonSerializer.Deserialize<MachineConfig>(text, Options);
        var saved = SharedMachineProvider.Serialize(machine);

        Assert.Contains("\"futureThickness\"", saved);
        Assert.Contains("\"futureTab\": 7", saved);
    }

    [Fact]
    public void MachineJson_WithoutCuttingSettingsReadsAsNotSet()
    {
        var json = SharedMachineProvider.Serialize(Machine()).Replace("\"cutting\":", "\"oldCutting\":");

        var machine = JsonSerializer.Deserialize<MachineConfig>(json, Options);

        Assert.All(machine.Materials[0].Thicknesses, t => Assert.Null(t.Cutting));
    }

    [Fact]
    public void GetReplaceError_RefusesAMachineThatDropsAThicknesssCuttingSettings()
    {
        var stored = SharedMachineProvider.Serialize(Machine());
        var old = Without(stored, "cutting");

        Assert.NotNull(SharedSettingRules.GetReplaceError(SharedSettingKinds.Machine, stored, old));
    }

    [Fact]
    public void GetReplaceError_AllowsClearedSettingsRemovedThicknessesAndUnsetFields()
    {
        var machine = Machine();
        var stored = SharedMachineProvider.Serialize(machine);

        machine.Materials[0].Thicknesses[0].Cutting = null;
        Assert.Null(SharedSettingRules.GetReplaceError(SharedSettingKinds.Machine, stored, SharedMachineProvider.Serialize(machine)));

        machine.Materials[0].Thicknesses.RemoveAt(1);
        Assert.Null(SharedSettingRules.GetReplaceError(SharedSettingKinds.Machine, stored, SharedMachineProvider.Serialize(machine)));

        // An older build saving a machine whose thicknesses never had cutting settings is fine.
        var plain = Machine();
        foreach (var thickness in plain.Materials[0].Thicknesses)
            thickness.Cutting = null;
        var plainJson = SharedMachineProvider.Serialize(plain);
        Assert.Null(SharedSettingRules.GetReplaceError(SharedSettingKinds.Machine, plainJson, Without(plainJson, "cutting")));

        Assert.Null(SharedSettingRules.GetReplaceError("post", "{\"a\":1}", "{}"));
    }

    // The JSON with every property of that name removed, as a build that does not know it writes.
    private static string Without(string json, string property)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        Remove(node, property);
        return node.ToJsonString();
    }

    private static void Remove(System.Text.Json.Nodes.JsonNode node, string property)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                obj.Remove(property);
                foreach (var (_, child) in obj.ToList())
                    if (child != null)
                        Remove(child, property);
                break;
            case System.Text.Json.Nodes.JsonArray array:
                foreach (var child in array)
                    if (child != null)
                        Remove(child, property);
                break;
        }
    }
}
