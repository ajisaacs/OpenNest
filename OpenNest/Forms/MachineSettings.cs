using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// The desktop entry points for the persisted machines (with their clamps) and nest
    /// defaults. In Database mode each comes from the nest server once it holds a shared copy
    /// (<see cref="SharedSettings"/>), otherwise from this PC's files. The warning variants
    /// report unusable settings once per session.
    /// </summary>
    internal static class MachineSettings
    {
        /// <summary>
        /// The machine configurations edited in Tools > Machine Configuration: the server's when
        /// it holds any, otherwise this PC's. <paramref name="refresh"/> re-reads the server first.
        /// </summary>
        public static IDataProvider CreateMachineProvider(bool refresh = false) =>
            SharedSettings.Holding(SharedSettingKinds.Machine, refresh) is { } shared
                ? new SharedMachineProvider(shared)
                : CreateLocalMachineProvider();

        /// <summary>This PC's machine configurations, under %APPDATA%\OpenNest\Machines.</summary>
        private static LocalJsonProvider CreateLocalMachineProvider()
        {
            var provider = new LocalJsonProvider(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest",
                "Machines"));
            provider.EnsureDefaults();
            return provider;
        }

        /// <summary>Every readable machine configuration, for choosing acceleration limits.</summary>
        public static List<MachineConfig> LoadMachines() => ReadMachines(() => CreateMachineProvider());

        /// <summary>Every readable machine configuration on this PC, shared or not, for uploading.</summary>
        public static List<MachineConfig> LoadLocalMachines() => ReadMachines(CreateLocalMachineProvider);

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
        public static IEnumerable<MaterialConfig> LoadMaterials() =>
            LoadMachines().SelectMany(machine => machine.Materials);

        private static bool defaultsWarned;
        private static bool clampMigrationAsked;

        /// <summary>
        /// The clamps new nests assigned <paramref name="machine"/> receive (New, BOM import, Load
        /// Machine Defaults); off when there is no machine or it has no clamps.
        /// </summary>
        public static ClampSettings LoadClampSettings(MachineConfig machine) =>
            machine?.GetClampSettings() ?? ClampSettings.Fallback;

        /// <summary>Saves <paramref name="settings"/> as the clamps of the machine with <paramref name="machineId"/>.</summary>
        public static void SaveClampSettings(Guid machineId, ClampSettings settings)
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
        /// several machines and no default the user picks one, asked once per session.
        /// </summary>
        public static void MigrateLegacyClamps(IWin32Window owner)
        {
            try
            {
                var legacy = LoadLegacyClampSettings();
                var provider = CreateMachineProvider();
                var machines = ReadMachines(() => provider)
                    .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (!LegacyClampMigration.IsNeeded(legacy, machines))
                    return;

                var target = LegacyClampMigration.Target(machines, LoadSavedNestDefaults(out _).MachineId);
                if (target == null)
                {
                    if (clampMigrationAsked)
                        return;
                    clampMigrationAsked = true;
                    target = AskClampMachine(owner, machines);
                    if (target == null)
                        return;
                }

                LegacyClampMigration.Apply(legacy, target);
                provider.SaveMachine(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(owner,
                    $"The clamp settings could not be copied to a machine:\n\n{ex.Message}\n\n"
                    + "New nests get no clamps until a machine has them (Tools > Machine Configuration).",
                    "Clamps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // The pre-machine clamp settings: the server's shared copy once it holds one, otherwise
        // this PC's clamps.json; null when neither exists.
        private static ClampSettings LoadLegacyClampSettings()
        {
            if (SharedSettings.Find(SharedSettingKinds.Clamps, SharedSettingKinds.DefaultKey) is { } shared)
                return ClampSettings.Parse(shared.Json, out _);
            return File.Exists(ClampSettings.DefaultPath) ? ClampSettings.Load(ClampSettings.DefaultPath) : null;
        }

        private static MachineConfig AskClampMachine(IWin32Window owner, IReadOnlyList<MachineConfig> machines)
        {
            var page = new TaskDialogPage
            {
                Caption = "Clamps",
                Heading = "Which machine are these clamps on?",
                Text = "Clamps are now kept with each machine. Choose the machine that gets the clamps "
                    + "saved before, or choose later in Tools > Machine Configuration. Until then new "
                    + "nests get no clamps.",
                Icon = TaskDialogIcon.Information,
                AllowCancel = true,
            };
            foreach (var machine in machines)
                page.RadioButtons.Add(new TaskDialogRadioButton(machine.Name) { Tag = machine });
            page.RadioButtons[0].Checked = true;
            var copy = new TaskDialogButton("Copy Clamps");
            page.Buttons.Add(copy);
            page.Buttons.Add(new TaskDialogButton("Not Now"));

            var handle = owner?.Handle ?? IntPtr.Zero;
            var result = handle == IntPtr.Zero ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(handle, page);
            return result == copy
                ? page.RadioButtons.FirstOrDefault(b => b.Checked)?.Tag as MachineConfig
                : null;
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
        public static MachineConfig FindMachine(NestMachine machine)
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
        public static void AssignMachine(Nest nest, MachineConfig machine)
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
        public static CuttingParameters DefaultCuttingParameters(Nest nest)
        {
            var parameters = LastCuttingParameters();
            var machine = FindMachine(nest.Machine);
            if (!MachineCuttingDefaults.IsRecordedFor(parameters, machine, nest))
                MachineCuttingDefaults.ApplyTo(parameters, machine, nest);
            return parameters;
        }

        /// <summary>The cutting parameters last applied, or the built-in defaults when none are saved or readable.</summary>
        public static CuttingParameters LastCuttingParameters()
        {
            var json = Properties.Settings.Default.CuttingParametersJson;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    return CuttingParametersSerializer.Deserialize(json);
                }
                catch
                { /* fall through */
                }
            }
            return new CuttingParameters();
        }

        /// <summary>
        /// Gives <paramref name="view"/> the cut-off clearance, overtravel, minimum segment and
        /// direction of the nest's machine for its material and thickness, when the machine has
        /// them; otherwise the view keeps its settings. Existing cut-offs follow them the next time
        /// they regenerate.
        /// </summary>
        public static void ApplyCutOffDefaults(PlateView view, Nest nest) =>
            MachineCuttingDefaults.ApplyTo(view.CutOffSettings, FindMachine(nest.Machine), nest);

        /// <summary>
        /// The support grid of <paramref name="machine"/>; a machine without one saved yet, or no
        /// machine, uses the legacy shared support-grid.json. Never throws, and loads as off when unreadable.
        /// </summary>
        public static SupportGridSettings LoadSupportGridSettings(MachineConfig machine) =>
            machine?.SupportGrid ?? SupportGridSettings.Load(SupportGridSettings.DefaultPath);

        /// <summary>Saves <paramref name="settings"/> as the support grid of the machine with <paramref name="machineId"/>.</summary>
        public static void SaveSupportGridSettings(Guid machineId, SupportGridSettings settings)
        {
            var provider = CreateMachineProvider();
            var machine = provider.GetMachine(machineId)
                ?? throw new IOException("The machine configuration is no longer on file.");
            machine.SupportGrid = settings;
            provider.SaveMachine(machine);
        }

        /// <summary>
        /// Loads the persisted nest defaults (see <see cref="LoadSavedNestDefaults"/>);
        /// a corrupt or unreadable file warns once per session.
        /// </summary>
        public static NestDefaults LoadNestDefaults()
        {
            var defaults = LoadSavedNestDefaults(out var status);

            if (status == NestDefaultsStatus.Invalid && !defaultsWarned)
            {
                defaultsWarned = true;
                var source = SharedNestDefaults() != null ? "on the nest server" : NestDefaults.DefaultPath;
                MessageBox.Show(
                    $"The nest defaults could not be read:\n{source}\n\nBuilt-in defaults will be used. Re-save your defaults from Tools > Nest Defaults.",
                    "Nest Defaults",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            return defaults;
        }

        /// <summary>
        /// The single desktop entry point for persisted nest defaults. Units
        /// fall back to the legacy DefaultUnit setting whenever the file is
        /// missing or unusable or has no valid unit, so New, BOM import and
        /// the defaults dialog always agree.
        /// </summary>
        public static NestDefaults LoadSavedNestDefaults(out NestDefaultsStatus status) =>
            SharedNestDefaults() is { } shared
                ? NestDefaults.Parse(shared.Json, Properties.Settings.Default.DefaultUnit, out status)
                : NestDefaults.Load(NestDefaults.DefaultPath, Properties.Settings.Default.DefaultUnit, out status);

        /// <summary>This PC's own nest defaults, shared or not, for uploading.</summary>
        public static NestDefaults LoadLocalNestDefaults() =>
            NestDefaults.Load(NestDefaults.DefaultPath, Properties.Settings.Default.DefaultUnit, out _);

        /// <summary>
        /// Saves the nest defaults where they are read: the server once it holds them, otherwise
        /// this PC's file. Throws <see cref="IOException"/> when the server cannot take them.
        /// </summary>
        public static void SaveNestDefaults(NestDefaults defaults)
        {
            if (SharedSettings.Current() is { } current
                && current.Get(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey) != null)
                current.Save(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey, defaults.ToJson());
            else
                defaults.Save(NestDefaults.DefaultPath);
        }

        private static SharedSettingRecord SharedNestDefaults() =>
            SharedSettings.Find(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey);
    }
}
