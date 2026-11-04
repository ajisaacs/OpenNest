using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class MaterialDensityLookupTests
{
    private static MaterialConfig M(string name, string grade, double density) =>
        new() { Name = name, Grade = grade, Density = density };

    private static MachineConfig Machine(params MaterialConfig[] materials) =>
        new() { Name = "CL-980", Materials = materials.ToList() };

    [Fact]
    public void Resolve_PrefersTheNestMachineExactGrade()
    {
        var machine = Machine(M("Stainless Steel", "310", 0.287));
        var catalog = new[] { M("Stainless Steel", "310", 0.3) };

        Assert.Equal(0.287, MaterialDensityLookup.Resolve("Stainless Steel", "310", machine, catalog));
    }

    [Fact]
    public void Resolve_ExactGradeOnAnotherMachineBeatsTheSameNameOnTheNestMachine()
    {
        // N26-295: CL-980 lists only 304; the 310 grade lives on another machine.
        var machine = Machine(M("Stainless Steel", "304", 0.289));
        var catalog = new[] { M("Stainless Steel", "304", 0.289), M("stainless steel", " 310 ", 0.287) };

        Assert.Equal(0.287, MaterialDensityLookup.Resolve("Stainless Steel", "310", machine, catalog));
    }

    [Fact]
    public void Resolve_FallsBackToTheMaterialNameWhenNoGradeMatches()
    {
        var machine = Machine(M("Stainless Steel", "304", 0.289));

        Assert.Equal(0.289, MaterialDensityLookup.Resolve("Stainless Steel", "17-4", machine, null));
    }

    [Fact]
    public void Resolve_SkipsEntriesWithoutADensity()
    {
        var machine = Machine(M("Stainless Steel", "310", 0));
        var catalog = new[] { M("Stainless Steel", "310", 0.287) };

        Assert.Equal(0.287, MaterialDensityLookup.Resolve("Stainless Steel", "310", machine, catalog));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Brass")]
    public void Resolve_IsZeroWhenNothingMatches(string? name)
    {
        var machine = Machine(M("Stainless Steel", "310", 0.287));

        Assert.Equal(0, MaterialDensityLookup.Resolve(name, "310", machine, null));
    }
}
