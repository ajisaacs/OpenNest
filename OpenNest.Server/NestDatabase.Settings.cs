using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Settings every workstation shares (<see cref="SharedSettingKinds"/>): <c>shared_settings</c>
/// holds each document's current revision and <c>shared_setting_revisions</c> every revision,
/// including deletions, so a bad change can be traced and recovered by hand. A deleted document
/// keeps its row; creating it again continues its revision count, so a client holding an old
/// revision can never overwrite the new one.
/// </summary>
public sealed partial class NestDatabase
{
    private const string SettingColumnDefinitions = """
            kind     TEXT NOT NULL,
            key      TEXT NOT NULL,
            revision INTEGER NOT NULL,
            json     TEXT NOT NULL,
            deleted  INTEGER NOT NULL DEFAULT 0,
            savedAt  TEXT NOT NULL,
            savedBy  TEXT NOT NULL DEFAULT ''
        """;

    // v12 -> v13. The tables start empty: the first workstation offers to upload its own settings.
    private const string SharedSettingSchema = $"""
        CREATE TABLE IF NOT EXISTS shared_settings ({SettingColumnDefinitions}, PRIMARY KEY (kind, key));
        CREATE TABLE IF NOT EXISTS shared_setting_revisions ({SettingColumnDefinitions}, PRIMARY KEY (kind, key, revision));
        PRAGMA user_version=13;
        """;

    private const string SettingColumns = "kind, key, revision, json, deleted, savedAt, savedBy";

    /// <summary>Every current, undeleted settings document, by kind then key.</summary>
    public IReadOnlyList<SharedSettingRecord> ListSharedSettings()
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {SettingColumns} FROM shared_settings WHERE deleted=0 ORDER BY kind, key";
            using var reader = command.ExecuteReader();
            var records = new List<SharedSettingRecord>();
            while (reader.Read())
                records.Add(ReadSetting(reader).Record);
            return records;
        }
    }

    /// <summary>The current document, or null when it was never stored or is deleted.</summary>
    public SharedSettingRecord? GetSharedSetting(string kind, string key)
    {
        var stored = FindSetting(kind, key);
        return stored is { Deleted: false } ? stored.Value.Record : null;
    }

    /// <summary>
    /// Stores a new document (revision 1, or the next revision of a deleted one). A document
    /// already stored is a conflict carrying it, unless it holds the same JSON, which is a no-op.
    /// </summary>
    public (SharedSettingRecord Record, bool Created) CreateSharedSetting(string kind, string key, SharedSettingWrite write)
    {
        key = Validate(kind, key, write);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var stored = FindSetting(kind, key);
            if (stored is { Deleted: false } current)
            {
                if (current.Record.Json == write.Json)
                    return (current.Record, false);
                throw new SharedSettingConflictException(current.Record, "These settings already exist. Reload before saving.");
            }

            var record = WriteSetting(kind, key, write, (stored?.Record.Revision ?? 0) + 1, deleted: false, transaction);
            transaction.Commit();
            return (record, true);
        }
    }

    /// <summary>
    /// Appends a revision. Returns null when the document is not stored (or deleted); a stale
    /// revision is a conflict, and the same JSON is a no-op.
    /// </summary>
    public SharedSettingRecord? UpdateSharedSetting(string kind, string key, SharedSettingWrite write, int expectedRevision)
    {
        key = Validate(kind, key, write);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (FindSetting(kind, key) is not { Deleted: false } current)
                return null;
            if (current.Record.Revision != expectedRevision)
                throw new SharedSettingConflictException(current.Record, "These settings were changed by someone else. Reload before saving.");
            if (current.Record.Json == write.Json)
                return current.Record;

            var record = WriteSetting(kind, key, write, checked(current.Record.Revision + 1), deleted: false, transaction);
            transaction.Commit();
            return record;
        }
    }

    /// <summary>Deletes the document as a new revision. Returns false when it is not stored (or already deleted).</summary>
    public bool DeleteSharedSetting(string kind, string key, string savedBy, int expectedRevision)
    {
        key = SharedSettingRules.NormalizeKey(kind, key) ?? throw new ArgumentException($"Invalid {kind} settings key.", nameof(key));
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (FindSetting(kind, key) is not { Deleted: false } current)
                return false;
            if (current.Record.Revision != expectedRevision)
                throw new SharedSettingConflictException(current.Record, "These settings were changed by someone else. Reload before deleting.");

            var write = new SharedSettingWrite { Json = current.Record.Json, SavedBy = savedBy ?? "" };
            WriteSetting(kind, key, write, checked(current.Record.Revision + 1), deleted: true, transaction);
            transaction.Commit();
            return true;
        }
    }

    private static string Validate(string kind, string key, SharedSettingWrite write)
    {
        if (SharedSettingRules.GetError(kind, key, write) is { } error)
            throw new ArgumentException(error, nameof(write));
        return SharedSettingRules.NormalizeKey(kind, key)!;
    }

    private (SharedSettingRecord Record, bool Deleted)? FindSetting(string kind, string? key)
    {
        key = SharedSettingRules.NormalizeKey(kind, key);
        if (key is null)
            return null;
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {SettingColumns} FROM shared_settings WHERE kind=$kind AND key=$key";
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$key", key);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadSetting(reader) : null;
        }
    }

    private SharedSettingRecord WriteSetting(string kind, string key, SharedSettingWrite write, int revision, bool deleted,
        SqliteTransaction transaction)
    {
        var record = new SharedSettingRecord
        {
            Kind = kind,
            Key = key,
            Revision = revision,
            Json = write.Json,
            SavedAt = DateTime.Now,
            SavedBy = write.SavedBy ?? "",
        };
        foreach (var statement in new[] { "INSERT OR REPLACE INTO shared_settings", "INSERT INTO shared_setting_revisions" })
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"{statement} ({SettingColumns}) VALUES ($kind, $key, $revision, $json, $deleted, $savedAt, $savedBy)";
            command.Parameters.AddWithValue("$kind", record.Kind);
            command.Parameters.AddWithValue("$key", record.Key);
            command.Parameters.AddWithValue("$revision", record.Revision);
            command.Parameters.AddWithValue("$json", record.Json);
            command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
            command.Parameters.AddWithValue("$savedAt", record.SavedAt.ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$savedBy", record.SavedBy);
            command.ExecuteNonQuery();
        }
        return record;
    }

    private static (SharedSettingRecord Record, bool Deleted) ReadSetting(SqliteDataReader reader) => (
        new SharedSettingRecord
        {
            Kind = reader.GetString(0),
            Key = reader.GetString(1),
            Revision = reader.GetInt32(2),
            Json = reader.GetString(3),
            SavedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            SavedBy = reader.GetString(6),
        },
        reader.GetInt64(4) != 0);
}

/// <summary>A guarded settings write found a different current document; nothing was changed.</summary>
public sealed class SharedSettingConflictException(SharedSettingRecord current, string message) : Exception(message)
{
    public SharedSettingRecord Current { get; } = current;
}
