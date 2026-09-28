using System.Text.Json;
using OpenNest.CNC.CuttingStrategy;
using CuttingParametersSerializer = OpenNest.IO.CuttingParametersSerializer;

namespace OpenNest.IO.Tests;

public class CuttingParametersSerializerTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("Line")]
    [InlineData("Arc")]
    [InlineData("LineArc")]
    [InlineData("CleanHole")]
    [InlineData("LineLine")]
    public void SettingsRoundTrip_PreservesEveryLeadInType(string type)
    {
        var leadIn = CreateLeadIn(type);
        var original = new CuttingParameters
        {
            ExternalLeadIn = leadIn,
            InternalLeadIn = leadIn,
            ArcCircleLeadIn = leadIn,
            TabConfig = new NormalTab { Size = 0.42, TabLeadIn = leadIn },
        };

        var restored = RoundTrip(original);

        AssertEquivalent(leadIn, restored.ExternalLeadIn);
        AssertEquivalent(leadIn, restored.InternalLeadIn);
        AssertEquivalent(leadIn, restored.ArcCircleLeadIn);
        AssertEquivalent(leadIn, restored.TabConfig.TabLeadIn);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Line")]
    [InlineData("Arc")]
    public void SettingsRoundTrip_PreservesEveryLeadOutType(string type)
    {
        var leadOut = CreateLeadOut(type);
        var original = new CuttingParameters
        {
            ExternalLeadOut = leadOut,
            InternalLeadOut = leadOut,
            ArcCircleLeadOut = leadOut,
            TabConfig = new NormalTab { Size = 0.42, TabLeadOut = leadOut },
        };

        var restored = RoundTrip(original);

        AssertEquivalent(leadOut, restored.ExternalLeadOut);
        AssertEquivalent(leadOut, restored.InternalLeadOut);
        AssertEquivalent(leadOut, restored.ArcCircleLeadOut);
        AssertEquivalent(leadOut, restored.TabConfig.TabLeadOut);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Machine")]
    [InlineData("Breaker")]
    public void SettingsRoundTrip_PreservesEveryTabType(string type)
    {
        var original = new CuttingParameters
        {
            TabsEnabled = true,
            TabConfig = CreateTab(type),
        };

        AssertEquivalent(original, RoundTrip(original));
    }

    [Theory]
    [InlineData(SequenceMethod.RightSide)]
    [InlineData(SequenceMethod.LeastCode)]
    [InlineData(SequenceMethod.Advanced)]
    [InlineData(SequenceMethod.BottomSide)]
    [InlineData(SequenceMethod.EdgeStart)]
    [InlineData(SequenceMethod.LeftSide)]
    [InlineData(SequenceMethod.RightSideAlt)]
    public void SettingsRoundTrip_PreservesAllFieldsAndSequenceMethods(SequenceMethod method)
    {
        var original = CreateParameters();
        original.Assignment.Method = method;
        original.Sequencing.Method = method;

        AssertEquivalent(original, RoundTrip(original));
    }

    [Fact]
    public void Deserialize_LegacySettings_PreservesValuesAndUsesMissingFieldDefaults()
    {
        const string json = """
            {
              "externalLeadIn": { "type": "LineLine", "length1": 0.2, "angle1": 31,
                                  "length2": 0.4, "angle2": 62 },
              "externalLeadOut": { "type": "Line", "length": 0.5, "approachAngle": 43 },
              "internalLeadIn": { "type": "Arc", "radius": 0.12 },
              "internalLeadOut": { "type": "Arc", "radius": 0.09, "gapSize": 0 },
              "arcCircleLeadIn": { "type": "CleanHole", "lineLength": 0.8,
                                   "arcRadius": 0.3, "kerf": 0.04 },
              "arcCircleLeadOut": { "type": "None" },
              "tabsEnabled": true,
              "tabWidth": 0.375,
              "pierceClearance": 0.0625
            }
            """;

        var restored = CuttingParametersSerializer.Deserialize(json);

        var lineLine = Assert.IsType<LineLineLeadIn>(restored.ExternalLeadIn);
        Assert.Equal(0.2, lineLine.Length1);
        Assert.Equal(31, lineLine.ApproachAngle1);
        Assert.Equal(0.4, lineLine.Length2);
        Assert.Equal(62, lineLine.ApproachAngle2);
        var lineOut = Assert.IsType<LineLeadOut>(restored.ExternalLeadOut);
        Assert.Equal(0.5, lineOut.Length);
        Assert.Equal(43, lineOut.ApproachAngle);
        Assert.Equal(0.12, Assert.IsType<ArcLeadIn>(restored.InternalLeadIn).Radius);
        Assert.Equal(0.09, Assert.IsType<ArcLeadOut>(restored.InternalLeadOut).Radius);
        var cleanHole = Assert.IsType<CleanHoleLeadIn>(restored.ArcCircleLeadIn);
        Assert.Equal(0.8, cleanHole.LineLength);
        Assert.Equal(0.3, cleanHole.ArcRadius);
        Assert.Equal(0.04, cleanHole.Kerf);
        Assert.IsType<NoLeadOut>(restored.ArcCircleLeadOut);
        Assert.True(restored.TabsEnabled);
        Assert.Equal(0.375, Assert.IsType<NormalTab>(restored.TabConfig).Size);
        Assert.Equal(0.0625, restored.PierceClearance);
        Assert.False(restored.RoundLeadInAngles);
        Assert.Equal(5, restored.LeadInAngleIncrement);
        Assert.Equal(0, restored.AutoTabMinSize);
        Assert.Equal(0, restored.AutoTabMaxSize);
        AssertEquivalent(new AssignmentParameters(), restored.Assignment);
        AssertEquivalent(new SequenceParameters(), restored.Sequencing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deserialize_LegacyNonpositiveAngleIncrement_UsesFiveDegrees(double increment)
    {
        var json = JsonSerializer.Serialize(new { leadInAngleIncrement = increment });

        Assert.Equal(5, CuttingParametersSerializer.Deserialize(json).LeadInAngleIncrement);
    }

    [Fact]
    public void Deserialize_EmptyObject_PreservesLegacyDefaults()
    {
        var restored = CuttingParametersSerializer.Deserialize("{}");

        Assert.IsType<NoLeadIn>(restored.ExternalLeadIn);
        Assert.IsType<NoLeadIn>(restored.InternalLeadIn);
        Assert.IsType<NoLeadIn>(restored.ArcCircleLeadIn);
        Assert.IsType<NoLeadOut>(restored.ExternalLeadOut);
        Assert.IsType<NoLeadOut>(restored.InternalLeadOut);
        Assert.IsType<NoLeadOut>(restored.ArcCircleLeadOut);
        Assert.Equal(0, Assert.IsType<NormalTab>(restored.TabConfig).Size);
        Assert.Equal(0, restored.PierceClearance);
        Assert.Equal(5, restored.LeadInAngleIncrement);
    }

    [Fact]
    public void Deserialize_JsonNull_ReturnsDomainDefaults()
    {
        AssertEquivalent(new CuttingParameters(), CuttingParametersSerializer.Deserialize("null"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"externalLeadIn\":17}")]
    public void Deserialize_MalformedSettings_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => CuttingParametersSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_UnknownLeadTypes_UsesNoLead()
    {
        var restored = CuttingParametersSerializer.Deserialize("""
            { "externalLeadIn": { "type": "FutureLead" },
              "externalLeadOut": { "type": "FutureLead" } }
            """);

        Assert.IsType<NoLeadIn>(restored.ExternalLeadIn);
        Assert.IsType<NoLeadOut>(restored.ExternalLeadOut);
    }

    [Fact]
    public void SettingsRoundTrip_NullTab_PreservesLegacyWidthFallback()
    {
        var original = new CuttingParameters { TabConfig = null };
        var json = CuttingParametersSerializer.Serialize(original);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(0.25, document.RootElement.GetProperty("tabWidth").GetDouble());
        Assert.DoesNotContain('\n', json);
        Assert.Equal(0.25, Assert.IsType<NormalTab>(
            CuttingParametersSerializer.Deserialize(json).TabConfig).Size);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Machine")]
    [InlineData("Breaker")]
    public void DtoRoundTrip_UsesNestJsonOptionsAndPreservesAllFields(string tabType)
    {
        var original = CreateParameters();
        original.TabConfig = CreateTab(tabType);
        original.LeadInAngleIncrement = 0;
        var dto = CuttingParametersSerializer.ToDto(original);

        var json = JsonSerializer.Serialize(dto, NestFormat.JsonOptions);
        var parsed = JsonSerializer.Deserialize<CuttingParametersDto>(json, NestFormat.JsonOptions);
        var restored = CuttingParametersSerializer.FromDto(parsed);

        AssertEquivalent(original, restored);
    }

    [Fact]
    public void DtoRoundTrip_NullParameters_RemainNull()
    {
        Assert.Null(CuttingParametersSerializer.ToDto(null));
        Assert.Null(CuttingParametersSerializer.FromDto(null));
    }

    [Fact]
    public void DtoRoundTrip_DefaultParameters_PreservesNullTab()
    {
        var original = new CuttingParameters();
        var dto = CuttingParametersSerializer.ToDto(original);
        var json = JsonSerializer.Serialize(dto, NestFormat.JsonOptions);
        var parsed = JsonSerializer.Deserialize<CuttingParametersDto>(json, NestFormat.JsonOptions);

        AssertEquivalent(original, CuttingParametersSerializer.FromDto(parsed));
    }

    [Fact]
    public void DtoRoundTrip_NullAssignmentAndSequencing_RemainNull()
    {
        var original = new CuttingParameters { Assignment = null, Sequencing = null };
        var dto = CuttingParametersSerializer.ToDto(original);
        var json = JsonSerializer.Serialize(dto, NestFormat.JsonOptions);
        var parsed = JsonSerializer.Deserialize<CuttingParametersDto>(json, NestFormat.JsonOptions);

        AssertEquivalent(original, CuttingParametersSerializer.FromDto(parsed));
    }

    [Fact]
    public void DtoMapping_SourceSnapshotAndRestoredParameters_AreIndependent()
    {
        var original = CreateParameters();
        var dto = CuttingParametersSerializer.ToDto(original);
        var restored = CuttingParametersSerializer.FromDto(dto);
        AssertEquivalent(original, restored);

        original.Assignment.Preference = "changed source";
        original.Sequencing.SmallCutoutWidth = 101;
        ((LineLineLeadIn)original.ExternalLeadIn).Length1 = 102;
        ((BreakerTab)original.TabConfig).BreakerDepth = 103;
        ((LineArcLeadIn)original.TabConfig.TabLeadIn).ArcRadius = 104;
        ((ArcLeadOut)original.TabConfig.TabLeadOut).Radius = 105;
        AssertEquivalent(CreateParameters(), CuttingParametersSerializer.FromDto(dto));

        dto.Assignment.Preference = "changed snapshot";
        dto.Sequencing.SmallCutoutWidth = 201;
        dto.ExternalLeadIn.Length1 = 202;
        dto.TabConfig.BreakerDepth = 203;
        dto.TabConfig.TabLeadIn.ArcRadius = 204;
        dto.TabConfig.TabLeadOut.Radius = 205;
        AssertEquivalent(CreateParameters(), restored);
    }

    [Fact]
    public void Deserialize_UnknownTabType_DoesNotInventACutStrategy()
    {
        var restored = CuttingParametersSerializer.Deserialize("""
            { "tabConfig": { "type": "FutureTab" }, "tabWidth": 0.7 }
            """);

        Assert.Null(restored.TabConfig);
    }

    private static CuttingParameters RoundTrip(CuttingParameters original) =>
        CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(original));

    private static CuttingParameters CreateParameters() => new()
    {
        Id = 47,
        MachineName = "Laser A",
        MaterialName = "Steel",
        Grade = "A36",
        Thickness = 0.1875,
        Kerf = 0.018,
        PartSpacing = 0.23,
        ExternalLeadIn = CreateLeadIn("LineLine"),
        ExternalLeadOut = CreateLeadOut("Line"),
        InternalLeadIn = CreateLeadIn("CleanHole"),
        InternalLeadOut = CreateLeadOut("Arc"),
        ArcCircleLeadIn = CreateLeadIn("LineArc"),
        ArcCircleLeadOut = CreateLeadOut("None"),
        PierceClearance = 0.17,
        RoundLeadInAngles = true,
        LeadInAngleIncrement = 13,
        AutoTabMinSize = 0.62,
        AutoTabMaxSize = 4.7,
        TabConfig = CreateTab("Breaker"),
        TabsEnabled = true,
        Assignment = new AssignmentParameters
        {
            Method = SequenceMethod.EdgeStart,
            Preference = "TAIL",
            MinGeometryLength = 0.37,
        },
        Sequencing = new SequenceParameters
        {
            Method = SequenceMethod.LeftSide,
            SmallCutoutWidth = 2.3,
            SmallCutoutHeight = 3.4,
            MediumCutoutWidth = 9.5,
            MediumCutoutHeight = 10.6,
            DistanceMediumSmall = 1.7,
            AlternateRowsColumns = false,
            AlternateCutoutsWithinRowColumn = false,
            MinDistanceBetweenRowsColumns = 0.8,
        },
    };

    private static LeadIn CreateLeadIn(string type) => type switch
    {
        "Line" => new LineLeadIn { Length = 0.37, ApproachAngle = 47 },
        "Arc" => new ArcLeadIn { Radius = 0.53 },
        "LineArc" => new LineArcLeadIn
        {
            LineLength = 0.43,
            ArcRadius = 0.29,
            ApproachAngle = 118,
        },
        "CleanHole" => new CleanHoleLeadIn { LineLength = 0.38, ArcRadius = 0.16, Kerf = 0.021 },
        "LineLine" => new LineLineLeadIn
        {
            Length1 = 0.24,
            ApproachAngle1 = 72,
            Length2 = 0.48,
            ApproachAngle2 = 36,
        },
        "None" => new NoLeadIn(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static LeadOut CreateLeadOut(string type) => type switch
    {
        "Line" => new LineLeadOut { Length = 0.21, ApproachAngle = 58 },
        "Arc" => new ArcLeadOut { Radius = 0.19 },
        "None" => new NoLeadOut(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static Tab CreateTab(string type)
    {
        var tab = type switch
        {
            "Normal" => (Tab)new NormalTab
            {
                CutoutMinWidth = 0.31,
                CutoutMinHeight = 0.52,
                CutoutMaxWidth = 3.7,
                CutoutMaxHeight = 4.6,
            },
            "Machine" => new MachineTab { MachineTabId = 29 },
            "Breaker" => new BreakerTab
            {
                BreakerDepth = 0.013,
                BreakerLeadInLength = 0.09,
                BreakerAngle = 26,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        tab.Size = 0.41;
        tab.TabLeadIn = CreateLeadIn("LineArc");
        tab.TabLeadOut = CreateLeadOut("Arc");
        return tab;
    }

    // Compare concrete runtime properties, including nested subtype fields, rather than
    // using the serializer under test as the equality oracle. All model objects must be owned.
    private static void AssertEquivalent(object? expected, object? actual)
    {
        if (expected == null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        var type = expected.GetType();
        Assert.Equal(type, actual.GetType());
        if (type.IsValueType || expected is string)
        {
            Assert.Equal(expected, actual);
            return;
        }

        Assert.NotSame(expected, actual);
        foreach (var property in type.GetProperties())
            AssertEquivalent(property.GetValue(expected), property.GetValue(actual));
    }
}
