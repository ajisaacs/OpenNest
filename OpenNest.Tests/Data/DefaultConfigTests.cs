using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DefaultConfigTests : IDisposable
{
    private readonly string _testDir;

    public DefaultConfigTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void EnsureDefaults_EmptyDirectory_CopiesDefaultConfig()
    {
        var provider = new LocalJsonProvider(_testDir);
        provider.EnsureDefaults();
        var machines = provider.GetMachines();
        Assert.Single(machines);
        Assert.Equal("CL-980", machines[0].Name);
    }

    [Fact]
    public void EnsureDefaults_ExistingFiles_DoesNotCopy()
    {
        var provider = new LocalJsonProvider(_testDir);
        var existing = new MachineConfig { Name = "My Machine" };
        provider.SaveMachine(existing);

        provider.EnsureDefaults();

        var machines = provider.GetMachines();
        Assert.Single(machines);
        Assert.Equal("My Machine", machines[0].Name);
    }

    [Fact]
    public void DefaultConfig_HasValidStructure()
    {
        var provider = new LocalJsonProvider(_testDir);
        provider.EnsureDefaults();

        var machines = provider.GetMachines();
        var config = provider.GetMachine(machines[0].Id);

        Assert.NotNull(config);
        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal(MachineType.Laser, config.Type);
        Assert.Equal(UnitSystem.Inches, config.Units);
        Assert.NotEmpty(config.Materials);

        foreach (var material in config.Materials)
        {
            Assert.False(string.IsNullOrWhiteSpace(material.Name));
            Assert.NotEmpty(material.Thicknesses);

            foreach (var thickness in material.Thicknesses)
            {
                Assert.True(thickness.Value > 0);
                Assert.True(thickness.Kerf > 0);
                Assert.False(string.IsNullOrWhiteSpace(thickness.AssistGas));
                Assert.NotNull(thickness.LeadIn);
                Assert.NotNull(thickness.LeadOut);
                Assert.NotNull(thickness.CutOff);
            }
        }
    }
}
