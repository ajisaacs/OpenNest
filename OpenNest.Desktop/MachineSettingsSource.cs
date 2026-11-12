using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Desktop;

/// <summary>
/// The two per-user settings the machine entry points read: the legacy default unit, used
/// when the nest defaults give none, and this PC's last cutting settings (saving one persists it).
/// </summary>
public interface IMachineUserSettings
{
    /// <summary>The DefaultUnit setting.</summary>
    Units DefaultUnit { get; }

    /// <summary>The CuttingParametersJson setting; setting it saves it.</summary>
    string CuttingParametersJson { get; set; }
}

/// <summary>
/// The desktop entry points for the persisted machines (with their clamps) and nest defaults.
/// In Database mode each comes from the nest server once it holds a shared copy
/// (<see cref="SharedSettingsSource"/>), otherwise from this PC's files. Nothing here shows UI:
/// the warning texts it returns are shown by the front end, at most once per session where
/// noted.
/// </summary>
public sealed class MachineSettingsSource
{
    private readonly IMachineUserSettings user;
    private readonly string machinesDirectory;
    private readonly string nestDefaultsPath;
    private readonly string legacyClampsPath;
    private readonly string legacySupportGridPath;
    private bool defaultsWarned;
    private bool clampMigrationAsked;

    /// <param name="shared">Where shared copies come from.</param>
    /// <param name="user">The per-user settings this reads and saves.</param>
    /// <param name="machinesDirectory">This PC's machines; defaults to %APPDATA%\OpenNest\Machines.</param>
    /// <param name="nestDefaultsPath">This PC's nest defaults; defaults to <see cref="NestDefaults.DefaultPath"/>.</param>
    /// <param name="legacyClampsPath">The pre-machine clamps.json; defaults to <see cref="ClampSettings.DefaultPath"/>.</param>
    /// <param name="legacySupportGridPath">The pre-machine support-grid.json; defaults to <see cref="SupportGridSettings.DefaultPath"/>.</param>
    public MachineSettingsSource(
        SharedSettingsSource shared,
        IMachineUserSettings user,
        string machinesDirectory = null,
        string nestDefaultsPath = null,
        string legacyClampsPath = null,
        string legacySupportGridPath = null)
    {
        Shared = shared ?? throw new ArgumentNullException(nameof(shared));
        this.user = user ?? throw new ArgumentNullException(nameof(user));
        this.machinesDirectory = machinesDirectory ?? DefaultMachinesDirectory;
        this.nestDefaultsPath = nestDefaultsPath ?? NestDefaults.DefaultPath;
        this.legacyClampsPath = legacyClampsPath ?? ClampSettings.DefaultPath;
        this.legacySupportGridPath = legacySupportGridPath ?? SupportGridSettings.DefaultPath;
    }

