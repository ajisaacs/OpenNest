using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>
/// Each machine's own clamp rail (<see cref="MachineConfig.Clamps"/>, in the machine's units),
/// the rail new nests copy from it, and the one-time copy of the legacy clamps.json onto a machine.
/// </summary>
public class MachineClampsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());

    public MachineClampsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static MachineClamps Rail(bool enabled = true) => new()
    {
        Enabled = enabled,
        Width = 1.5,
        Depth = 0.625,
        RemovalMinutes = 3,
        Centers = new List<double> { 6, 21, 36 },
        FarEndCenters = new List<double> { 10 },
    };

    private static ClampSettings Legacy(Units units = Units.Inches) => new()
    {
        Enabled = true,
        Units = units,
        Width = 2,
        Depth = 0.5,
        RemovalMinutes = 4,
        Centers = new double[] { 12, 24 },
    };

    [Fact]
    public void SaveMachine_RoundTripsTheClamps()
    {
        var provider = new LocalJsonProvider(_directory);
        var machine = new MachineConfig { Name = "Laser", Clamps = Rail() };

        provider.SaveMachine(machine);
        var clamps = provider.GetMachine(machine.Id)!.Clamps!;

        Assert.True(clamps.Enabled);
        Assert.Equal(1.5, clamps.Width);
        Assert.Equal(0.625, clamps.Depth);
        Assert.Equal(3, clamps.RemovalMinutes);
        Assert.Equal(new double[] { 6, 21, 36 }, clamps.Centers);
        Assert.Empty(clamps.FarSideCenters);
        Assert.Equal(new double[] { 10 }, clamps.FarEndCenters);
    }

    [Fact]
    public void MachineSavedBeforeClamps_LoadsWithoutClamps()
    {
        var provider = new LocalJsonProvider(_directory);
        var machine = new MachineConfig { Name = "Laser" };
        provider.SaveMachine(machine);

        var loaded = provider.GetMachine(machine.Id)!;

        Assert.Null(loaded.Clamps);
        Assert.Null(loaded.GetClampSettings().RailFor(Units.Inches));
    }

    [Fact]
    public void MachineWithoutClamps_GivesNoRailAtTheDefaultSizeInItsUnits()
    {
        var machine = new MachineConfig { Units = UnitSystem.Millimeters };

        var settings = machine.GetClampSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(Units.Millimeters, settings.Units);
        Assert.Equal(ClampSettings.DefaultWidth * 25.4, settings.Width, 9);
        Assert.Equal(0, settings.ClampCount);
    }

    [Fact]
    public void DisabledClamps_GiveNewNestsNoRail()
    {
        var machine = new MachineConfig { Clamps = Rail(enabled: false) };
        var nest = new Nest { Units = Units.Inches };

        machine.GetClampSettings().ApplyToNewNest(nest);

        Assert.Null(nest.PlateDefaults.ClampRail);
    }

    [Fact]
    public void MillimeterMachine_GivesAnInchNestItsRailConverted()
    {
        var machine = new MachineConfig
        {
            Units = UnitSystem.Millimeters,
            Clamps = new MachineClamps
            {
                Enabled = true,
                Width = 38.1,
                Depth = 12.7,
                RemovalMinutes = 2,
                Centers = new List<double> { 254 },
            },
        };
        var nest = new Nest { Units = Units.Inches };

        machine.GetClampSettings().ApplyToNewNest(nest);

        var rail = nest.PlateDefaults.ClampRail!;
        Assert.Equal(1.5, rail.Width, 9);
        Assert.Equal(0.5, rail.Depth, 9);
        Assert.Equal(2, rail.RemovalMinutes);
        Assert.Equal(10, Assert.Single(rail.Clamps).Center, 9);
    }

    [Fact]
    public void EditingTheMachine_NeverMovesAnExistingNestsClamps()
    {
        var machine = new MachineConfig { Clamps = Rail() };
        var nest = new Nest { Units = Units.Inches };
        machine.GetClampSettings().ApplyToNewNest(nest);

        machine.Clamps!.Centers = new List<double> { 50 };
        machine.Clamps.Width = 3;

        var rail = nest.PlateDefaults.ClampRail!;
        Assert.Equal(1.5, rail.Width);
        Assert.Equal(new double[] { 6, 21, 36 },
            rail.Clamps.Where(c => c.Edge == ClampEdge.OriginSide).Select(c => c.Center));
    }

    [Fact]
    public void InvalidStoredValues_FallBackOrAreDropped()
    {
        var clamps = new MachineClamps
        {
            Enabled = true,
            Width = -1,
            Depth = double.NaN,
            RemovalMinutes = -2,
            Centers = new List<double> { 30, -5, double.PositiveInfinity, 10, 30 },
            FarSideCenters = null!,
        };

        var settings = clamps.ToSettings(UnitSystem.Inches);

        Assert.Equal(ClampSettings.DefaultWidth, settings.Width);
        Assert.Equal(ClampSettings.DefaultDepth, settings.Depth);
        Assert.Equal(ClampRail.DefaultRemovalMinutes, settings.RemovalMinutes);
        Assert.Equal(new double[] { 10, 30 }, settings.Centers);
        Assert.Empty(settings.FarSideCenters);
    }

    [Fact]
    public void FromSettings_ConvertsToTheMachineUnits()
    {
        var clamps = MachineClamps.FromSettings(Legacy(), UnitSystem.Millimeters);

        Assert.True(clamps.Enabled);
        Assert.Equal(50.8, clamps.Width, 9);
        Assert.Equal(12.7, clamps.Depth, 9);
        Assert.Equal(4, clamps.RemovalMinutes);
        Assert.Equal(new[] { 304.8, 609.6 }, clamps.Centers.Select(c => System.Math.Round(c, 9)));
    }

    [Fact]
    public void Migration_IsNeededOnlyWhileNoMachineHasClamps()
    {
        var a = new MachineConfig { Name = "A" };
        var b = new MachineConfig { Name = "B" };

        Assert.True(LegacyClampMigration.IsNeeded(Legacy(), new[] { a, b }));
        Assert.False(LegacyClampMigration.IsNeeded(null, new[] { a, b }));
        Assert.False(LegacyClampMigration.IsNeeded(new ClampSettings { Enabled = true }, new[] { a, b }));
        Assert.False(LegacyClampMigration.IsNeeded(Legacy(), Array.Empty<MachineConfig>()));

        LegacyClampMigration.Apply(Legacy(), a);

        Assert.False(LegacyClampMigration.IsNeeded(Legacy(), new[] { a, b }));
    }

    [Fact]
    public void Migration_TargetsTheDefaultMachine()
    {
        var a = new MachineConfig { Name = "A" };
        var b = new MachineConfig { Name = "B" };

        Assert.Same(b, LegacyClampMigration.Target(new[] { a, b }, b.Id));
    }

    [Fact]
    public void Migration_TargetsTheOnlyMachine()
    {
        var only = new MachineConfig { Name = "Only" };

        Assert.Same(only, LegacyClampMigration.Target(new[] { only }, null));
        Assert.Same(only, LegacyClampMigration.Target(new[] { only }, Guid.NewGuid()));
    }

    [Fact]
    public void Migration_WithSeveralMachinesAndNoDefault_LeavesTheChoiceToTheUser()
    {
        var machines = new[] { new MachineConfig { Name = "A" }, new MachineConfig { Name = "B" } };

        Assert.Null(LegacyClampMigration.Target(machines, null));
        Assert.Null(LegacyClampMigration.Target(machines, Guid.NewGuid()));
    }

    [Fact]
    public void Migration_CopiesTheLegacyFileIntoTheMachineUnits()
    {
        var path = Path.Combine(_directory, "clamps.json");
        Legacy().Save(path);
        var machine = new MachineConfig { Units = UnitSystem.Millimeters };

        LegacyClampMigration.Apply(ClampSettings.Load(path), machine);

        var rail = machine.GetClampSettings().RailFor(Units.Inches)!;
        Assert.Equal(2, rail.Width, 9);
        Assert.Equal(4, rail.RemovalMinutes);
        Assert.Equal(new[] { 12.0, 24.0 }, rail.Clamps.Select(c => System.Math.Round(c.Center, 9)));
        Assert.Equal(new[] { 304.8, 609.6 }, machine.Clamps!.Centers.Select(c => System.Math.Round(c, 9)));
    }
}
