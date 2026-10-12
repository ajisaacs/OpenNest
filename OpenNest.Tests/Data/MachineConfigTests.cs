using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class MachineConfigTests
{
    private static MachineConfig CreateTestMachine()
    {
        return new MachineConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Laser",
            Type = MachineType.Laser,
            Units = UnitSystem.Inches,
            Materials = new List<MaterialConfig>
            {
                new()
                {
                    Name = "Mild Steel",
                    Grade = "A36",
                    Density = 0.2836,
                    Thicknesses = new List<ThicknessConfig>
                    {
                        new()
                        {
                            Value = 0.250,
                            Kerf = 0.012,
                            AssistGas = "O2",
                            LeadIn = new LeadConfig { Type = "Arc", Radius = 0.25 },
                            LeadOut = new LeadConfig { Type = "Line", Length = 0.125 },
                            PlateSizes = new List<string> { "60x120", "48x96" },
                        },
                        new()
                        {
                            Value = 0.500,
                            Kerf = 0.020,
                            AssistGas = "O2",
                            LeadIn = new LeadConfig { Type = "Arc", Radius = 0.375 },
                            LeadOut = new LeadConfig { Type = "Line", Length = 0.25 },
                            PlateSizes = new List<string> { "60x120" },
                        },
                    },
                },
                new()
                {
                    Name = "Stainless Steel",
                    Grade = "304",
                    Density = 0.289,
                    Thicknesses = new List<ThicknessConfig>
                    {
                        new()
                        {
                            Value = 0.250,
                            Kerf = 0.014,
                            AssistGas = "N2",
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void GetParameters_ExactMatch_ReturnsThickness()
    {
        var machine = CreateTestMachine();
        var result = machine.GetParameters("Mild Steel", 0.250);
        Assert.NotNull(result);
        Assert.Equal(0.012, result.Kerf);
        Assert.Equal("O2", result.AssistGas);
    }

    [Fact]
    public void GetParameters_WithinTolerance_ReturnsThickness()
    {
        var machine = CreateTestMachine();
        var result = machine.GetParameters("Mild Steel", 0.250001);
        Assert.NotNull(result);
        Assert.Equal(0.012, result.Kerf);
    }

    [Fact]
    public void GetParameters_NoMatch_ReturnsNull()
    {
        var machine = CreateTestMachine();
        var result = machine.GetParameters("Mild Steel", 0.375);
        Assert.Null(result);
    }

    [Fact]
    public void GetParameters_CaseInsensitiveMaterial()
    {
        var machine = CreateTestMachine();
        var result = machine.GetParameters("mild steel", 0.250);
        Assert.NotNull(result);
        Assert.Equal(0.012, result.Kerf);
    }

    [Fact]
    public void GetParameters_UnknownMaterial_ReturnsNull()
    {
        var machine = CreateTestMachine();
        var result = machine.GetParameters("Titanium", 0.250);
        Assert.Null(result);
    }

    [Fact]
    public void GetMaterial_ReturnsMaterialByName()
    {
        var machine = CreateTestMachine();
        var result = machine.GetMaterial("Stainless Steel");
        Assert.NotNull(result);
        Assert.Equal("304", result.Grade);
    }

    [Fact]
    public void GetMaterial_CaseInsensitive()
    {
        var machine = CreateTestMachine();
        var result = machine.GetMaterial("stainless steel");
        Assert.NotNull(result);
    }

    [Fact]
    public void GetMaterial_NotFound_ReturnsNull()
    {
        var machine = CreateTestMachine();
        var result = machine.GetMaterial("Titanium");
        Assert.Null(result);
    }

    private static MachineConfig CreateMultiGradeMachine()
    {
        return new MachineConfig
        {
            Materials = new List<MaterialConfig>
            {
                new()
                {
                    Name = "Mild Steel",
                    Grade = "A36",
                    Thicknesses = new List<ThicknessConfig>
                    {
                        new() { Value = 0.250, Kerf = 0.012 },
                    },
                },
                new()
                {
                    Name = "Mild Steel",
                    Grade = "A1011",
                    Thicknesses = new List<ThicknessConfig>
                    {
                        new() { Value = 0.250, Kerf = 0.010 },
                    },
                },
            },
        };
    }

    [Fact]
    public void GetMaterial_WithGrade_ReturnsMatchingGrade()
    {
        var machine = CreateMultiGradeMachine();

        var a36 = machine.GetMaterial("Mild Steel", "A36");
        var a1011 = machine.GetMaterial("Mild Steel", "A1011");

        Assert.NotNull(a36);
        Assert.NotNull(a1011);
        Assert.Equal(0.012, a36.Thicknesses[0].Kerf);
        Assert.Equal(0.010, a1011.Thicknesses[0].Kerf);
    }

    [Fact]
    public void GetMaterial_WithUnknownGrade_ReturnsNull()
    {
        var machine = CreateMultiGradeMachine();
        var result = machine.GetMaterial("Mild Steel", "A572");
        Assert.Null(result);
    }

    [Fact]
    public void GetGrades_ReturnsAllGradesForName()
    {
        var machine = CreateMultiGradeMachine();
        var grades = machine.GetGrades("mild steel").Select(m => m.Grade).ToList();
        Assert.Equal(new[] { "A36", "A1011" }, grades);
    }

    [Fact]
    public void GetParameters_WithGrade_ReturnsThicknessForThatGrade()
    {
        var machine = CreateMultiGradeMachine();

        var a36 = machine.GetParameters("Mild Steel", "A36", 0.250);
        var a1011 = machine.GetParameters("Mild Steel", "A1011", 0.250);

        Assert.NotNull(a36);
        Assert.NotNull(a1011);
        Assert.Equal(0.012, a36.Kerf);
        Assert.Equal(0.010, a1011.Kerf);
    }
}
