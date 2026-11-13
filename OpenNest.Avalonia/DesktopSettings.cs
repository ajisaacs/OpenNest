using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// The machines, nest defaults and post processors this app works with, from the shared
    /// desktop library: in Database mode (storage.json, shared with the WinForms app) the nest
    /// server's copy once it holds one, otherwise this PC's files, with the user settings both
    /// apps keep in user-settings.json.
    /// </summary>
    internal static class DesktopSettings
    {
        public static SharedSettingsSource Shared { get; } = SharedSettingsSource.FromStorageFile();

        public static MachineSettingsSource Machines { get; } =
            new(Shared, new MachineUserSettingsFile(UserSettingsFile.DefaultPath));

        /// <summary>The storage mode and nest server, read once from storage.json as <see cref="Shared"/> does.</summary>
        private static readonly NestStorageSettings Storage = NestStorageSettings.Load(NestStorageSettings.DefaultPath);

        private static RemoteNestRepository repository;

        /// <summary>
        /// File > New nests, numbered by the nest server in Database mode and otherwise by the
        /// counter both apps keep in user-settings.json.
        /// </summary>
        public static NewNestSource NewNests { get; } = new(
            Machines,
            new NewNestUserSettingsFile(UserSettingsFile.DefaultPath),
            () => Storage.IsDatabaseMode ? repository ??= new RemoteNestRepository(Storage.ServerUrl) : null);

        /// <summary>The post processors loaded from Posts/ beside the app, by load order.</summary>
        public static IReadOnlyList<IPostProcessor> PostProcessors { get; private set; } = [];

        /// <summary>The readable machine configurations, once <see cref="LoadMachinesAsync"/> has run.</summary>
        public static IReadOnlyList<MachineConfig> MachineConfigs { get; private set; } = [];

        /// <summary>
        /// Loads the post processors in <paramref name="directory"/> (each DLL on its own, so one
        /// that fails does not stop the others) and registers their materials. Returns the
        /// failures to show, or null when every post loaded.
        /// </summary>
        public static string LoadPosts(string directory)
        {
            var result = PostProcessorLoader.Load(directory);
            PostProcessors = result.PostProcessors;
            return result.Failures.Count == 0
                ? null
                : "These post processors could not be loaded: " + string.Join("; ", result.Failures);
        }

        /// <summary>
        /// Reads the machines off the UI thread (the nest server can take seconds to answer), then
        /// copies the legacy clamps onto the default or only machine, as the WinForms app does at
        /// startup (with several machines and no default it waits for the WinForms app to ask).
        /// Returns what to tell the user: the machines' source when the server could not be
        /// reached, or why the clamps were not copied; null when there is nothing to say.
        /// </summary>
        public static Task<string> LoadMachinesAsync() => Task.Run(() =>
        {
            MachineConfigs = Machines.LoadMachines();
            var clamps = Machines.MigrateLegacyClamps(askMachine: null);
            var offline = Shared.TakeOfflineWarning();
            var notices = new[] { offline, clamps }
                .Where(text => !string.IsNullOrEmpty(text))
                .Select(OneLine)
                .ToList();
            return notices.Count == 0 ? null : string.Join("   ", notices);
        });

        private static string OneLine(string text) =>
            string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
