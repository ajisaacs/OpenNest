using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class MotionConfigTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void MachineWithoutMotionFallsBackToUnitPlaceholders()
    {
        var inch = new MachineConfig { Units = UnitSystem.Inches };
        var metric = new MachineConfig { Units = UnitSystem.Millimeters };

        Assert.Null(inch.Motion);
        Assert.Equal(193, inch.GetMotionOrPlaceholder().CutAcceleration);
        Assert.Equal(4900, metric.GetMotionOrPlaceholder().CutAcceleration);
    }

    [Fact]
    public void MotionRoundTripsAndOlderFilesLoadWithoutIt()
    {
        var provider = new LocalJsonProvider(_testDir);
        var machine = new MachineConfig
        {
            Name = "Laser",
            Motion = new MotionConfig { CutAcceleration = 250, RapidAcceleration = 500, CornerDeviation = 0.002 },
        };
        var older = new MachineConfig { Name = "Older" };

        provider.SaveMachine(machine);
        provider.SaveMachine(older);

        var loaded = provider.GetMachine(machine.Id)!;
        Assert.Equal(250, loaded.Motion!.CutAcceleration);
        Assert.Equal(500, loaded.Motion.RapidAcceleration);
        Assert.Equal(0.002, loaded.Motion.CornerDeviation);
        Assert.Null(provider.GetMachine(older.Id)!.Motion);
        Assert.DoesNotContain("motionOrPlaceholder", File.ReadAllText(Path.Combine(_testDir, $"{machine.Id}.json")));
    }

    [Theory]
    [InlineData(UnitSystem.Inches, Units.Inches, 1)]
    [InlineData(UnitSystem.Inches, Units.Millimeters, 25.4)]
    [InlineData(UnitSystem.Millimeters, Units.Inches, 1 / 25.4)]
    public void ProfileConvertsToTheNestUnits(UnitSystem machine, Units nest, double scale)
    {
        var motion = new MotionConfig { CutAcceleration = 100, RapidAcceleration = 200, CornerDeviation = 0.01 };

        var profile = motion.ToProfile(machine, nest);

        Assert.Equal(100 * scale, profile.CutAcceleration, 9);
        Assert.Equal(200 * scale, profile.RapidAcceleration, 9);
        Assert.Equal(0.01 * scale, profile.CornerDeviation, 9);
    }
}
