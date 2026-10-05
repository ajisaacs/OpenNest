using System.Globalization;
using System.Reflection;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Delta-review regressions for settings freshness: capture must see accepted custom settings
/// state exactly, must execute no foreign getter or enumerator, must never throw out of a
/// commit, and must be culture-independent. Unsupported shapes refuse conservatively (Stale),
/// never silently compare equal.
/// </summary>
public class SettingsFreshnessTests
{
    [Fact]
    public void Capture_DictionarySettingsEditInPlace_IsStale()
    {
        var settings = new DictionarySettings();
        settings.Values["leadLength"] = 0.3;
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        settings.Values["leadLength"] = 9;
        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([result]).Status);
    }

    [Fact]
    public void Capture_PropertyBackedStructSettingsEdit_IsStale()
    {
        var settings = new StructSettings { Extra = new ScalarSettings { Length = 0.3 } };
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        settings.Extra = new ScalarSettings { Length = 9 };
        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([result]).Status);
    }

    [Fact]
    public void Capture_DeepSettingsChain_IsFullyCompared()
    {
        var root = new Chain();
        var leaf = root;
        for (var i = 0; i < 8; i++)
        {
            leaf.Next = new Chain();
            leaf = leaf.Next;
        }
        var settings = new DeepSettings { Extra = root };
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);

        var (plate2, _) = SinglePart(settings);
        var second = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate2));
        Assert.Equal(CuttingPlanStatus.Ready, second.Status);
        leaf.Length = 9;
        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([second]).Status);
    }

    [Fact]
    public void Capture_UnsupportedSettingsDepth_RefusesConservatively()
    {
        var root = new Chain();
        var leaf = root;
        for (var i = 0; i < 40; i++)
        {
            leaf.Next = new Chain();
            leaf = leaf.Next;
        }
        var settings = new DeepSettings { Extra = root };
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        // The deep leaf cannot be captured; an edit there must still never apply.
        leaf.Length = 9;
        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([result]).Status);
        var (untouched, _) = SinglePart(new DeepSettings { Extra = new Chain() });
        var other = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(untouched));
        Assert.Equal(CuttingPlanStatus.Ready, other.Status);
    }

    [Fact]
    public void Capture_ReadsNoForeignGetterState()
    {
        var settings = new MutatingSettings();
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, settings.Kerf); // The nominally read-only capture never ran ++Kerf.
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
    }

    [Fact]
    public void Apply_FailingSettingsEnumerable_IsStaleWithoutException()
    {
        var settings = new EnumerableSettings();
        var (plate, _) = SinglePart(settings);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        settings.EnableFailure();
        var commit = CuttingPlanService.Apply([result]);
        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
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
                    tab.TabLeadIn = lead;
                    tab.TabLeadOut = leadOut;
                    var one = StateFingerprint.Of(settings);
                    Assert.NotEqual(StateFingerprint.Invalid, one);
                    Assert.Equal(one, StateFingerprint.Of(settings));
                    settings.Kerf = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
                    Assert.NotEqual(one, StateFingerprint.Of(settings));
                }
    }

    [Fact]
    public void Fingerprint_CycleThroughLeadInIsDeterministic()
    {
        var tab = new NormalTab();
        var settings = new CuttingParameters { TabConfig = tab };
        tab.TabLeadIn = new LineLeadIn { Length = 1 };
        tab.TabLeadOut = new LineLeadOut { Length = 2 };
        var one = StateFingerprint.Of(settings);
        Assert.NotEqual(StateFingerprint.Invalid, one);
        Assert.Equal(one, StateFingerprint.Of(settings));
        ((LineLeadIn)tab.TabLeadIn).Length = 3;
        Assert.NotEqual(one, StateFingerprint.Of(settings));
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

    private sealed class Chain
    {
        public Chain? Next { get; set; }
        public double Length { get; set; }
    }

    private sealed class DeepSettings : CuttingParameters
    {
        public Chain? Extra { get; set; }
    }

    private sealed class MutatingSettings : CuttingParameters
    {
        public double Bump => ++Kerf;
    }

    private sealed class EnumerableSettings : CuttingParameters
    {
        private bool fail;

        public void EnableFailure() => fail = true;

        public IEnumerable<int> Values
        {
            get
            {
                yield return 1;
                if (fail)
                    throw new IOException("changed enumerable");
            }
        }
    }
}
