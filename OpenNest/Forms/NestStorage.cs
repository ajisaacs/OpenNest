using System;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// App-scoped nest storage-mode selection (File vs Database) and the shared
    /// <see cref="RemoteNestRepository"/> for the configured server. Settings persist
    /// to %APPDATA%\OpenNest\storage.json via <see cref="NestStorageSettings"/>.
    /// </summary>
    public static class NestStorage
    {
        private static NestStorageSettings settings =
            NestStorageSettings.Load(NestStorageSettings.DefaultPath);

        private static RemoteNestRepository cachedRepository;
        private static string cachedServerUrl;
        private static RemoteDrawingLibrary cachedLibrary;
        private static string cachedLibraryUrl;
        private static RemoteDrawingSetStore cachedSets;
        private static string cachedSetsUrl;

        /// <summary>Tooltip for drawing library and set commands while File mode is active.</summary>
        public const string DatabaseModeOnly = "Database mode only";

        /// <summary>Current settings, loaded once at process start.</summary>
        public static NestStorageSettings Settings => settings;

        /// <summary>
        /// Persists new settings and invalidates the cached repository so the next
        /// <see cref="CreateRepository"/> call picks up a changed server URL.
        /// If persistence fails the current mode remains unchanged.
        /// </summary>
        public static void Save(NestStorageMode mode, string serverUrl)
        {
            var next = new NestStorageSettings { Mode = mode, ServerUrl = serverUrl };
            next.Save(NestStorageSettings.DefaultPath);
            settings = next;
        }

        /// <summary>
        /// The shared repository for the current server URL, recreated only when the
        /// URL changes. Throws <see cref="ArgumentException"/> if the configured URL
        /// is not a valid absolute http/https address.
        /// </summary>
        public static INestRepository CreateRepository() =>
            GetOrCreate(ref cachedRepository, ref cachedServerUrl, url => new RemoteNestRepository(url));

        /// <summary>
        /// The shared drawing library client for the current server URL, recreated only when
        /// the URL changes. Throws <see cref="ArgumentException"/> for an invalid URL.
        /// </summary>
        public static IDrawingLibraryStore CreateDrawingLibrary() =>
            GetOrCreate(ref cachedLibrary, ref cachedLibraryUrl, url => new RemoteDrawingLibrary(url));

        /// <summary>
        /// The shared drawing set client for the current server URL, recreated only when the
        /// URL changes. Throws <see cref="ArgumentException"/> for an invalid URL.
        /// </summary>
        public static IDrawingSetStore CreateDrawingSetStore() =>
            GetOrCreate(ref cachedSets, ref cachedSetsUrl, url => new RemoteDrawingSetStore(url));

        private static T GetOrCreate<T>(ref T cached, ref string cachedUrl, Func<string, T> create)
            where T : NestServerClient
        {
            if (cached == null || cachedUrl != settings.ServerUrl)
            {
                cached?.Dispose();
                cached = create(settings.ServerUrl);
                cachedUrl = settings.ServerUrl;
            }

            return cached;
        }
    }
}
