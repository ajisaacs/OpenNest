using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class LocalJsonProviderTests : IDisposable
{
    private readonly string _testDir;

    public LocalJsonProviderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private LocalJsonProvider CreateProvider() => new(_testDir);

    [Fact]
    public void GetMachines_EmptyDirectory_ReturnsEmpty()
    {
        var provider = CreateProvider();
        var result = provider.GetMachines();
        Assert.Empty(result);
    }

    [Fact]
    public void SaveMachine_ThenGetMachine_RoundTrips()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig
        {
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
                            Name = "3 ga",
                            Kerf = 0.012,
                            AssistGas = "O2",
                            LeadIn = new LeadConfig
                            {
                                Type = "Arc",
                                Length = 0.25,
                                Angle = 90.0,
                                Radius = 0.125,
                            },
                            LeadOut = new LeadConfig { Type = "Line", Length = 0.125 },
                            CutOff = new CutOffConfig
                            {
                                PartClearance = 0.5,
                                Overtravel = 0.25,
                                Direction = "AwayFromOrigin",
                                MinSegmentLength = 1.0,
                            },
                            PlateSizes = new List<string> { "60x120", "48x96" },
                        },
                    },
                },
            },
        };

        provider.SaveMachine(machine);
        var loaded = provider.GetMachine(machine.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Test Laser", loaded.Name);
        Assert.Equal(MachineType.Laser, loaded.Type);
        Assert.Equal(UnitSystem.Inches, loaded.Units);
        Assert.Single(loaded.Materials);

        var mat = loaded.Materials[0];
        Assert.Equal("Mild Steel", mat.Name);
        Assert.Equal("A36", mat.Grade);
        Assert.Equal(0.2836, mat.Density);
        Assert.Single(mat.Thicknesses);

        var thick = mat.Thicknesses[0];
        Assert.Equal(0.250, thick.Value);
        Assert.Equal("3 ga", thick.Name);
        Assert.Equal(0.012, thick.Kerf);
        Assert.Equal("O2", thick.AssistGas);
        Assert.Equal("Arc", thick.LeadIn.Type);
        Assert.Equal(0.25, thick.LeadIn.Length);
        Assert.Equal("Line", thick.LeadOut.Type);
        Assert.Equal(0.125, thick.LeadOut.Length);
        Assert.Equal(0.5, thick.CutOff.PartClearance);
        Assert.Equal("AwayFromOrigin", thick.CutOff.Direction);
        Assert.Equal(new List<string> { "60x120", "48x96" }, thick.PlateSizes);
    }

    [Fact]
    public void GetMachines_ReturnsSummaries()
    {
        var provider = CreateProvider();
        var m1 = new MachineConfig { Name = "Laser A" };
        var m2 = new MachineConfig { Name = "Plasma B" };

        provider.SaveMachine(m1);
        provider.SaveMachine(m2);
        var summaries = provider.GetMachines();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Name == "Laser A" && s.Id == m1.Id);
        Assert.Contains(summaries, s => s.Name == "Plasma B" && s.Id == m2.Id);
    }

    [Fact]
    public void GetMachine_NotFound_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetMachine(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public void DeleteMachine_RemovesFile()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig { Name = "To Delete" };
        provider.SaveMachine(machine);

        provider.DeleteMachine(machine.Id);

        Assert.Null(provider.GetMachine(machine.Id));
        Assert.Empty(provider.GetMachines());
    }

    [Fact]
    public void SaveMachine_OverwritesExisting()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig { Name = "Original" };
        provider.SaveMachine(machine);

        machine.Name = "Updated";
        provider.SaveMachine(machine);

        var loaded = provider.GetMachine(machine.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded.Name);
        Assert.Single(provider.GetMachines());
    }

    [Fact]
    public void SaveMachine_PreservesSchemaVersion()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig { Name = "Versioned", SchemaVersion = 1 };

        provider.SaveMachine(machine);
        var loaded = provider.GetMachine(machine.Id);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SchemaVersion);
    }

    [Fact]
    public void SaveMachine_RoundTripsTheSupportGrid()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig
        {
            Name = "Plasma",
            SupportGrid = new SupportGridSettings
            {
                Enabled = true,
                Units = Units.Millimeters,
                PitchX = 50,
                PitchY = 40,
                OffsetX = -5,
                StabilityMargin = 6,
            },
        };

        provider.SaveMachine(machine);
        var grid = provider.GetMachine(machine.Id)!.SupportGrid!;

        Assert.True(grid.Enabled);
        Assert.Equal(Units.Millimeters, grid.Units);
        Assert.Equal(50, grid.PitchX);
        Assert.Equal(40, grid.PitchY);
        Assert.Equal(-5, grid.OffsetX);
        Assert.Equal(6, grid.StabilityMargin);
    }

    [Fact]
    public void MachineWithoutSupportGrid_LoadsWithNone()
    {
        var provider = CreateProvider();
        var machine = new MachineConfig { Name = "Laser" };

        provider.SaveMachine(machine);

        Assert.Null(provider.GetMachine(machine.Id)!.SupportGrid);
    }
}
