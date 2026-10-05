using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Tests.CuttingPlanning;

public class OwnedCuttingParameterTests
{
    public static IEnumerable<object[]> Styles()
    {
        foreach (var leadIn in new LeadIn[] { new NoLeadIn(), new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
            new ArcLeadIn { Radius = 0.2 }, new LineArcLeadIn { LineLength = 0.1, ArcRadius = 0.2, ApproachAngle = 120 },
            new LineLineLeadIn { Length1 = 0.1, Length2 = 0.2, ApproachAngle1 = 40, ApproachAngle2 = -30 },
            new CleanHoleLeadIn { LineLength = 0.1, ArcRadius = 0.2, Kerf = 0.01 } })
            foreach (var leadOut in new LeadOut[] { new NoLeadOut(), new LineLeadOut { Length = 0.4, ApproachAngle = 80 }, new ArcLeadOut { Radius = 0.3 } })
                foreach (var tab in new Tab[] { new NormalTab { Size = 0.2, CutoutMinWidth = 1, CutoutMaxWidth = 5, CutoutMinHeight = 2, CutoutMaxHeight = 6 },
                    new BreakerTab { Size = 0.3, BreakerDepth = 0.1, BreakerLeadInLength = 0.2, BreakerAngle = 30 },
                    new MachineTab { Size = 0.4, MachineTabId = 7 } })
                    yield return [leadIn, leadOut, tab];
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Copy_PreservesEveryMemberAndBreaksEveryMutableAlias(LeadIn leadIn, LeadOut leadOut, Tab tab)
    {
        tab.TabLeadIn = leadIn; tab.TabLeadOut = leadOut;
        var source = new CuttingParameters
        {
            Id = 17,
            MachineName = "machine",
            MaterialName = "material",
            Grade = "grade",
            Thickness = 0.4,
            Kerf = 0.01,
            PartSpacing = 0.1,
            PierceClearance = 0.05,
            ExternalLeadIn = leadIn,
            InternalLeadIn = leadIn,
            ArcCircleLeadIn = leadIn,
            ExternalLeadOut = leadOut,
            InternalLeadOut = leadOut,
            ArcCircleLeadOut = leadOut,
            TabConfig = tab,
            TabsEnabled = true,
            AutoTabMinSize = 0.1,
            AutoTabMaxSize = 0.5,
            RoundLeadInAngles = true,
            LeadInAngleIncrement = 30,
            Sequencing = new SequenceParameters
            {
                Method = SequenceMethod.LeftSide,
                SmallCutoutWidth = 2,
                SmallCutoutHeight = 3,
                MediumCutoutWidth = 9,
                MediumCutoutHeight = 10,
                DistanceMediumSmall = 4,
                AlternateRowsColumns = false,
                AlternateCutoutsWithinRowColumn = false,
                MinDistanceBetweenRowsColumns = 0.3
            },
            Assignment = new AssignmentParameters { Method = SequenceMethod.EdgeStart, Preference = "test", MinGeometryLength = 0.04 }
        };
        var copy = OwnedCuttingParameters.Copy(source);
        AssertOwnedEqual(source, copy);
        var secondCopy = OwnedCuttingParameters.Copy(copy);
        Mutate(source, new HashSet<object>(ReferenceEqualityComparer.Instance));
        AssertOwnedEqual(secondCopy, copy);
    }

    private static void AssertOwnedEqual(object? source, object? copy)
    {
        Assert.Equal(source?.GetType(), copy?.GetType());
        if (source == null) return;
        Assert.NotSame(source, copy);
        foreach (var property in source.GetType().GetProperties())
        {
            var a = property.GetValue(source)!; var b = property.GetValue(copy);
            if (property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                Assert.Equal(a, b);
            else
                AssertOwnedEqual(a, b);
        }
    }

    private static void Mutate(object? source, HashSet<object> visited)
    {
        if (source == null || !visited.Add(source)) return;
        foreach (var property in source.GetType().GetProperties())
        {
            var value = property.GetValue(source)!;
            if (property.PropertyType == typeof(double)) property.SetValue(source, (double)value + 100);
            else if (property.PropertyType == typeof(bool)) property.SetValue(source, !(bool)value);
            else if (property.PropertyType == typeof(int)) property.SetValue(source, (int)value + 10);
            else if (property.PropertyType == typeof(string)) property.SetValue(source, "mutated");
            else if (property.PropertyType.IsEnum) property.SetValue(source, Enum.ToObject(property.PropertyType, 100));
            else Mutate(value, visited);
        }
    }

    [Theory]
    [InlineData("null-settings")]
    [InlineData("null-sequence")]
    [InlineData("null-assignment")]
    [InlineData("null-lead")]
    [InlineData("null-leadout")]
    [InlineData("null-tab")]
    [InlineData("dimension")]
    [InlineData("angle")]
    [InlineData("increment")]
    [InlineData("enum")]
    [InlineData("tab-lead")]
    [InlineData("custom-in")]
    [InlineData("custom-out")]
    [InlineData("custom-tab")]
    [InlineData("custom-settings")]
    [InlineData("custom-sequence")]
    [InlineData("custom-assignment")]
    public void Copy_RejectsInvalidOrUnsupportedSettings(string fault)
    {
        var source = new CuttingParameters();
        switch (fault)
        {
            case "null-settings": source = null; break;
            case "null-sequence": source.Sequencing = null; break;
            case "null-assignment": source.Assignment = null; break;
            case "null-lead": source.ArcCircleLeadIn = null; break;
            case "null-leadout": source.InternalLeadOut = null; break;
            case "null-tab": source.TabsEnabled = true; break;
            case "dimension": source.PartSpacing = -1; break;
            case "angle": source.ExternalLeadIn = new LineLeadIn { ApproachAngle = double.NaN }; break;
            case "increment": source.LeadInAngleIncrement = 0; break;
            case "enum": source.Assignment.Method = (SequenceMethod)6; break;
            case "tab-lead": source.TabConfig = new NormalTab { TabLeadIn = new ArcLeadIn { Radius = double.PositiveInfinity } }; break;
            case "custom-in": source.InternalLeadIn = new CustomLeadIn(); break;
            case "custom-out": source.ArcCircleLeadOut = new CustomLeadOut(); break;
            case "custom-tab": source.TabConfig = new CustomTab(); break;
            case "custom-settings": source = new CustomParameters(); break;
            case "custom-sequence": source.Sequencing = new CustomSequence(); break;
            case "custom-assignment": source.Assignment = new CustomAssignment(); break;
        }
        Assert.ThrowsAny<Exception>(() => OwnedCuttingParameters.Copy(source));
        Assert.ThrowsAny<Exception>(() => PreparedContours.Capture(ExplicitContourTests.Square(false), source));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Copy_RejectsEveryInvalidDimension(double bad)
    {
        foreach (var sample in Styles())
        {
            var source = new CuttingParameters { ExternalLeadIn = (LeadIn)sample[0], ExternalLeadOut = (LeadOut)sample[1], TabConfig = (Tab)sample[2] };
            foreach (var owner in new object[] { source, source.Sequencing, source.Assignment, source.ExternalLeadIn, source.ExternalLeadOut, source.TabConfig })
                foreach (var property in owner.GetType().GetProperties().Where(p => p.PropertyType == typeof(double) && !p.Name.Contains("Angle")))
                {
                    var original = property.GetValue(owner);
                    property.SetValue(owner, bad);
                    Assert.Throws<ArgumentException>(() => OwnedCuttingParameters.Copy(source));
                    property.SetValue(owner, original);
                }
        }
    }

    private sealed class CustomLeadIn : LineLeadIn { }
    private sealed class CustomLeadOut : ArcLeadOut { }
    private sealed class CustomTab : NormalTab { }
    private sealed class CustomParameters : CuttingParameters { }
    private sealed class CustomSequence : SequenceParameters { }
    private sealed class CustomAssignment : AssignmentParameters { }
}
