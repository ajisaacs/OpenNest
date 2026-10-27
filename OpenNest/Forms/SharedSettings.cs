using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// The settings every workstation shares through the nest server in Database mode: machines,
    /// post settings, clamp settings and nest defaults (see docs/shared-settings.md). Each kind
    /// is read from the server once it holds a copy and from this PC's own files until then.
    /// The server's copy is re-read when it is older than a few minutes, and always before
    /// posting or editing machines; while the server is unreachable the last copy read is used
    /// and nothing shared can be changed.
    /// </summary>
    internal static class SharedSettings
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan OfflineRetry = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private static readonly object sync = new();
        private static SharedSettingsStore store;
        private static string storeUrl;
        private static bool offlineWarned;

        /// <summary>
        /// The shared settings for the configured server, re-read from it when
        /// <paramref name="refresh"/> is set or the copy is stale; null in File mode or when the
        /// server predates shared settings.
        /// </summary>
        public static SharedSettingsStore Current(bool refresh = false)
        {
            var settings = NestStorage.Settings;
            if (!settings.IsDatabaseMode)
                return null;

            lock (sync)
            {
                if (store == null || storeUrl != settings.ServerUrl)
                {
                    store?.Dispose();
                    store = null;
                    try
                    {
                        store = new SharedSettingsStore(
                            new RemoteSharedSettings(settings.ServerUrl, Timeout),
                            CachePath(settings.ServerUrl),
                            Environment.UserName);
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                    storeUrl = settings.ServerUrl;
                }

                var age = DateTime.Now - (store.RefreshedAt ?? DateTime.MinValue);
                if (refresh || age > (store.IsOffline ? OfflineRetry : MaxAge))
                    store.Refresh();
                return store.IsUnsupported ? null : store;
            }
        }

        /// <summary>The shared document of <paramref name="kind"/> under <paramref name="key"/>, or null when this PC's own is used.</summary>
        public static SharedSettingRecord Find(string kind, string key, bool refresh = false) =>
            Current(refresh)?.Get(kind, key);

        /// <summary>The store when the server holds documents of <paramref name="kind"/>, so they replace this PC's own.</summary>
        public static SharedSettingsStore Holding(string kind, bool refresh = false) =>
            Current(refresh) is { } current && current.List(kind).Count > 0 ? current : null;

        /// <summary>Where this PC keeps its last copy of a server's shared settings.</summary>
        private static string CachePath(string serverUrl)
        {
            var key = (serverUrl ?? "").Trim().TrimEnd('/').ToLowerInvariant();
            var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest",
                "SharedSettings",
                name + ".json");
        }

        /// <summary>
        /// Tells the user once per session that the server could not be reached and the last
        /// copy read is in use.
        /// </summary>
        public static void WarnIfOffline(IWin32Window owner)
        {
            var current = store;
            if (current is not { IsOffline: true } || offlineWarned)
                return;
            offlineWarned = true;
            var copy = current.ReadAt is { } readAt
                ? $"the copy read {readAt:g}"
                : "this PC's own settings (no copy of the shared ones has been read yet)";
            MessageBox.Show(owner,
                $"The nest server could not be reached, so machines, post settings, clamps and nest defaults come from {copy}. "
                + "They cannot be changed until the server is back.\n\n" + current.LastError?.Message,
                "Shared Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Where Tools > Machine Configuration reads and saves, for its title.</summary>
        public static string MachineSource(IDataProvider provider)
        {
            if (provider is not SharedMachineProvider)
                return Current() == null ? "" : "this PC only: not shared on the server yet";
            return store?.IsOffline == true ? "shared, read-only: the server cannot be reached" : "shared on the nest server";
        }

        /// <summary>
        /// Where Tools > Nest Defaults saves (the server once it holds them, as
        /// <see cref="MachineSettings.SaveNestDefaults"/> does) and whether it can save there now.
        /// </summary>
        public static (string Text, bool ReadOnly) NestDefaultsSource()
        {
            if (Find(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey) == null)
                return (Current() == null ? "Saved on this PC." : "Saved on this PC only: not shared on the server yet.", false);
            return store?.IsOffline == true
                ? ("Shared, read-only: the server cannot be reached.", true)
                : ("Shared with every workstation on the nest server.", false);
        }

        /// <summary>The key of a post's shared settings.</summary>
        public static string PostKey(IPostProcessor postProcessor) => postProcessor.GetType().FullName;

        /// <summary>
        /// Gives <paramref name="postProcessor"/> the server's settings, re-read for this post, when
        /// the server holds them. Returns false (after telling the user) when they are unusable.
        /// </summary>
        public static bool LoadPostSettings(IWin32Window owner, IPostProcessor postProcessor)
        {
            if (postProcessor is not IJsonConfigPostProcessor post)
                return true;
            var record = Find(SharedSettingKinds.Post, PostKey(post), refresh: true);
            WarnIfOffline(owner);
            if (record == null)
                return true;
            try
            {
                post.LoadConfig(record.Json);
                return true;
            }
            catch (System.Text.Json.JsonException ex)
            {
                MessageBox.Show(owner,
                    $"The shared {post.Name} settings on the nest server could not be read, so nothing was posted.\n\n{ex.Message}",
                    "Post Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Saves a post's settings where they are read: the server once it holds them, otherwise
        /// the post's own file. JSON equal to <paramref name="unchanged"/> (the settings before the
        /// dialog opened) is not saved, so posting without editing never writes a shared revision.
        /// Throws <see cref="IOException"/> when the server cannot take them.
        /// </summary>
        public static void SavePostSettings(IConfigurablePostProcessor postProcessor, string unchanged)
        {
            if (postProcessor is not IJsonConfigPostProcessor post)
            {
                postProcessor.SaveConfig();
                return;
            }
            var json = post.SerializeConfig();
            if (json == unchanged)
                return;
            if (Current() is { } current && current.Get(SharedSettingKinds.Post, PostKey(post)) != null)
                current.Save(SharedSettingKinds.Post, PostKey(post), json);
            else
                post.SaveConfig();
        }

        /// <summary>
        /// When the server holds none of this PC's machines (with their clamps), post settings or nest
        /// defaults, offers to upload them so every workstation uses them. Documents the server
        /// already holds are never replaced.
        /// </summary>
        public static void OfferUpload(IWin32Window owner, IEnumerable<IPostProcessor> postProcessors)
        {
            var current = Current(refresh: true);
            WarnIfOffline(owner);
            if (current == null || current.IsOffline)
                return;

            var uploads = new List<(string Kind, string Key, string Json, string Label)>();
            if (current.List(SharedSettingKinds.Machine).Count == 0)
            {
                foreach (var machine in MachineSettings.LoadLocalMachines())
                    uploads.Add((SharedSettingKinds.Machine, machine.Id.ToString("D"), SharedMachineProvider.Serialize(machine),
                        $"Machine: {machine.Name}"));
            }
            foreach (var post in postProcessors.OfType<IJsonConfigPostProcessor>())
            {
                if (current.Get(SharedSettingKinds.Post, PostKey(post)) == null)
                    uploads.Add((SharedSettingKinds.Post, PostKey(post), post.SerializeConfig(), $"Post settings: {post.Name}"));
            }
            if (current.Get(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey) == null
                && File.Exists(NestDefaults.DefaultPath))
                uploads.Add((SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey,
                    MachineSettings.LoadLocalNestDefaults().ToJson(), "Nest defaults"));
            if (uploads.Count == 0)
                return;

            var answer = MessageBox.Show(owner,
                "The nest server does not have these settings yet:\n\n"
                + string.Join("\n", uploads.Select(u => "• " + u.Label))
                + "\n\nUpload this PC's copies so every workstation nests and posts with them? "
                + "Until then, each PC keeps using its own.",
                "Shared Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            var failures = new List<string>();
            foreach (var upload in uploads)
            {
                try
                {
                    current.Save(upload.Kind, upload.Key, upload.Json);
                }
                catch (IOException ex)
                {
                    failures.Add($"{upload.Label}: {ex.Message}");
                }
            }
            if (failures.Count > 0)
                MessageBox.Show(owner, "Some settings were not uploaded:\n\n" + string.Join("\n", failures),
                    "Shared Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
