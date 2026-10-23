using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// The desktop entry points for the persisted machine clamp settings and nest defaults.
    /// The warning variants report an unusable file once per session.
    /// </summary>
    internal static class MachineSettings
    {
        private static bool clampSettingsWarned;

        /// <summary>The machine configurations edited in Tools > Machine Configuration.</summary>
        public static LocalJsonProvider CreateMachineProvider()
        {
            var provider = new LocalJsonProvider(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest",
                "Machines"));
            provider.EnsureDefaults();
            return provider;
        }

        /// <summary>Every readable machine configuration, for choosing acceleration limits.</summary>
        public static List<MachineConfig> LoadMachines()
        {
            var machines = new List<MachineConfig>();
            try
            {
                var provider = CreateMachineProvider();
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

        /// <summary>
        /// Loads the machine clamp settings for creating new nests (New, BOM import); an
        /// unreadable or partly invalid file warns once per session, because the new nest's
        /// clamps may not match the machine.
        /// </summary>
        public static ClampSettings LoadClampSettings(IWin32Window owner)
        {
            var settings = LoadSavedClampSettings(out var status);

            if (
                status is ClampSettingsStatus.Invalid or ClampSettingsStatus.Partial
                && !clampSettingsWarned
            )
            {
                clampSettingsWarned = true;
                var problem =
                    status == ClampSettingsStatus.Invalid
                        ? "could not be read, so new nests have no clamps"
                        : "has invalid values that were ignored, so new nests may be missing clamps";
                MessageBox.Show(
                    owner,
                    $"The clamp settings file {problem}:\n{ClampSettings.DefaultPath}\n\nCheck the clamps in Nest > Clamps and use Save as Machine Defaults to rewrite the file.",
                    "Clamps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            return settings;
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
            nest.SetSupportBed(grid.Enabled ? grid.GridFor(nest.Units) : null);
        }

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

        /// <summary>The single desktop entry point for the persisted machine clamp settings.</summary>
        public static ClampSettings LoadSavedClampSettings(out ClampSettingsStatus status) =>
            ClampSettings.Load(ClampSettings.DefaultPath, out status);

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
                MessageBox.Show(
                    $"The nest defaults file could not be read:\n{NestDefaults.DefaultPath}\n\nBuilt-in defaults will be used. Re-save your defaults from Tools > Nest Defaults.",
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
            NestDefaults.Load(
                NestDefaults.DefaultPath,
                Properties.Settings.Default.DefaultUnit,
                out status
            );
    }
}
