using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

/// <summary>
/// The shared machine entry points: the legacy clamps moving onto a machine once, cutting
/// settings saved onto the nest's machine thickness or this PC, and the nest defaults.
/// </summary>
public sealed class MachineSettingsSourceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenNestDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly FakeSettingsServer server = new();
    private readonly MemoryUserSettings user = new();

    public MachineSettingsSourceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private string MachinesDirectory => Path.Combine(directory, "Machines");

    private string DefaultsPath => Path.Combine(directory, "defaults.json");

    private string ClampsPath => Path.Combine(directory, "clamps.json");

    private static SharedSettingsSource FileMode() =>
        new(() => new NestStorageSettings(), _ => throw new InvalidOperationException("File mode never reaches the server."));

    private MachineSettingsSource Machines(SharedSettingsSource? shared = null) =>
        new(shared ?? FileMode(), user, MachinesDirectory, DefaultsPath, ClampsPath, Path.Combine(directory, "grid.json"));

    private MachineConfig SaveLocalMachine(string name)
    {
        var machine = new MachineConfig { Name = name };
        new LocalJsonProvider(MachinesDirectory).SaveMachine(machine);
        return machine;
    }

    private MachineConfig Reload(MachineConfig machine) => new LocalJsonProvider(MachinesDirectory).GetMachine(machine.Id)!;

    private static ClampSettings Legacy() => new()
    {
        Enabled = true,
        Units = Units.Inches,
        Width = 2,
        Depth = 0.5,
        RemovalMinutes = 4,
        Centers = [12, 24],
    };

    [Fact]
    public void LegacyClamps_GoOntoTheOnlyMachineOnce()
    {
        var only = SaveLocalMachine("Only");
        Legacy().Save(ClampsPath);
        var machines = Machines();

        Assert.Null(machines.MigrateLegacyClamps(_ => throw new InvalidOperationException("Nothing to ask.")));

        Assert.Equal([12.0, 24.0], Reload(only).Clamps!.Centers);
        // Once a machine has clamps the legacy file is never read again.
        File.Delete(ClampsPath);
        new ClampSettings { Enabled = true, Units = Units.Inches, Width = 1, Depth = 1, Centers = [99] }.Save(ClampsPath);
        Assert.Null(machines.MigrateLegacyClamps(null!));
        Assert.Equal([12.0, 24.0], Reload(only).Clamps!.Centers);
    }

    [Fact]
    public void LegacyClamps_GoOntoTheNestDefaultsMachine()
    {
        var a = SaveLocalMachine("A");
        var b = SaveLocalMachine("B");
        new NestDefaults { MachineId = b.Id }.Save(DefaultsPath);
        Legacy().Save(ClampsPath);

        Assert.Null(Machines().MigrateLegacyClamps(_ => throw new InvalidOperationException("Nothing to ask.")));

        Assert.Null(Reload(a).Clamps);
        Assert.NotNull(Reload(b).Clamps);
    }

    [Fact]
    public void LegacyClamps_WithSeveralMachinesAndNoDefault_AskOncePerSession()
    {
        var a = SaveLocalMachine("A");
        var b = SaveLocalMachine("B");
        Legacy().Save(ClampsPath);
        var machines = Machines();
        var asked = new List<IReadOnlyList<string>>();

        machines.MigrateLegacyClamps(choices => { asked.Add(choices.Select(m => m.Name).ToList()); return null; });
        machines.MigrateLegacyClamps(choices => { asked.Add(choices.Select(m => m.Name).ToList()); return choices[1]; });

        Assert.Equal([["A", "B"]], asked);
        Assert.Null(Reload(a).Clamps);
        Assert.Null(Reload(b).Clamps);

        // The next session asks again, and the machine picked gets the clamps.
        Assert.Null(Machines().MigrateLegacyClamps(choices => choices[1]));
        Assert.Null(Reload(a).Clamps);
        Assert.NotNull(Reload(b).Clamps);
    }

    [Fact]
    public void LegacyClamps_ComeFromTheServersCopyInDatabaseMode()
    {
        var only = new MachineConfig { Name = "Shared Laser" };
        server.Hold(SharedSettingKinds.Machine, only.Id.ToString("D"), SharedMachineProvider.Serialize(only));
        server.Hold(SharedSettingKinds.Clamps, SharedSettingKinds.DefaultKey, Legacy().ToJson());
        var machines = Machines(server.Source(Path.Combine(directory, "shared.json")));

        Assert.Null(machines.MigrateLegacyClamps(null!));

        var stored = server.Records.Single(r => r.Kind == SharedSettingKinds.Machine);
        Assert.Equal(2, stored.Revision);
        Assert.NotNull(machines.LoadMachines().Single().Clamps);
    }

    [Fact]
    public void LegacyClamps_ThatCannotBeSavedAreReported()
    {
        var only = new MachineConfig { Name = "Shared Laser" };
        server.Hold(SharedSettingKinds.Machine, only.Id.ToString("D"), SharedMachineProvider.Serialize(only));
        Legacy().Save(ClampsPath);
        var shared = server.Source(Path.Combine(directory, "shared.json"));
        var machines = Machines(shared);
        shared.Current(refresh: true);
        server.Online = false;

        var warning = machines.MigrateLegacyClamps(null!);

        Assert.Contains("could not be copied to a machine", warning);
        Assert.Equal(1, server.Records.Single().Revision);
    }

    private static MachineConfig CuttingMachine() => new()
    {
        Name = "Fiber 1",
        Materials =
        {
            new MaterialConfig
            {
                Name = "Mild Steel",
                Grade = "A36",
                Thicknesses = { new ThicknessConfig { Value = 0.25, LeadIn = new LeadConfig { Type = "Line", Length = 0.1, Angle = 60 } } },
            },
        },
    };

    private static Nest NestFor(MachineConfig machine, double thickness) => new()
    {
        Thickness = thickness,
        Material = new Material("Mild Steel", "A36", 0.2836),
        Machine = new NestMachine(machine.Id, machine.Name),
    };

    private static CuttingParameters Edited() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.2, ApproachAngle = 45 },
        SupportTabs = true,
        TabBySize = true,
        TabSizeMaxWidth = 2,
        TabConfig = new NormalTab { Size = 0.03 },
    };

    [Fact]
    public void CuttingSettings_SaveOntoTheMachinesThicknessAndDropThisPcsCopyForThatJob()
    {
        var machine = CuttingMachine();
        var provider = new LocalJsonProvider(MachinesDirectory);
        provider.SaveMachine(machine);
        var machines = Machines();
        var nest = NestFor(machine, 0.25);
        var stale = new CuttingParameters();
        MachineCuttingDefaults.ApplyTo(stale, machine, nest);
        machines.SaveLastCuttingParameters(stale);
        var warnings = new List<string>();

        Assert.True(machines.SaveCuttingParameters(nest, Edited(), warnings.Add));

        Assert.Empty(warnings);
        var thickness = Reload(machine).Materials[0].Thicknesses[0];
        Assert.Equal(0.2, thickness.LeadIn.Length);
        Assert.True(thickness.Cutting!.TabBySize);
        Assert.Equal(2, thickness.Cutting.TabSizeMaxWidth);
        Assert.False(MachineCuttingDefaults.IsRecordedFor(machines.LastCuttingParameters(), machine, nest));
        Assert.True(machines.HasMachineThickness(nest));
        Assert.Equal("Saved for Fiber 1 · Mild Steel A36 0.25 in", machines.DescribeCuttingSource(nest));
        // The machine's thickness is what the next plate starts from.
        Assert.Equal(0.2, ((LineLeadIn)machines.DefaultCuttingParameters(nest).ExternalLeadIn).Length);
    }

    [Fact]
    public void CuttingSettings_SaveOnThisPcWhenTheMachineHasNoSuchThickness()
    {
        var machine = CuttingMachine();
        new LocalJsonProvider(MachinesDirectory).SaveMachine(machine);
        var machines = Machines();
        var nest = NestFor(machine, 0.5);

        Assert.False(machines.SaveCuttingParameters(nest, Edited(), _ => Assert.Fail("No warning expected.")));

        Assert.Null(Reload(machine).Materials[0].Thicknesses[0].Cutting);
        Assert.True(machines.LastCuttingParameters().TabBySize);
        Assert.Null(machines.LastCuttingParameters().MachineName);
        Assert.Equal("This PC's settings: Fiber 1 has no Mild Steel A36 0.5 in.", machines.DescribeCuttingSource(nest));
    }

    [Fact]
    public void CuttingSettings_SaveOnThisPcWhenTheNestHasNoMachine()
    {
        var machines = Machines();
        var nest = new Nest { Thickness = 0.25, Material = new Material("Mild Steel", "A36", 0.2836) };

        Assert.False(machines.SaveCuttingParameters(nest, Edited(), _ => Assert.Fail("No warning expected.")));

        Assert.True(machines.LastCuttingParameters().TabBySize);
        Assert.Equal("This PC's settings: the nest has no machine.", machines.DescribeCuttingSource(nest));
    }

    [Fact]
    public void CuttingSettings_KeptOnThisPcForThatJobWhenTheServerIsOffline()
    {
        var machine = CuttingMachine();
        server.Hold(SharedSettingKinds.Machine, machine.Id.ToString("D"), SharedMachineProvider.Serialize(machine));
        var shared = server.Source(Path.Combine(directory, "shared.json"));
        var machines = Machines(shared);
        var nest = NestFor(machine, 0.25);
        shared.Current(refresh: true);
        server.Online = false;
        var warnings = new List<string>();

        Assert.False(machines.SaveCuttingParameters(nest, Edited(), warnings.Add));

        Assert.Contains("could not be saved to Fiber 1", Assert.Single(warnings));
        Assert.Equal(1, server.Records.Single().Revision);
        var last = machines.LastCuttingParameters();
        Assert.True(MachineCuttingDefaults.IsRecordedFor(last, machine, nest));
        Assert.True(last.TabBySize);
        // Recorded for the job, so the machine's settings do not replace the edit here.
        Assert.Equal(0.2, ((LineLeadIn)machines.DefaultCuttingParameters(nest).ExternalLeadIn).Length);
    }

    [Fact]
    public void CuttingParametersFor_UsesThePlatesOwnOnlyWhenNoneAreSaved()
    {
        var machines = Machines();
        var plate = new Plate { CuttingParameters = new CuttingParameters { TabBySize = true } };
        var nest = new Nest();

        Assert.Same(plate.CuttingParameters, machines.CuttingParametersFor(plate, nest));

        machines.SaveLastCuttingParameters(new CuttingParameters());
        Assert.NotSame(plate.CuttingParameters, machines.CuttingParametersFor(plate, nest));
    }

    [Fact]
    public void NestDefaults_FallBackToTheDefaultUnitAndAnUnreadableFileWarnsOnce()
    {
        user.DefaultUnit = Units.Millimeters;
        var machines = Machines();

        Assert.Equal(Units.Millimeters, machines.LoadNestDefaults(out var none).Units);
        Assert.Null(none);

        File.WriteAllText(DefaultsPath, "{ not json");
        machines.LoadNestDefaults(out var first);
        machines.LoadNestDefaults(out var second);

        Assert.Contains(DefaultsPath, first);
        Assert.Null(second);
    }

    [Fact]
    public void DefaultMachine_IsTheDefaultsMachineElseTheFirstByName()
    {
        var b = new MachineConfig { Name = "b" };
        var a = new MachineConfig { Name = "A" };

        Assert.Same(a, MachineSettingsSource.DefaultMachine(new NestDefaults(), [b, a]));
        Assert.Same(b, MachineSettingsSource.DefaultMachine(new NestDefaults { MachineId = b.Id }, [b, a]));
        Assert.Null(MachineSettingsSource.DefaultMachine(null!, []));
    }

    [Fact]
    public void LocalMachines_StartWithTheBuiltInMachine()
    {
        var loaded = Machines().LoadMachines();

        Assert.NotEmpty(loaded);
        Assert.True(Directory.GetFiles(MachinesDirectory, "*.json").Length > 0);
    }

    [Fact]
    public void UserSettingsFile_KeepsBothSettingsUnderTheWinFormsNames()
    {
        var path = Path.Combine(directory, "user-settings.json");
        UserSettingsFile.Write(path, new Dictionary<string, string> { ["DefaultUnit"] = "Millimeters", ["Other"] = "kept" });
        var settings = new MachineUserSettingsFile(path);

        Assert.Equal(Units.Millimeters, settings.DefaultUnit);
        Assert.Equal("", settings.CuttingParametersJson);

        settings.CuttingParametersJson = "{\"a\":1}";

        var values = UserSettingsFile.Read(path);
        Assert.Equal("{\"a\":1}", values["CuttingParametersJson"]);
        Assert.Equal("kept", values["Other"]);
        Assert.Equal("{\"a\":1}", settings.CuttingParametersJson);

        UserSettingsFile.Write(path, new Dictionary<string, string> { ["DefaultUnit"] = "Furlongs" });
        Assert.Equal(Units.Inches, settings.DefaultUnit);
        UserSettingsFile.Write(path, new Dictionary<string, string> { ["DefaultUnit"] = "7" });
        Assert.Equal(Units.Inches, settings.DefaultUnit);
    }

    [Fact]
    public void CutOffDefaults_ComeFromTheNestsMachineThickness()
    {
        var machine = CuttingMachine();
        machine.Materials[0].Thicknesses[0].CutOff = new CutOffConfig { PartClearance = 0.75 };
        new LocalJsonProvider(MachinesDirectory).SaveMachine(machine);
        var settings = new CutOffSettings { PartClearance = 0.1 };

        Machines().ApplyCutOffDefaults(settings, NestFor(machine, 0.25));

        Assert.Equal(0.75, settings.PartClearance);
    }
}