    /// <summary>%APPDATA%\OpenNest\Machines.</summary>
    public static string DefaultMachinesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNest",
        "Machines");

    /// <summary>Where shared copies come from.</summary>
    public SharedSettingsSource Shared { get; }

    /// <summary>This PC's nest defaults file.</summary>
    public string NestDefaultsPath => nestDefaultsPath;

    /// <summary>
    /// The machine configurations edited in Tools > Machine Configuration: the server's when
    /// it holds any, otherwise this PC's. <paramref name="refresh"/> re-reads the server first.
    /// </summary>
    public IDataProvider CreateMachineProvider(bool refresh = false) =>
        Shared.Holding(SharedSettingKinds.Machine, refresh) is { } shared
            ? new SharedMachineProvider(shared)
            : CreateLocalMachineProvider();

    /// <summary>This PC's machine configurations, with the built-in ones written on first use.</summary>
    public LocalJsonProvider CreateLocalMachineProvider()
    {
        var provider = new LocalJsonProvider(machinesDirectory);
        provider.EnsureDefaults();
        return provider;
    }

    /// <summary>Every readable machine configuration, for choosing acceleration limits.</summary>
    public List<MachineConfig> LoadMachines() => ReadMachines(() => CreateMachineProvider());

    /// <summary>Every readable machine configuration on this PC, shared or not, for uploading.</summary>
    public List<MachineConfig> LoadLocalMachines() => ReadMachines(CreateLocalMachineProvider);

    private static List<MachineConfig> ReadMachines(Func<IDataProvider> createProvider)
    {
        var machines = new List<MachineConfig>();
        try
        {
            var provider = createProvider();
            foreach (var summary in provider.GetMachines())
            {
                if (provider.GetMachine(summary.Id) is { } machine)
                    machines.Add(machine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return machines;
    }

    /// <summary>Every material and grade on file across the readable machine configurations.</summary>
    public IEnumerable<MaterialConfig> LoadMaterials() =>
        LoadMachines().SelectMany(machine => machine.Materials);

    /// <summary>
    /// The clamps new nests assigned <paramref name="machine"/> receive (New, BOM import, Load
    /// Machine Defaults); off when there is no machine or it has no clamps.
    /// </summary>
    public static ClampSettings LoadClampSettings(MachineConfig machine) =>
        machine?.GetClampSettings() ?? ClampSettings.Fallback;

    /// <summary>Saves <paramref name="settings"/> as the clamps of the machine with <paramref name="machineId"/>.</summary>
    public void SaveClampSettings(Guid machineId, ClampSettings settings)
    {
        var provider = CreateMachineProvider();
        var machine = provider.GetMachine(machineId)
            ?? throw new IOException("The machine configuration is no longer on file.");
        machine.Clamps = MachineClamps.FromSettings(settings, machine.Units);
        provider.SaveMachine(machine);
    }

    /// <summary>
    /// Copies the legacy clamps.json (or the server's shared copy) onto a machine once, while
    /// no machine has clamps of its own: the nest defaults' machine, or the only machine. With
    /// several machines and no default <paramref name="askMachine"/> picks one (null for not
    /// now), asked once per session. Returns what to tell the user when the copy failed, or null.
    /// </summary>
    public string MigrateLegacyClamps(Func<IReadOnlyList<MachineConfig>, MachineConfig> askMachine)
    {
        try
        {
            var legacy = LoadLegacyClampSettings();
            var provider = CreateMachineProvider();
            var machines = ReadMachines(() => provider)
                .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (!LegacyClampMigration.IsNeeded(legacy, machines))
                return null;

            var target = LegacyClampMigration.Target(machines, LoadSavedNestDefaults(out _).MachineId);
            if (target == null)
            {
                if (clampMigrationAsked)
                    return null;
                clampMigrationAsked = true;
                target = askMachine?.Invoke(machines);
                if (target == null)
                    return null;
            }

            LegacyClampMigration.Apply(legacy, target);
            provider.SaveMachine(target);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The clamp settings could not be copied to a machine:\n\n{ex.Message}\n\n"
                + "New nests get no clamps until a machine has them (Tools > Machine Configuration).";
        }
    }

    // The pre-machine clamp settings: the server's shared copy once it holds one, otherwise
    // this PC's clamps.json; null when neither exists.
    private ClampSettings LoadLegacyClampSettings()
    {
        if (Shared.Find(SharedSettingKinds.Clamps, SharedSettingKinds.DefaultKey) is { } shared)
            return ClampSettings.Parse(shared.Json, out _);
        return File.Exists(legacyClampsPath) ? ClampSettings.Load(legacyClampsPath) : null;
    }

    /// <summary>
    /// The machine new nests are assigned: the nest defaults' machine when it is on file,
    /// otherwise the first by name, or null when there are none.
    /// </summary>
    public static MachineConfig DefaultMachine(NestDefaults defaults, IEnumerable<MachineConfig> machines)
    {
        var sorted = machines.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return sorted.FirstOrDefault(m => m.Id == defaults?.MachineId) ?? sorted.FirstOrDefault();
    }

    /// <summary>The configuration of <paramref name="machine"/>, or null when none is assigned or it is not on file.</summary>
    public MachineConfig FindMachine(NestMachine machine)
    {
        if (machine == null)
            return null;
        try
        {
            return CreateMachineProvider().GetMachine(machine.Id);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// Assigns <paramref name="machine"/> to <paramref name="nest"/> (null for none) and gives
    /// every sheet that machine's support grid, or none when the machine gives nests no grid.
    /// </summary>
    public void AssignMachine(Nest nest, MachineConfig machine)
    {
        nest.Machine = machine == null ? null : new NestMachine(machine.Id, machine.Name);
        var grid = LoadSupportGridSettings(machine);
        nest.SetSupportBed(grid.Enabled ? grid.BedFor(nest.Units) : null);
    }

    /// <summary>
    /// The cutting parameters a plate of <paramref name="nest"/> without its own starts from:
    /// the last ones applied (or the built-in defaults), with the lead-ins, lead-outs and kerf
    /// of the nest's machine for its material and thickness when the machine has them. Last
    /// parameters already taken from that machine, material and thickness keep the user's
    /// edits to them.
    /// </summary>
    public CuttingParameters DefaultCuttingParameters(Nest nest)
    {
        var parameters = LastCuttingParameters();
        var machine = FindMachine(nest.Machine);
        if (!MachineCuttingDefaults.IsRecordedFor(parameters, machine, nest))
            MachineCuttingDefaults.ApplyTo(parameters, machine, nest);
        return parameters;
    }

    /// <summary>
    /// The cutting parameters to plan or place lead-ins on <paramref name="plate"/> with: the
    /// user's (see <see cref="DefaultCuttingParameters"/>), so an edit reaches nests already
    /// planned. The plate's own are used only when no user settings are saved or readable.
    /// </summary>
    public CuttingParameters CuttingParametersFor(Plate plate, Nest nest) =>
        plate?.CuttingParameters != null && ReadLastCuttingParameters() == null
            ? plate.CuttingParameters
            : DefaultCuttingParameters(nest);

    /// <summary>Whether the cutting settings of <paramref name="nest"/> are kept on its machine's thickness.</summary>
    public bool HasMachineThickness(Nest nest) =>
        nest != null && MachineCuttingDefaults.Find(FindMachine(nest.Machine), nest) != null;

    /// <summary>
    /// Where the cutting settings of <paramref name="nest"/> are kept, for the settings
    /// dialogs: the machine's thickness, or why this PC's settings are used instead.
    /// </summary>
    public string DescribeCuttingSource(Nest nest)
    {
        var machine = FindMachine(nest?.Machine);
        if (nest == null || machine == null)
            return "This PC's settings: the nest has no machine.";
        var material = nest.Material?.Name;
        if (string.IsNullOrWhiteSpace(material) || !double.IsFinite(nest.Thickness) || nest.Thickness <= 0)
            return "This PC's settings: the nest has no material and thickness.";
        var job = $"{material}{(string.IsNullOrWhiteSpace(nest.Material.Grade) ? "" : " " + nest.Material.Grade)} "
            + $"{nest.Thickness:0.####} {(nest.Units == Units.Millimeters ? "mm" : "in")}";
        return MachineCuttingDefaults.Find(machine, nest) == null
            ? $"This PC's settings: {machine.Name} has no {job}."
            : $"Saved for {machine.Name} · {job}";
    }

    /// <summary>
    /// Saves cutting settings edited for <paramref name="nest"/>: on the machine's thickness
    /// when it has one, otherwise as this PC's settings. When the machine cannot be saved
    /// (the nest server is offline, or someone else changed the machine), the settings are
    /// kept on this PC for this job and <paramref name="warn"/> is told why. Returns whether
    /// they reached the machine.
    /// </summary>
    public bool SaveCuttingParameters(Nest nest, CuttingParameters parameters, Action<string> warn) =>
        SaveCuttingParameters(nest, parameters, CreateMachineProvider(), warn);

    /// <summary>
    /// <see cref="SaveCuttingParameters(Nest, CuttingParameters, Action{string})"/> with the
    /// machines of <paramref name="provider"/>.
    /// </summary>
    public bool SaveCuttingParameters(Nest nest, CuttingParameters parameters, IDataProvider provider, Action<string> warn)
    {
        MachineConfig machine = null;
        string error;
        try
        {
            machine = nest?.Machine == null ? null : provider.GetMachine(nest.Machine.Id);
            if (machine == null || !MachineCuttingDefaults.WriteTo(machine, nest, parameters))
            {
                SaveLastCuttingParameters(parameters);
                return false;
            }
            provider.SaveMachine(machine);
            // A copy kept on this PC for this job would hide the machine's from now on.
            var last = ReadLastCuttingParameters();
            if (last != null && MachineCuttingDefaults.IsRecordedFor(last, machine, nest))
            {
                last.MachineName = null;
                SaveLastCuttingParameters(last);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
        }

        // Stamped for this job, so the edit sticks on this PC until the machine takes it.
        var kept = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        kept.MachineName = machine?.Name ?? nest.Machine.Name;
        kept.MaterialName = nest.Material?.Name;
        kept.Grade = nest.Material?.Grade;
        kept.Thickness = nest.Thickness;
        SaveLastCuttingParameters(kept);
        warn?.Invoke($"The cutting settings could not be saved to {kept.MachineName}:\n\n{error}\n\n"
            + "They are kept on this PC for this material and thickness.");
        return false;
    }

    /// <summary>Saves <paramref name="parameters"/> as this PC's last cutting settings.</summary>
    public void SaveLastCuttingParameters(CuttingParameters parameters) =>
        user.CuttingParametersJson = CuttingParametersSerializer.Serialize(parameters);

    /// <summary>The cutting parameters last applied, or the built-in defaults when none are saved or readable.</summary>
    public CuttingParameters LastCuttingParameters() =>
        ReadLastCuttingParameters() ?? new CuttingParameters();

    private CuttingParameters ReadLastCuttingParameters()
    {
        var json = user.CuttingParametersJson;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                // Settings saved with the retired "Tab every part" read as tabs by size over
                // every width. A plate's recorded parameters are never migrated.
                var parameters = CuttingParametersSerializer.Deserialize(json);
                Support.SupportTabSettings.MigrateTabEveryPart(parameters);
                return parameters;
            }
            catch
            { /* fall through */
            }
        }
        return null;
    }

    /// <summary>
    /// Gives <paramref name="settings"/> the cut-off clearance, overtravel, minimum segment and
    /// direction of the nest's machine for its material and thickness, when the machine has
    /// them; otherwise they are left as they are.
    /// </summary>
    public void ApplyCutOffDefaults(CutOffSettings settings, Nest nest) =>
        MachineCuttingDefaults.ApplyTo(settings, FindMachine(nest.Machine), nest);

    /// <summary>
    /// The support grid of <paramref name="machine"/>; a machine without one saved yet, or no
    /// machine, uses the legacy shared support-grid.json. Never throws, and loads as off when unreadable.
    /// </summary>
    public SupportGridSettings LoadSupportGridSettings(MachineConfig machine) =>
        machine?.SupportGrid ?? SupportGridSettings.Load(legacySupportGridPath);

    /// <summary>Saves <paramref name="settings"/> as the support grid of the machine with <paramref name="machineId"/>.</summary>
    public void SaveSupportGridSettings(Guid machineId, SupportGridSettings settings)
    {
        var provider = CreateMachineProvider();
        var machine = provider.GetMachine(machineId)
            ?? throw new IOException("The machine configuration is no longer on file.");
        machine.SupportGrid = settings;
        provider.SaveMachine(machine);
    }

    /// <summary>
    /// Loads the persisted nest defaults (see <see cref="LoadSavedNestDefaults"/>).
    /// <paramref name="warning"/> says, the first time in a session, that they could not be
    /// read and the built-in defaults are used; it is null otherwise.
    /// </summary>
    public NestDefaults LoadNestDefaults(out string warning)
    {
        var defaults = LoadSavedNestDefaults(out var status);
        warning = null;
        if (status == NestDefaultsStatus.Invalid && !defaultsWarned)
        {
            defaultsWarned = true;
            var source = SharedNestDefaults() != null ? "on the nest server" : nestDefaultsPath;
            warning = $"The nest defaults could not be read:\n{source}\n\nBuilt-in defaults will be used. Re-save your defaults from Tools > Nest Defaults.";
        }
        return defaults;
    }

    /// <summary>
    /// The single desktop entry point for persisted nest defaults. Units
    /// fall back to the legacy DefaultUnit setting whenever the file is
    /// missing or unusable or has no valid unit, so New, BOM import and
    /// the defaults dialog always agree.
    /// </summary>
    public NestDefaults LoadSavedNestDefaults(out NestDefaultsStatus status) =>
        SharedNestDefaults() is { } shared
            ? NestDefaults.Parse(shared.Json, user.DefaultUnit, out status)
            : NestDefaults.Load(nestDefaultsPath, user.DefaultUnit, out status);

    /// <summary>This PC's own nest defaults, shared or not, for uploading.</summary>
    public NestDefaults LoadLocalNestDefaults() =>
        NestDefaults.Load(nestDefaultsPath, user.DefaultUnit, out _);

    /// <summary>
    /// Saves the nest defaults where they are read: the server once it holds them, otherwise
    /// this PC's file. Throws <see cref="IOException"/> when the server cannot take them.
    /// </summary>
    public void SaveNestDefaults(NestDefaults defaults)
    {
        if (Shared.Current() is { } current
            && current.Get(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey) != null)
            current.Save(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey, defaults.ToJson());
        else
            defaults.Save(nestDefaultsPath);
    }

    /// <summary>
    /// This PC's machines, <paramref name="postProcessors"/>' settings and nest defaults that
    /// <paramref name="current"/> does not hold yet (see <see cref="SharedSettingsSource.PendingUploads"/>).
    /// </summary>
    public IReadOnlyList<SharedSettingUpload> PendingUploads(SharedSettingsStore current, IEnumerable<IPostProcessor> postProcessors) =>
        SharedSettingsSource.PendingUploads(current, LoadLocalMachines, postProcessors,
            () => File.Exists(nestDefaultsPath) ? LoadLocalNestDefaults().ToJson() : null);

    private SharedSettingRecord SharedNestDefaults() =>
        Shared.Find(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey);
}

/// <summary>
/// <see cref="IMachineUserSettings"/> kept in the desktop user-settings file
/// (<see cref="UserSettingsFile"/>) under the WinForms names and text: DefaultUnit as the unit's
/// name ("Inches" when missing or unreadable) and CuttingParametersJson as JSON text.
/// </summary>
public sealed class MachineUserSettingsFile(string path) : IMachineUserSettings
{
    public const string DefaultUnitName = "DefaultUnit";
    public const string CuttingParametersName = "CuttingParametersJson";

    public Units DefaultUnit =>
        UserSettingsFile.Read(path).TryGetValue(DefaultUnitName, out var text)
        && Enum.TryParse<Units>(text?.Trim(), ignoreCase: false, out var units)
        && Enum.IsDefined(units)
            ? units
            : Units.Inches;

    public string CuttingParametersJson
    {
        get => UserSettingText.ReadString(UserSettingsFile.Read(path), CuttingParametersName, "");
        set => UserSettingsFile.Update(path, values => values[CuttingParametersName] = value ?? "");
    }
}
