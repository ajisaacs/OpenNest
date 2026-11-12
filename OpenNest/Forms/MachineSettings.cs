using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Forms
{
    /// <summary>
    /// The WinForms entry points for the persisted machines (with their clamps) and nest
    /// defaults. The rules live in the shared <see cref="MachineSettingsSource"/>, reading this
    /// app's settings; this shows its warnings (once per session where noted) and questions.
    /// </summary>
    internal static class MachineSettings
    {
        /// <summary>The shared machine settings, reading and saving through <see cref="Properties.Settings"/>.</summary>
        internal static MachineSettingsSource Source { get; } = new(SharedSettings.Source, new AppUserSettings());

        /// <summary>
        /// The machine configurations edited in Tools > Machine Configuration: the server's when
        /// it holds any, otherwise this PC's. <paramref name="refresh"/> re-reads the server first.
        /// </summary>
        public static IDataProvider CreateMachineProvider(bool refresh = false) => Source.CreateMachineProvider(refresh);

        /// <summary>Every readable machine configuration, for choosing acceleration limits.</summary>
        public static List<MachineConfig> LoadMachines() => Source.LoadMachines();

        /// <summary>Every readable machine configuration on this PC, shared or not, for uploading.</summary>
        public static List<MachineConfig> LoadLocalMachines() => Source.LoadLocalMachines();

        /// <summary>Every material and grade on file across the readable machine configurations.</summary>
        public static IEnumerable<MaterialConfig> LoadMaterials() => Source.LoadMaterials();

        /// <summary>The clamps new nests assigned <paramref name="machine"/> receive.</summary>
        public static ClampSettings LoadClampSettings(MachineConfig machine) =>
            MachineSettingsSource.LoadClampSettings(machine);

        /// <summary>Saves <paramref name="settings"/> as the clamps of the machine with <paramref name="machineId"/>.</summary>
        public static void SaveClampSettings(Guid machineId, ClampSettings settings) =>
            Source.SaveClampSettings(machineId, settings);

        /// <summary>
        /// Copies the legacy clamps onto a machine once (see
        /// <see cref="MachineSettingsSource.MigrateLegacyClamps"/>), asking which machine when
        /// it cannot tell and warning when the copy fails.
        /// </summary>
        public static void MigrateLegacyClamps(IWin32Window owner)
        {
            var warning = Source.MigrateLegacyClamps(machines => AskClampMachine(owner, machines));
            if (warning != null)
                MessageBox.Show(owner, warning, "Clamps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        /// <summary>The machine new nests are assigned (see <see cref="MachineSettingsSource.DefaultMachine"/>).</summary>
        public static MachineConfig DefaultMachine(NestDefaults defaults, IEnumerable<MachineConfig> machines) =>
            MachineSettingsSource.DefaultMachine(defaults, machines);

        /// <summary>The configuration of <paramref name="machine"/>, or null when none is assigned or it is not on file.</summary>
        public static MachineConfig FindMachine(NestMachine machine) => Source.FindMachine(machine);

        /// <summary>Assigns <paramref name="machine"/> and its support grid to <paramref name="nest"/>.</summary>
        public static void AssignMachine(Nest nest, MachineConfig machine) => Source.AssignMachine(nest, machine);

        /// <summary>See <see cref="MachineSettingsSource.DefaultCuttingParameters"/>.</summary>
        public static CuttingParameters DefaultCuttingParameters(Nest nest) => Source.DefaultCuttingParameters(nest);

        /// <summary>See <see cref="MachineSettingsSource.CuttingParametersFor"/>.</summary>
        public static CuttingParameters CuttingParametersFor(Plate plate, Nest nest) => Source.CuttingParametersFor(plate, nest);

        /// <summary>Whether the cutting settings of <paramref name="nest"/> are kept on its machine's thickness.</summary>
        public static bool HasMachineThickness(Nest nest) => Source.HasMachineThickness(nest);

        /// <summary>See <see cref="MachineSettingsSource.DescribeCuttingSource"/>.</summary>
        public static string DescribeCuttingSource(Nest nest) => Source.DescribeCuttingSource(nest);

        /// <summary>
        /// Saves cutting settings edited for <paramref name="nest"/> (see
        /// <see cref="MachineSettingsSource.SaveCuttingParameters(Nest, CuttingParameters, Action{string})"/>),
        /// telling the user why they could not reach the machine. Returns whether they did.
        /// </summary>
        public static bool SaveCuttingParameters(IWin32Window owner, Nest nest, CuttingParameters parameters) =>
            Source.SaveCuttingParameters(nest, parameters, error => MessageBox.Show(owner, error,
                "Cutting Parameters", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        /// <summary>The save above with the machines of <paramref name="provider"/>.</summary>
        internal static bool SaveCuttingParameters(Nest nest, CuttingParameters parameters, IDataProvider provider, Action<string> warn) =>
            Source.SaveCuttingParameters(nest, parameters, provider, warn);

        /// <summary>Saves <paramref name="parameters"/> as this PC's last cutting settings.</summary>
        public static void SaveLastCuttingParameters(CuttingParameters parameters) =>
            Source.SaveLastCuttingParameters(parameters);

        /// <summary>The cutting parameters last applied, or the built-in defaults when none are saved or readable.</summary>
        public static CuttingParameters LastCuttingParameters() => Source.LastCuttingParameters();

        /// <summary>
        /// Saves a Nest Info dialog to <paramref name="nest"/>: a changed material or thickness
        /// takes the machine's spacing, and an untouched trailing sheet follows the new defaults.
        /// </summary>
        public static void SaveNestInfo(PlateManager manager, EditNestInfoForm form, Nest nest) =>
            manager.ChangePlateDefaults(() => MachineCuttingDefaults.ApplySpacingAfter(
                nest, FindMachine(nest.Machine), () => form.SaveNestInfo(nest)));

        /// <summary>
        /// Gives <paramref name="view"/> the cut-off clearance, overtravel, minimum segment and
        /// direction of the nest's machine for its material and thickness, when the machine has
        /// them; otherwise the view keeps its settings. Existing cut-offs follow them the next time
        /// they regenerate.
        /// </summary>
        public static void ApplyCutOffDefaults(PlateView view, Nest nest) =>
            Source.ApplyCutOffDefaults(view.CutOffSettings, nest);

        /// <summary>See <see cref="MachineSettingsSource.LoadSupportGridSettings"/>.</summary>
        public static SupportGridSettings LoadSupportGridSettings(MachineConfig machine) =>
            Source.LoadSupportGridSettings(machine);

        /// <summary>Saves <paramref name="settings"/> as the support grid of the machine with <paramref name="machineId"/>.</summary>
        public static void SaveSupportGridSettings(Guid machineId, SupportGridSettings settings) =>
            Source.SaveSupportGridSettings(machineId, settings);

        /// <summary>
        /// Loads the persisted nest defaults (see <see cref="LoadSavedNestDefaults"/>);
        /// a corrupt or unreadable file warns once per session.
        /// </summary>
        public static NestDefaults LoadNestDefaults()
        {
            var defaults = Source.LoadNestDefaults(out var warning);
            if (warning != null)
                MessageBox.Show(warning, "Nest Defaults", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return defaults;
        }

        /// <summary>See <see cref="MachineSettingsSource.LoadSavedNestDefaults"/>.</summary>
        public static NestDefaults LoadSavedNestDefaults(out NestDefaultsStatus status) =>
            Source.LoadSavedNestDefaults(out status);

        /// <summary>This PC's own nest defaults, shared or not, for uploading.</summary>
        public static NestDefaults LoadLocalNestDefaults() => Source.LoadLocalNestDefaults();

        /// <summary>
        /// Saves the nest defaults where they are read: the server once it holds them, otherwise
        /// this PC's file. Throws <see cref="System.IO.IOException"/> when the server cannot take them.
        /// </summary>
        public static void SaveNestDefaults(NestDefaults defaults) => Source.SaveNestDefaults(defaults);

        /// <summary>Saves the nest defaults, telling the user when they could not be saved.</summary>
        public static void SaveNestDefaults(IWin32Window owner, NestDefaults defaults)
        {
            try
            {
                SaveNestDefaults(defaults);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(owner, "The nest defaults could not be saved.\n\n" + ex.Message, "Nest Defaults",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Makes values taken from an open nest the defaults new nests start from. The nest's
        /// machine becomes the default; a nest without one keeps the saved choice.
        /// </summary>
        public static void SaveNestDefaults(IWin32Window owner, NestDefaults defaults, NestMachine machine)
        {
            defaults.MachineId = machine?.Id ?? LoadSavedNestDefaults(out _).MachineId;
            SaveNestDefaults(owner, defaults);
        }

        /// <summary>The app settings the shared rules read and save.</summary>
        private sealed class AppUserSettings : IMachineUserSettings
        {
            public Units DefaultUnit => Properties.Settings.Default.DefaultUnit;

            public string CuttingParametersJson
            {
                get => Properties.Settings.Default.CuttingParametersJson;
                set
                {
                    Properties.Settings.Default.CuttingParametersJson = value;
                    Properties.Settings.Default.Save();
                }
            }
        }
    }
}
