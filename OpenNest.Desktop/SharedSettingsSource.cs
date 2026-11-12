using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenNest.Data;

namespace OpenNest.Desktop;

/// <summary>A document this PC could upload to a nest server that does not hold it yet.</summary>
public sealed record SharedSettingUpload(string Kind, string Key, string Json, string Label);

/// <summary>
/// The settings every workstation shares through the nest server in Database mode: machines,
/// post settings, clamp settings and nest defaults (see docs/shared-settings.md). Each kind
/// is read from the server once it holds a copy and from this PC's own files until then.
/// The server's copy is re-read when it is older than a few minutes, and always before
/// posting or editing machines; while the server is unreachable the last copy read is used
/// and nothing shared can be changed. Front ends show the texts it returns.
/// </summary>
public sealed class SharedSettingsSource
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OfflineRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly object sync = new();
    private readonly Func<NestStorageSettings> storageSettings;
    private readonly Func<string, SharedSettingsStore> createStore;
    private readonly Func<DateTime> now;
    private SharedSettingsStore store;
    private string storeUrl;
    private bool offlineWarned;

    /// <param name="storageSettings">The current storage mode and server, read on every use.</param>
    /// <param name="createStore">Makes the store for a server URL; defaults to <see cref="CreateStore"/>.</param>
    /// <param name="now">The clock deciding when the copy is stale; defaults to <see cref="DateTime.Now"/>.</param>
    public SharedSettingsSource(
        Func<NestStorageSettings> storageSettings,
        Func<string, SharedSettingsStore> createStore = null,
        Func<DateTime> now = null)
    {
        this.storageSettings = storageSettings ?? throw new ArgumentNullException(nameof(storageSettings));
        this.createStore = createStore ?? CreateStore;
        this.now = now ?? (() => DateTime.Now);
    }

    /// <summary>
    /// A source reading the storage mode once from %APPDATA%\OpenNest\storage.json, for a front
    /// end that does not change it while running.
    /// </summary>
    public static SharedSettingsSource FromStorageFile(string path = null)
    {
        var settings = NestStorageSettings.Load(path ?? NestStorageSettings.DefaultPath);
        return new SharedSettingsSource(() => settings);
    }

    /// <summary>
    /// The store for <paramref name="serverUrl"/>: requests give up after five seconds and the
    /// last copy read is kept under %APPDATA%\OpenNest\SharedSettings. Throws
    /// <see cref="ArgumentException"/> for an invalid URL.
    /// </summary>
    public static SharedSettingsStore CreateStore(string serverUrl) =>
        new(new RemoteSharedSettings(serverUrl, Timeout), CachePath(serverUrl), Environment.UserName);

    /// <summary>Where this PC keeps its last copy of a server's shared settings.</summary>
    public static string CachePath(string serverUrl)
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
    /// The shared settings for the configured server, re-read from it when
    /// <paramref name="refresh"/> is set or the copy is stale; null in File mode or when the
    /// server predates shared settings.
    /// </summary>
    public SharedSettingsStore Current(bool refresh = false)
    {
        var settings = storageSettings();
        if (settings == null || !settings.IsDatabaseMode)
            return null;

        lock (sync)
        {
            if (store == null || storeUrl != settings.ServerUrl)
            {
                store?.Dispose();
                store = null;
                try
                {
                    store = createStore(settings.ServerUrl);
                }
                catch (ArgumentException)
                {
                    return null;
                }
                storeUrl = settings.ServerUrl;
            }

            var age = now() - (store.RefreshedAt ?? DateTime.MinValue);
            if (refresh || age > (store.IsOffline ? OfflineRetry : MaxAge))
                store.Refresh();
            return store.IsUnsupported ? null : store;
        }
    }

    /// <summary>The shared document of <paramref name="kind"/> under <paramref name="key"/>, or null when this PC's own is used.</summary>
    public SharedSettingRecord Find(string kind, string key, bool refresh = false) =>
        Current(refresh)?.Get(kind, key);

    /// <summary>The store when the server holds documents of <paramref name="kind"/>, so they replace this PC's own.</summary>
    public SharedSettingsStore Holding(string kind, bool refresh = false) =>
        Current(refresh) is { } current && current.List(kind).Count > 0 ? current : null;

    /// <summary>Whether the last read of the server failed, so the last copy read is in use.</summary>
    public bool IsOffline => store?.IsOffline == true;

    /// <summary>
    /// What to tell the user, once per session, when the server could not be reached and the
    /// last copy read is in use; null when there is nothing (more) to say.
    /// </summary>
    public string TakeOfflineWarning()
    {
        var current = store;
        if (current is not { IsOffline: true } || offlineWarned)
            return null;
        offlineWarned = true;
        var copy = current.ReadAt is { } readAt
            ? $"the copy read {readAt:g}"
            : "this PC's own settings (no copy of the shared ones has been read yet)";
        return $"The nest server could not be reached, so machines, post settings, clamps and nest defaults come from {copy}. "
            + "They cannot be changed until the server is back.\n\n" + current.LastError?.Message;
    }

    /// <summary>Where Tools > Machine Configuration reads and saves, for its title.</summary>
    public string MachineSource(IDataProvider provider)
    {
        if (provider is not SharedMachineProvider)
            return Current() == null ? "" : "this PC only: not shared on the server yet";
        return store?.IsOffline == true ? "shared, read-only: the server cannot be reached" : "shared on the nest server";
    }

    /// <summary>
    /// Where Tools > Nest Defaults saves (the server once it holds them) and whether it can save there now.
    /// </summary>
    public (string Text, bool ReadOnly) NestDefaultsSource()
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
    /// Gives a post that shares its settings the server's, re-read for this post, when the
    /// server holds them. Returns why they are unusable (nothing may be posted), or null.
    /// </summary>
    public string LoadPostSettings(IJsonConfigPostProcessor post)
    {
        var record = Find(SharedSettingKinds.Post, PostKey(post), refresh: true);
        if (record == null)
            return null;
        try
        {
            post.LoadConfig(record.Json);
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            return $"The shared {post.Name} settings on the nest server could not be read, so nothing was posted.\n\n{ex.Message}";
        }
    }

    /// <summary>
    /// Saves a post's settings where they are read: the server once it holds them, otherwise
    /// the post's own file. JSON equal to <paramref name="unchanged"/> (the settings before the
    /// dialog opened) is not saved, so posting without editing never writes a shared revision.
    /// Throws <see cref="IOException"/> when the server cannot take them.
    /// </summary>
    public void SavePostSettings(IConfigurablePostProcessor postProcessor, string unchanged)
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
    /// This PC's machines (with their clamps; only while the server holds none), post settings
    /// and nest defaults (when <paramref name="localDefaultsJson"/> gives them) that
    /// <paramref name="current"/> does not hold yet. Documents the server holds are never listed.
    /// </summary>
    public static IReadOnlyList<SharedSettingUpload> PendingUploads(
        SharedSettingsStore current,
        Func<IEnumerable<MachineConfig>> localMachines,
        IEnumerable<IPostProcessor> postProcessors,
        Func<string> localDefaultsJson)
    {
        var uploads = new List<SharedSettingUpload>();
        if (current.List(SharedSettingKinds.Machine).Count == 0)
        {
            foreach (var machine in localMachines())
                uploads.Add(new(SharedSettingKinds.Machine, machine.Id.ToString("D"), SharedMachineProvider.Serialize(machine),
                    $"Machine: {machine.Name}"));
        }
        foreach (var post in (postProcessors ?? []).OfType<IJsonConfigPostProcessor>())
        {
            if (current.Get(SharedSettingKinds.Post, PostKey(post)) == null)
                uploads.Add(new(SharedSettingKinds.Post, PostKey(post), post.SerializeConfig(), $"Post settings: {post.Name}"));
        }
        if (current.Get(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey) == null
            && localDefaultsJson() is { } defaults)
            uploads.Add(new(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey, defaults, "Nest defaults"));
        return uploads;
    }

    /// <summary>The question offering <paramref name="uploads"/> to the server.</summary>
    public static string DescribeUploads(IEnumerable<SharedSettingUpload> uploads) =>
        "The nest server does not have these settings yet:\n\n"
        + string.Join("\n", uploads.Select(u => "• " + u.Label))
        + "\n\nUpload this PC's copies so every workstation nests and posts with them? "
        + "Until then, each PC keeps using its own.";

    /// <summary>
    /// Saves each upload to <paramref name="current"/>; returns what to tell the user about the
    /// ones that failed, or null when all were uploaded.
    /// </summary>
    public static string Upload(SharedSettingsStore current, IEnumerable<SharedSettingUpload> uploads)
    {
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
        return failures.Count > 0 ? "Some settings were not uploaded:\n\n" + string.Join("\n", failures) : null;
    }
}
