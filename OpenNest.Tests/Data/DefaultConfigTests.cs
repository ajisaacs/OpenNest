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
        Assert.Equal("Generic Laser", machines[0].Name);
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

    [Fact]
    public void DefaultConfig_HasSensibleValues()
    {
        var provider = new LocalJsonProvider(_testDir);
        provider.EnsureDefaults();
        var config = provider.GetMachine(provider.GetMachines()[0].Id);

        foreach (var material in config.Materials)
        {
            Assert.True(material.Density > 0);
            var thicknesses = material.Thicknesses.OrderBy(t => t.Value).ToList();
            for (var i = 0; i < thicknesses.Count; i++)
            {
                var thickness = thicknesses[i];
                var label = $"{material.Name} {thickness.Value}";
                Assert.False(string.IsNullOrWhiteSpace(thickness.Name), label);
                Assert.True(thickness.Kerf < thickness.Value / 4, label);
                Assert.True(thickness.CutOff.PartClearance > thickness.Kerf, label);
                Assert.True(thickness.CutOff.Overtravel > 0, label);
                Assert.True(thickness.CutOff.MinSegmentLength > 0, label);
                Assert.Equal("Arc", thickness.LeadIn.Type);
                Assert.True(thickness.LeadIn.Radius >= thickness.Kerf * 4, label);
                Assert.NotEmpty(thickness.PlateSizes);
                Assert.All(thickness.PlateSizes, s => Assert.True(OpenNest.Shapes.PlateSizes.TryGet(s, out _), s));

                if (i == 0)
                    continue;
                var thinner = thicknesses[i - 1];
                Assert.True(thickness.Value > thinner.Value, label);
                Assert.True(thickness.Kerf >= thinner.Kerf, label);
                Assert.True(thickness.LeadIn.Radius >= thinner.LeadIn.Radius, label);
            }
        }
    }
}
