using System.Globalization;
using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Settings freshness for plate-scoped plans. Only the exact built-in settings types are
/// captured, each member explicitly; any other runtime type is refused at capture without
/// running its code, and becoming unsupported after capture is Stale. The coverage tests
/// fail when a supported type gains state the fingerprint does not write.
/// </summary>
public class SettingsFreshnessTests
{
    private static readonly Type[] SupportedTypes =
    [
        typeof(CuttingParameters), typeof(SequenceParameters), typeof(AssignmentParameters),
        typeof(NoLeadIn), typeof(LineLeadIn), typeof(ArcLeadIn), typeof(LineArcLeadIn),
        typeof(LineLineLeadIn), typeof(CleanHoleLeadIn),
        typeof(NoLeadOut), typeof(LineLeadOut), typeof(ArcLeadOut),
        typeof(NormalTab), typeof(BreakerTab), typeof(MachineTab)
    ];

    [Theory]
    [InlineData("dictionary")]
    [InlineData("struct")]
    [InlineData("cycle")]
    [InlineData("date")]
    [InlineData("array")]
    [InlineData("mutating-getter")]
    [InlineData("failing-enumerable")]
    public void Plan_CustomSettingsType_IsUnsupportedWithoutRunningItsCode(string shape)
    {
        CuttingParameters settings = shape switch
        {
            "dictionary" => new DictionarySettings { Values = { ["leadLength"] = 0.3 } },
            "struct" => new StructSettings { Extra = new ScalarSettings { Length = 0.3 } },
            "cycle" => CycleSettings.Create(),
            "date" => new DateSettings { When = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc) },
            "array" => new ArraySettings { Values = new int[2, 3] },
            "mutating-getter" => new MutatingSettings(),
            "failing-enumerable" => new EnumerableSettings { Fail = true },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var (plate, part) = SinglePart(settings);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("cannot be captured exactly"));
        Assert.Equal(0, settings.Kerf); // A Bump => ++Kerf getter never ran.
        Assert.Same(settings, part.CuttingParameters);
    }

    [Theory]
    [InlineData("lead-in")]
    [InlineData("lead-out")]
    [InlineData("tab")]
    [InlineData("tab-lead-in")]
    [InlineData("sequence")]
    [InlineData("assignment")]
    [InlineData("plate")]
    public void Plan_CustomNestedOrPlateSettingsType_IsUnsupported(string slot)
    {
        var settings = new CuttingParameters();
        var (plate, _) = SinglePart(settings);
        switch (slot)
        {
            case "lead-in": settings.InternalLeadIn = new CustomLeadIn(); break;
            case "lead-out": settings.ArcCircleLeadOut = new CustomLeadOut(); break;
            case "tab": settings.TabConfig = new CustomTab(); break;
            case "tab-lead-in": settings.TabConfig = new NormalTab { TabLeadIn = new CustomLeadIn() }; break;
            case "sequence": settings.Sequencing = new CustomSequence(); break;
            case "assignment": settings.Assignment = new CustomAssignment(); break;
            case "plate": plate.CuttingParameters = new MutatingSettings(); break;
        }

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry,
            CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate)).Status);
    }

    [Theory]
    [InlineData("lead-in")]
    [InlineData("tab")]
    [InlineData("plate")]
    public void Apply_SettingsReplacedWithUnsupportedTypeAfterCapture_IsStaleWithoutException(string slot)
    {
        var settings = new CuttingParameters();
        var (plate, _) = SinglePart(settings);
        plate.CuttingParameters = new CuttingParameters();
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        switch (slot)
        {
            case "lead-in": settings.ExternalLeadIn = new CustomLeadIn(); break;
            case "tab": settings.TabConfig = new CustomTab(); break;
            case "plate": plate.CuttingParameters.Sequencing = new CustomSequence(); break;
        }

        CuttingCommitResult? commit = null;
        Assert.Null(Record.Exception(() => commit = CuttingPlanService.Apply([result])));
        Assert.Equal(CuttingCommitStatus.Stale, commit!.Status);
    }

    [Fact]
    public void Plan_DetachedRequestWithCustomSettings_IsUnaffected()
    {
        // Only plate-scoped capture records settings; detached part lists never fingerprint them.
        var (plate, _) = SinglePart(new MutatingSettings());
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(plate.Parts));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
    }

    [Fact]
    public void Fingerprint_SeesAnEditToEveryPublicPropertyOfEverySupportedType()
    {
        var edits = 0;
        foreach (var type in SupportedTypes)
        {
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            var instance = Activator.CreateInstance(type)!;
            var settings = Graph(instance);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0,
                    $"{type.Name}.{property.Name}: extend StateFingerprint for non-settable state.");
                var before = StateFingerprint.Of(settings);
                property.SetValue(instance, Different(property.GetValue(instance), property.PropertyType,
                    $"{type.Name}.{property.Name}"));
                Assert.NotEqual(before, StateFingerprint.Of(settings));
                edits++;
            }
        }
        Assert.True(edits > 40, $"Only {edits} property edits were exercised.");
    }

    [Fact]
    public void Fingerprint_SupportedTypesAreEveryBuiltInLeadAndTab()
    {
        var builtIn = typeof(LeadIn).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (typeof(LeadIn).IsAssignableFrom(t)
                || typeof(LeadOut).IsAssignableFrom(t) || typeof(Tab).IsAssignableFrom(t)))
            .OrderBy(t => t.FullName).ToArray();
        var supported = SupportedTypes
            .Where(t => typeof(LeadIn).IsAssignableFrom(t) || typeof(LeadOut).IsAssignableFrom(t)
                || typeof(Tab).IsAssignableFrom(t))
            .OrderBy(t => t.FullName).ToArray();
        Assert.Equal(builtIn, supported);
        foreach (var type in builtIn)
            StateFingerprint.Of(Graph(Activator.CreateInstance(type)!)); // Must not throw.
    }

    [Fact]
    public void Fingerprint_BuiltInSettingsAreDeterministicAndDetectSignedZero()
    {
        LeadIn[] leads = [new NoLeadIn(), new LineLeadIn(), new ArcLeadIn(), new LineArcLeadIn(),
            new LineLineLeadIn(), new CleanHoleLeadIn()];
        LeadOut[] outs = [new NoLeadOut(), new LineLeadOut(), new ArcLeadOut()];
        Tab[] tabs = [new NormalTab(), new BreakerTab(), new MachineTab()];
        foreach (var lead in leads)
            foreach (var leadOut in outs)
                foreach (var tab in tabs)
                {
                    // Shared instances across slots are allowed: they are values, not cycles.
                    tab.TabLeadIn = lead;
                    tab.TabLeadOut = leadOut;
                    var settings = new CuttingParameters
                    {
                        ExternalLeadIn = lead,
                        InternalLeadIn = lead,
                        ArcCircleLeadIn = lead,
                        ExternalLeadOut = leadOut,
                        InternalLeadOut = leadOut,
                        ArcCircleLeadOut = leadOut,
                        TabConfig = tab,
                        TabsEnabled = true
                    };
                    var one = StateFingerprint.Of(settings);
                    Assert.Equal(one, StateFingerprint.Of(settings));
                    settings.Kerf = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
                    Assert.NotEqual(one, StateFingerprint.Of(settings));
                }
    }

    [Fact]
    public void Fingerprint_TextFieldsCannotShiftIntoNeighbours()
    {
        var a = new CuttingParameters { MachineName = "a;", MaterialName = "b" };
        var b = new CuttingParameters { MachineName = "a", MaterialName = ";b" };
        var n = new CuttingParameters { MachineName = null };
        var e = new CuttingParameters { MachineName = "" };
        Assert.NotEqual(StateFingerprint.Of(a), StateFingerprint.Of(b));
        Assert.NotEqual(StateFingerprint.Of(n), StateFingerprint.Of(e));
    }

    [Fact]
    public void Commit_BuiltInSettingsAreCultureIndependent()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var settings = new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = -90 }
            };
            var (plate, _) = SinglePart(settings);
            var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
            Assert.Equal(CuttingPlanStatus.Ready, result.Status);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_ConfirmedSettingsEdit_StalesOnlyWhenAlsoLiveState(bool aliasesLive)
    {
        var method = typeof(CuttingPlanCommitTests).GetMethod("RegeneratedPlate",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var (_, plate, _, live) = ((Nest, Plate, Part, CuttingParameters))method
            .Invoke(null, [Vector.Zero])!;
        var confirmed = aliasesLive ? live : OwnedCuttingParameters.Copy(live);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: confirmed));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        ((LineLeadIn)confirmed.ExternalLeadIn).Length = 9;
        Assert.Equal(aliasesLive ? CuttingCommitStatus.Stale : CuttingCommitStatus.Applied,
            CuttingPlanService.Apply([result]).Status);
    }

    // Places one supported settings object somewhere the fingerprint of the root reaches it.
    private static CuttingParameters Graph(object instance) => instance switch
    {
        CuttingParameters p => p,
        SequenceParameters s => new CuttingParameters { Sequencing = s },
        AssignmentParameters a => new CuttingParameters { Assignment = a },
        LeadIn l => new CuttingParameters { ExternalLeadIn = l },
        LeadOut l => new CuttingParameters { ExternalLeadOut = l },
        Tab t => new CuttingParameters { TabConfig = t },
        _ => throw new ArgumentOutOfRangeException(nameof(instance))
    };

    private static object? Different(object? value, Type type, string member)
    {
        if (type == typeof(double))
            return (double)value! + 1.25;
        if (type == typeof(int))
            return (int)value! + 1;
        if (type == typeof(bool))
            return !(bool)value!;
        if (type == typeof(string))
            return ((string?)value ?? "") + "x";
        if (type.IsEnum)
            return Enum.GetValues(type).Cast<object>().First(v => !v.Equals(value));
        if (type == typeof(LeadIn))
            return new ArcLeadIn { Radius = 7.77 };
        if (type == typeof(LeadOut))
            return new ArcLeadOut { Radius = 7.77 };
        if (type == typeof(Tab))
            return new MachineTab { MachineTabId = 77 };
        if (type == typeof(SequenceParameters))
            return new SequenceParameters { SmallCutoutWidth = 7.77 };
        if (type == typeof(AssignmentParameters))
            return new AssignmentParameters { MinGeometryLength = 7.77 };
        Assert.Fail($"{member}: type {type.Name} is not fingerprinted; extend StateFingerprint.");
        return null;
    }

    private static (Plate Plate, Part Part) SinglePart(CuttingParameters settings)
    {
        var rectangle = typeof(CuttingDependencyTests).GetMethod("Rectangle",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var part = (Part)rectangle.Invoke(null, [10.0, 10.0, 2.0, 2.0])!;
        part.CuttingParameters = settings;
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);
        return (plate, part);
    }

    private sealed class DictionarySettings : CuttingParameters
    {
        public Dictionary<string, double> Values { get; } = [];
    }

    private struct ScalarSettings
    {
        public double Length { get; set; }
    }

    private sealed class StructSettings : CuttingParameters
    {
        public ScalarSettings Extra { get; set; }
    }

    private sealed class Node
    {
        public Node? Next { get; set; }
        public double Length { get; set; }
    }

    private sealed class CycleSettings : CuttingParameters
    {
        public Node? Root { get; set; }

        public static CycleSettings Create()
        {
            var root = new Node();
            root.Next = new Node { Next = root };
            return new CycleSettings { Root = root };
        }
    }

    private sealed class DateSettings : CuttingParameters
    {
        public DateTime When { get; set; }
    }

    private sealed class ArraySettings : CuttingParameters
    {
        public int[,]? Values { get; set; }
    }

    private sealed class MutatingSettings : CuttingParameters
    {
        public double Bump => ++Kerf;
    }

    private sealed class EnumerableSettings : CuttingParameters
    {
        public bool Fail { get; set; }

        public IEnumerable<int> Values
        {
            get
            {
                yield return 1;
                if (Fail)
                    throw new IOException("changed enumerable");
            }
        }
    }

    private sealed class CustomLeadIn : LineLeadIn { }
    private sealed class CustomLeadOut : ArcLeadOut { }
    private sealed class CustomTab : NormalTab { }
    private sealed class CustomSequence : SequenceParameters { }
    private sealed class CustomAssignment : AssignmentParameters { }
}
