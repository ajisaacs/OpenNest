using System.Collections.Generic;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Forms
{
    /// <summary>
    /// The WinForms entry points for the settings every workstation shares through the nest
    /// server in Database mode (see docs/shared-settings.md). The rules live in the shared
    /// <see cref="SharedSettingsSource"/>, following this app's storage mode; this shows its
    /// warnings and questions.
    /// </summary>
    internal static class SharedSettings
    {
        /// <summary>The shared settings for the storage mode and server <see cref="NestStorage"/> has now.</summary>
        internal static SharedSettingsSource Source { get; } = new(() => NestStorage.Settings);

        /// <summary>
        /// The shared settings for the configured server, re-read from it when
        /// <paramref name="refresh"/> is set or the copy is stale; null in File mode or when the
        /// server predates shared settings.
        /// </summary>
        public static SharedSettingsStore Current(bool refresh = false) => Source.Current(refresh);

        /// <summary>The shared document of <paramref name="kind"/> under <paramref name="key"/>, or null when this PC's own is used.</summary>
        public static SharedSettingRecord Find(string kind, string key, bool refresh = false) =>
            Source.Find(kind, key, refresh);

        /// <summary>The store when the server holds documents of <paramref name="kind"/>, so they replace this PC's own.</summary>
        public static SharedSettingsStore Holding(string kind, bool refresh = false) => Source.Holding(kind, refresh);

        /// <summary>
        /// Tells the user once per session that the server could not be reached and the last
        /// copy read is in use.
        /// </summary>
        public static void WarnIfOffline(IWin32Window owner)
        {
            if (Source.TakeOfflineWarning() is { } warning)
                MessageBox.Show(owner, warning, "Shared Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Where Tools > Machine Configuration reads and saves, for its title.</summary>
        public static string MachineSource(IDataProvider provider) => Source.MachineSource(provider);

        /// <summary>
        /// Where Tools > Nest Defaults saves (the server once it holds them, as
        /// <see cref="MachineSettings.SaveNestDefaults(NestDefaults)"/> does) and whether it can save there now.
        /// </summary>
        public static (string Text, bool ReadOnly) NestDefaultsSource() => Source.NestDefaultsSource();

        /// <summary>The key of a post's shared settings.</summary>
        public static string PostKey(IPostProcessor postProcessor) => SharedSettingsSource.PostKey(postProcessor);

        /// <summary>
        /// Gives <paramref name="postProcessor"/> the server's settings, re-read for this post, when
        /// the server holds them. Returns false (after telling the user) when they are unusable.
        /// </summary>
        public static bool LoadPostSettings(IWin32Window owner, IPostProcessor postProcessor)
        {
            if (postProcessor is not IJsonConfigPostProcessor post)
                return true;
            var error = Source.LoadPostSettings(post);
            WarnIfOffline(owner);
            if (error == null)
                return true;
            MessageBox.Show(owner, error, "Post Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        /// <summary>
        /// Saves a post's settings where they are read (see
        /// <see cref="SharedSettingsSource.SavePostSettings"/>). Throws
        /// <see cref="System.IO.IOException"/> when the server cannot take them.
        /// </summary>
        public static void SavePostSettings(IConfigurablePostProcessor postProcessor, string unchanged) =>
            Source.SavePostSettings(postProcessor, unchanged);

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

            var uploads = MachineSettings.Source.PendingUploads(current, postProcessors);
            if (uploads.Count == 0)
                return;

            var answer = MessageBox.Show(owner, SharedSettingsSource.DescribeUploads(uploads),
                "Shared Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            if (SharedSettingsSource.Upload(current, uploads) is { } failures)
                MessageBox.Show(owner, failures, "Shared Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
