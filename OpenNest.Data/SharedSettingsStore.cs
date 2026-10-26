using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// The shared settings as last read from the nest server, for callers that read settings
/// synchronously (nest creation, posting). Every successful read is copied to a cache file;
/// when the server cannot be reached the cached copy is served instead and
/// <see cref="IsOffline"/> is set. Writes always go to the server, so nothing can be changed
/// while it is unreachable. Thread-safe.
/// </summary>
public sealed class SharedSettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions CacheOptions = new(NestServerHttp.JsonOptions) { WriteIndented = true };

    private readonly object _sync = new();
    private readonly RemoteSharedSettings _remote;
    private readonly string _cachePath;
    private readonly string _savedBy;
    private List<SharedSettingRecord>? _records;

    /// <param name="cachePath">The file holding this server's last-read copy.</param>
    /// <param name="savedBy">Who saves through this store, recorded with every change.</param>
    public SharedSettingsStore(RemoteSharedSettings remote, string cachePath, string savedBy)
    {
        _remote = remote;
        _cachePath = cachePath;
        _savedBy = savedBy ?? "";
    }

    /// <summary>The last refresh could not reach the server; reads come from the cached copy, if any.</summary>
    public bool IsOffline { get; private set; }

    /// <summary>The server predates shared settings: there are none, and none can be saved.</summary>
    public bool IsUnsupported { get; private set; }

    /// <summary>When the settings being served were read from the server, or null when there are none.</summary>
    public DateTime? ReadAt { get; private set; }

    /// <summary>Why the last refresh failed, or null after a successful one.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>When the last refresh was attempted, successful or not.</summary>
    public DateTime? RefreshedAt { get; private set; }

    /// <summary>
    /// Reads every document from the server. Returns false when the server could not be
    /// reached; the cached copy (or nothing) is then served.
    /// </summary>
    public bool Refresh()
    {
        try
        {
            var records = Task.Run(() => _remote.ListAsync()).GetAwaiter().GetResult();
            lock (_sync)
            {
                _records = records.ToList();
                ReadAt = DateTime.Now;
                RefreshedAt = ReadAt;
                IsOffline = false;
                IsUnsupported = false;
                LastError = null;
                WriteCache();
            }
            return true;
        }
        catch (ServerTooOldException ex)
        {
            lock (_sync)
            {
                _records = new List<SharedSettingRecord>();
                ReadAt = null;
                RefreshedAt = DateTime.Now;
                IsOffline = false;
                IsUnsupported = true;
                LastError = ex;
            }
            return true;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            lock (_sync)
            {
                if (_records is null)
                    ReadCache();
                RefreshedAt = DateTime.Now;
                IsOffline = true;
                LastError = ex;
            }
            return false;
        }
    }

    /// <summary>Every document of <paramref name="kind"/>, by key.</summary>
    public IReadOnlyList<SharedSettingRecord> List(string kind)
    {
        lock (_sync)
            return (_records ?? []).Where(record => record.Kind == kind).OrderBy(record => record.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>The document of <paramref name="kind"/> under <paramref name="key"/>, or null when none is shared.</summary>
    public SharedSettingRecord? Get(string kind, string key)
    {
        var normalized = SharedSettingRules.NormalizeKey(kind, key);
        lock (_sync)
            return _records?.FirstOrDefault(record => record.Kind == kind && record.Key == normalized);
    }

    /// <summary>
    /// Stores <paramref name="json"/> on the server, creating the document or replacing the
    /// revision last read. Unchanged JSON is not sent. Throws <see cref="SharedSettingSaveConflictException"/>
    /// (after taking the newer document) when someone else changed it, and <see cref="IOException"/>
    /// when the server cannot be reached.
    /// </summary>
    public SharedSettingRecord Save(string kind, string key, string json)
    {
        var current = Get(kind, key);
        if (current is not null && current.Json == json)
            return current;
        var write = new SharedSettingWrite { Json = json, SavedBy = _savedBy };
        return Send(kind, key, async () => current is null
            ? await _remote.CreateAsync(kind, key, write)
            : await _remote.UpdateAsync(kind, key, write, current.Revision));
    }

    /// <summary>Deletes the document, if shared. Throws as <see cref="Save"/> does.</summary>
    public void Delete(string kind, string key)
    {
        if (Get(kind, key) is not { } current)
            return;
        Send(kind, key, async () =>
        {
            await _remote.DeleteAsync(kind, key, _savedBy, current.Revision);
            return null;
        });
    }

    private SharedSettingRecord Send(string kind, string key, Func<Task<SharedSettingRecord?>> send)
    {
        SharedSettingRecord? saved;
        try
        {
            saved = Task.Run(send).GetAwaiter().GetResult();
        }
        catch (SharedSettingSaveConflictException conflict)
        {
            lock (_sync)
                Replace(kind, key, conflict.Current);
            throw;
        }
        catch (SharedSettingMissingException)
        {
            lock (_sync)
                Replace(kind, key, null);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new IOException(
                "The nest server could not be reached, so the shared settings cannot be changed until it is back. " + ex.Message, ex);
        }

        lock (_sync)
            Replace(kind, key, saved);
        return saved!;
    }

    private void Replace(string kind, string key, SharedSettingRecord? record)
    {
        var normalized = SharedSettingRules.NormalizeKey(kind, key);
        _records ??= new List<SharedSettingRecord>();
        _records.RemoveAll(existing => existing.Kind == kind && existing.Key == normalized);
        if (record is not null)
            _records.Add(record);
        if (!IsOffline)
            WriteCache();
    }

    private void WriteCache()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_cachePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Cache { ReadAt = ReadAt, Records = _records ?? [] }, CacheOptions));
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy only matters while the server is down; the settings themselves are on the server.
        }
    }

    private void ReadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return;
            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath), CacheOptions);
            if (cache?.Records is null || cache.Records.Any(record => record is null))
                return;
            _records = cache.Records.Where(record => SharedSettingKinds.IsKnown(record.Kind)).ToList();
            ReadAt = cache.ReadAt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable copy is the same as none.
        }
    }

    // A server error is treated like an unreachable server: the cached copy is still the best available.
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;

    public void Dispose() => _remote.Dispose();

    private sealed class Cache
    {
        public DateTime? ReadAt { get; set; }

        public List<SharedSettingRecord> Records { get; set; } = [];
    }
}
