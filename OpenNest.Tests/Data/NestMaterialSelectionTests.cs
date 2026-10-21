using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestMaterialSelectionTests
{
    [Fact]
    public void Apply_UnchangedNameAndGrade_KeepsDensity()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "Stainless", "304");

        Assert.Equal("Stainless", result.Name);
        Assert.Equal("304", result.Grade);
        Assert.Equal(0.289, result.Density);
    }

    [Fact]
    public void Apply_NameDifferingOnlyInSpacingAndCase_KeepsGradeAndDensityAndTakesTypedName()
    {
        var current = new Material("Cold Rolled Steel", "1008", 0.284);

        var result = NestMaterialSelection.Apply(current, " cold  rolled\tsteel ", "1008");

        Assert.Equal(" cold  rolled\tsteel ", result.Name);
        Assert.Equal("1008", result.Grade);
        Assert.Equal(0.284, result.Density);
    }

    [Fact]
    public void Apply_ChangedName_GetsNameOnly()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "Aluminum", "");

        Assert.Equal("Aluminum", result.Name);
        Assert.True(string.IsNullOrEmpty(result.Grade));
        Assert.Equal(0, result.Density);
    }

    [Fact]
    public void Apply_ClearedName_GetsEmptyNameOnly()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "", "");

        Assert.Equal("", result.Name);
        Assert.True(string.IsNullOrEmpty(result.Grade));
        Assert.Equal(0, result.Density);
    }

    [Fact]
    public void Apply_NoCurrentMaterial_GetsNameOnly()
    {
        var result = NestMaterialSelection.Apply(null, "Stainless", null);

        Assert.Equal("Stainless", result.Name);
        Assert.True(string.IsNullOrEmpty(result.Grade));
        Assert.Equal(0, result.Density);
    }

    [Fact]
    public void Apply_ReturnsCopy_LeavingCurrentMaterialUnchanged()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "stainless", "304");
        result.Grade = "316";
        result.Density = 1;

        Assert.NotSame(current, result);
        Assert.Equal("Stainless", current.Name);
        Assert.Equal("304", current.Grade);
        Assert.Equal(0.289, current.Density);
    }

    [Fact]
    public void Apply_ChangedGrade_TakesGradeWithoutDensity()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "Stainless", "316");

        Assert.Equal("Stainless", result.Name);
        Assert.Equal("316", result.Grade);
        Assert.Equal(0, result.Density);
    }

    [Fact]
    public void Apply_ChangedNameWithGrade_TakesNameAndGrade()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.Apply(current, "Aluminum", "5052");

        Assert.Equal("Aluminum", result.Name);
        Assert.Equal("5052", result.Grade);
        Assert.Equal(0, result.Density);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ApplyRecorded_BlankGradeWithSameName_KeepsArchivedGradeAndDensity(string? recordedGrade)
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.ApplyRecorded(current, "stainless", recordedGrade);

        Assert.Equal("stainless", result.Name);
        Assert.Equal("304", result.Grade);
        Assert.Equal(0.289, result.Density);
    }

    [Fact]
    public void ApplyRecorded_BlankGradeWithChangedName_GetsNameOnly()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.ApplyRecorded(current, "Aluminum", "");

        Assert.Equal("Aluminum", result.Name);
        Assert.Equal("", result.Grade);
        Assert.Equal(0, result.Density);
    }

    [Fact]
    public void ApplyRecorded_RecordedGrade_Wins()
    {
        var current = new Material("Stainless", "304", 0.289);

        var result = NestMaterialSelection.ApplyRecorded(current, "Stainless", "316");

        Assert.Equal("316", result.Grade);
        Assert.Equal(0, result.Density);
    }

    [Theory]
    [InlineData("Carbon Steel", "CARBON STEEL")]
    [InlineData("  carbon \t steel\n", "CARBON STEEL")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void SharedListNamesKey_TrimsCollapsesWhitespaceAndUppercases(string? name, string expected)
    {
        Assert.Equal(expected, SharedListNames.Key(name));
    }
}
