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
        public static INestRepository CreateRepository()
        {
            if (cachedRepository == null || cachedServerUrl != settings.ServerUrl)
            {
                cachedRepository?.Dispose();
                cachedRepository = new RemoteNestRepository(settings.ServerUrl);
                cachedServerUrl = settings.ServerUrl;
            }

            return cachedRepository;
        }
    }
}
