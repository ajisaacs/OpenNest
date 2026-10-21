using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// The company material list: <c>materials</c> and each one's <c>material_grades</c>, keyed by
/// <see cref="SharedListNames.Key"/>. Nests, drawings and releases keep their own free-text
/// material and grade; the list only suggests names and fixes their spelling.
/// </summary>
public sealed partial class NestDatabase
{
    // v9 -> v10. The list starts empty: it is filled through POST /api/materials, not from
    // the free-text materials already stored. IF NOT EXISTS keeps the step safe on a database
    // rewound to v9.
    private const string MaterialSchema = """
        CREATE TABLE IF NOT EXISTS materials (
            key       TEXT NOT NULL PRIMARY KEY,
            name      TEXT NOT NULL,
            createdAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS material_grades (
            materialKey TEXT NOT NULL REFERENCES materials(key),
            key         TEXT NOT NULL,
            name        TEXT NOT NULL,
            createdAt   TEXT NOT NULL,
            PRIMARY KEY (materialKey, key));
        PRAGMA user_version=10;
        """;

    /// <summary>Every listed material with its grades, both in case-insensitive name order.</summary>
    public IReadOnlyList<MaterialListEntry> ListMaterials()
    {
        lock (_sync)
        {
            var entries = new List<MaterialListEntry>();
            var byKey = new Dictionary<string, MaterialListEntry>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT key, name FROM materials ORDER BY name COLLATE NOCASE, key";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var entry = new MaterialListEntry { Name = reader.GetString(1) };
                    byKey[reader.GetString(0)] = entry;
                    entries.Add(entry);
                }
            }

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT materialKey, name FROM material_grades ORDER BY name COLLATE NOCASE, key";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    byKey[reader.GetString(0)].Grades.Add(reader.GetString(1));
            }

            return entries;
        }
    }

    /// <summary>
    /// Adds a material and, when given, one of its grades; names already listed by key keep
    /// their spelling. Returns the material as now listed and whether anything was added.
    /// </summary>
    public (MaterialListEntry Entry, bool Added) AddMaterial(MaterialListAddition addition)
    {
        if (MaterialListRules.GetError(addition) is { } error)
            throw new ArgumentException(error, nameof(addition));

        var name = MaterialListRules.Spelling(addition.Name);
        var key = SharedListNames.Key(name);
        var grade = MaterialListRules.Spelling(addition.Grade);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var now = DateTime.Now.ToString("o");
            var added = InsertMaterial(key, name, now, transaction);
            if (grade.Length > 0)
                added |= InsertGrade(key, SharedListNames.Key(grade), grade, now, transaction);
            transaction.Commit();
            return (ListMaterials().Single(entry => SharedListNames.Key(entry.Name) == key), added);
        }
    }

    private bool InsertMaterial(string key, string name, string createdAt, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO materials (key, name, createdAt) VALUES ($key, $name, $createdAt)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$createdAt", createdAt);
        return command.ExecuteNonQuery() > 0;
    }

    private bool InsertGrade(string materialKey, string key, string name, string createdAt, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO material_grades (materialKey, key, name, createdAt)
            VALUES ($materialKey, $key, $name, $createdAt)
            """;
        command.Parameters.AddWithValue("$materialKey", materialKey);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$createdAt", createdAt);
        return command.ExecuteNonQuery() > 0;
    }
}
