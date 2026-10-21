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

        /// <summary>The persisted machine support grid; never throws, and loads as off when unreadable.</summary>
        public static SupportGridSettings LoadSupportGridSettings() =>
            SupportGridSettings.Load(SupportGridSettings.DefaultPath);

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
