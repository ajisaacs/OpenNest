using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// SQLite-backed nest storage: metadata columns plus the .nest archive as a BLOB.
/// The database is created on first use. Records are keyed by client-generated ids
/// so a PC that loses its local id mapping can still re-upload under a new id;
/// updates keep the id.
/// </summary>
public sealed partial class NestDatabase : IDisposable
{
    // One reentrant monitor owns each complete connection operation, including
    // readers, nested Get readbacks, health checks, and disposal.
    private readonly object _sync = new();
    private readonly SqliteConnection _connection;

    public NestDatabase(string databasePath, bool pooling = true)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Test databases are short-lived files: disabling pooling releases Windows file handles on dispose.
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = pooling,
        }.ToString());
        _connection.Open();
        try
        {
            Execute("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS nests (
                id           TEXT PRIMARY KEY,
                name         TEXT NOT NULL,
                customer     TEXT NOT NULL DEFAULT '',
                dateCreated  TEXT NOT NULL,
                dateModified TEXT NOT NULL,
                material     TEXT NOT NULL DEFAULT '',
                thickness    REAL NOT NULL DEFAULT 0,
                status       TEXT NOT NULL DEFAULT 'Quote',
                plateCount   INTEGER NOT NULL DEFAULT 0,
                partCount    INTEGER NOT NULL DEFAULT 0,
                comments     TEXT NOT NULL DEFAULT '',
                madeBy       TEXT NOT NULL DEFAULT '',
                fileSize     INTEGER NOT NULL DEFAULT 0,
                savedAt      TEXT NOT NULL,
                file         BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_nests_savedAt ON nests(savedAt DESC);
            """);
            Migrate();
        }
        catch { _connection.Dispose(); throw; }
    }

    public IReadOnlyList<NestRecord> List()
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                $"SELECT {RecordColumns} FROM nests WHERE deleted=0 ORDER BY savedAt DESC";
            var records = new List<NestRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(ReadRecord(reader));
            return records;
        }
    }

    public NestRecord? Get(Guid id, bool deleted = false)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {RecordColumns} FROM nests WHERE id = $id AND deleted=$deleted";
            command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }
    }

    /// <summary>
    /// One bounded page of matching metadata plus the total match count. The count and
    /// the page are read in the same monitor hold, so they describe one state.
    /// </summary>
    public NestPage Query(NestQuery query, bool deleted = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        var search = query.NormalizedSearch;
        var where = "WHERE deleted=" + (deleted ? "1" : "0") + (search.Length == 0 ? "" : " AND (" + SearchPredicate + ")");
        lock (_sync)
        {
            using var count = _connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM nests {where}";
            AddSearchParameter(count, search);
            var total = checked((int)(long)count.ExecuteScalar()!);

            using var command = _connection.CreateCommand();
            var direction = query.Descending ? "DESC" : "ASC";
            command.CommandText = $"""
                SELECT {RecordColumns} FROM nests {where}
                ORDER BY {SortColumn(query.Sort)} {direction}, id {direction}
                LIMIT $limit OFFSET $offset
                """;
            AddSearchParameter(command, search);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var records = new List<NestRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(ReadRecord(reader));

            return new NestPage { Items = records, Total = total, Offset = query.Offset, Limit = query.Limit };
        }
    }

    public byte[]? GetFile(Guid id, bool deleted = false)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT file FROM nests WHERE id = $id AND deleted=$deleted";
            command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
            command.Parameters.AddWithValue("$id", id.ToString());
            var value = command.ExecuteScalar();
            return value is byte[] bytes ? bytes : null;
        }
    }

    public NestRecord Insert(Guid id, NestRecord record, byte[] file)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            // Ids are client-allocated: an existing id, active or deleted, is a conflict, not a replacement.
            if ((Get(id) ?? Get(id, deleted: true)) is { } existing)
                throw new RevisionConflictException(existing, "A nest with this id already exists.");
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO nests (id, name, customer, dateCreated, dateModified, material,
                    thickness, status, plateCount, partCount, comments, madeBy, fileSize,
                    savedAt, file)
                VALUES ($id, $name, $customer, $dateCreated, $dateModified, $material,
                    $thickness, $status, $plateCount, $partCount, $comments, $madeBy,
                    $fileSize, $savedAt, $file)
                """;
            AddRecordParameters(command, id, record, file, updateFile: true);
            command.ExecuteNonQuery();
            Snapshot(id, transaction);
            transaction.Commit();
            return Get(id) ?? throw new InvalidOperationException("Insert did not persist.");
        }
    }

    public NestRecord? UpdateMetadata(Guid id, NestRecord record, int expectedRevision)
    {
        lock (_sync)
        {
            return Update(id, record, null, expectedRevision);
        }
    }

    public NestRecord? Update(Guid id, NestRecord record, byte[]? file, int expectedRevision)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = Get(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision) throw new RevisionConflictException(current);
            file ??= GetFile(id)!;
            if (SameMetadata(current, record) && GetFile(id)!.AsSpan().SequenceEqual(file)) return current;

            WriteRevision(id, record, file, current.Revision, null, transaction);
            transaction.Commit();
            return Get(id);
        }
    }

    // Called only under _sync with the caller's transaction; promotion and snapshot are one write.
    private void WriteRevision(Guid id, NestRecord record, byte[] file, int currentRevision,
        int? sourceRevision, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        const string fileClause = ", file = $file, fileSize = $fileSize";
        command.CommandText = $"""
                UPDATE nests SET
                    name = $name, customer = $customer, dateCreated = $dateCreated,
                    dateModified = $dateModified, material = $material,
                    thickness = $thickness, status = $status, plateCount = $plateCount,
                    partCount = $partCount, comments = $comments, madeBy = $madeBy,
                    savedAt = $savedAt, revision = $revision, restoredFromRevision=$source{fileClause}
                WHERE id = $id
                """;
        AddRecordParameters(command, id, record, file, updateFile: file is not null);
        command.Parameters.AddWithValue("$revision", checked(currentRevision + 1));
        command.Parameters.AddWithValue("$source", (object?)sourceRevision ?? DBNull.Value);
        command.ExecuteNonQuery();
        Snapshot(id, transaction);
    }

    public NestRecord? Restore(Guid id, int sourceRevision, int expectedRevision)
    {
        if (sourceRevision < 1) throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = Get(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision) throw new RevisionConflictException(current);
            NestRecord historical;
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT {RecordColumns} FROM nest_revisions WHERE id=$id AND revision=$revision";
                command.Parameters.AddWithValue("$id", id.ToString());
                command.Parameters.AddWithValue("$revision", sourceRevision);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                historical = ReadRecord(reader);
            }
            // Restore is a deliberate new active revision, including when the bytes already match.
            WriteRevision(id, historical, GetRevisionFile(id, sourceRevision)!, current.Revision, sourceRevision, transaction);
            transaction.Commit();
            return Get(id);
        }
    }

    public NestRecord? SetDeleted(Guid id, bool deleted, int expectedRevision)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = Get(id, !deleted);
            if (current is null) return null;
            if (current.Revision != expectedRevision) throw new RevisionConflictException(current);
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE nests SET deleted=$deleted, revision=$revision, savedAt=$savedAt, restoredFromRevision=NULL WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
            command.Parameters.AddWithValue("$revision", checked(current.Revision + 1));
            command.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("o"));
            command.ExecuteNonQuery();
            Snapshot(id, transaction);
            transaction.Commit();
            return Get(id, deleted);
        }
    }

    public NestPage? History(Guid id, int offset, int limit, bool deleted = false)
    {
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_sync)
        {
            if (Get(id, deleted) is null) return null;
            using var count = _connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM nest_revisions WHERE id=$id";
            count.Parameters.AddWithValue("$id", id.ToString());
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {RecordColumns} FROM nest_revisions WHERE id=$id ORDER BY revision DESC LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var records = new List<NestRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) records.Add(ReadRecord(reader));
            return new NestPage { Items = records, Total = total, Offset = offset, Limit = limit };
        }
    }

    public byte[]? GetRevisionFile(Guid id, int revision, bool deleted = false)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            if (Get(id, deleted) is null) return null;
            command.CommandText = "SELECT file FROM nest_revisions WHERE id=$id AND revision=$revision";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$revision", revision);
            return command.ExecuteScalar() as byte[];
        }
    }

    private static bool SameMetadata(NestRecord a, NestRecord b) =>
        a.Name == b.Name && a.Customer == b.Customer && a.DateCreated == b.DateCreated &&
        a.DateModified == b.DateModified && a.Material == b.Material && a.Thickness == b.Thickness &&
        a.Status == b.Status && a.PlateCount == b.PlateCount && a.PartCount == b.PartCount &&
        a.Comments == b.Comments && a.MadeBy == b.MadeBy;

    private void Snapshot(Guid id, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO nest_revisions ({RecordColumns}, file) SELECT {RecordColumns}, file FROM nests WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private void Migrate()
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version";
            var version = (long)command.ExecuteScalar()!;
            if (version > 7) throw new InvalidOperationException("Database schema is newer than this server.");
            if (version == 0)
            {
                command.CommandText = """
                    ALTER TABLE nests ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
                    CREATE TABLE nest_revisions AS SELECT * FROM nests WHERE 0;
                    CREATE UNIQUE INDEX ix_nest_revisions_key ON nest_revisions(id, revision);
                    INSERT INTO nest_revisions SELECT * FROM nests;
                    PRAGMA user_version=1;
                    """;
                command.ExecuteNonQuery();
            }
            if (version < 2)
            {
                command.CommandText = """
                    ALTER TABLE nests ADD COLUMN deleted INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE nest_revisions ADD COLUMN deleted INTEGER NOT NULL DEFAULT 0;
                    PRAGMA user_version=2;
                    """;
                command.ExecuteNonQuery();
            }
            if (version < 3)
            {
                command.CommandText = """
                    ALTER TABLE nests ADD COLUMN restoredFromRevision INTEGER;
                    ALTER TABLE nest_revisions ADD COLUMN restoredFromRevision INTEGER;
                    PRAGMA user_version=3;
                    """;
                command.ExecuteNonQuery();
            }
            if (version < 4)
            {
                // No drawing tables yet: create the current (v5) layout directly.
                command.CommandText = DrawingLibrarySchema;
                command.ExecuteNonQuery();
            }
            else if (version == 4)
            {
                // Packages become content-addressed; the nest tables are untouched.
                command.CommandText = DrawingLibraryUpgradeFromV4;
                command.ExecuteNonQuery();
            }
            if (version < 6)
            {
                command.CommandText = NestSequenceSchema;
                command.ExecuteNonQuery();
            }
            if (version < 7)
            {
                command.CommandText = DrawingSetSchema;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    /// <summary>Checks the live connection without exposing storage error details.</summary>
    public bool IsHealthy()
    {
        lock (_sync)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT 1";
                return command.ExecuteScalar() is long value && value == 1;
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
            _connection.Dispose();
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private const string RecordColumns = """
        id, name, customer, dateCreated, dateModified, material, thickness,
        status, plateCount, partCount, comments, madeBy, fileSize, savedAt, revision, deleted, restoredFromRevision
        """;

    // Case-insensitive (ASCII) substring over the text columns and the status's stored
    // and display names. The search text is always a bound, escaped LIKE parameter.
    private const string SearchPredicate = """
        name LIKE $pattern ESCAPE '\'
            OR customer LIKE $pattern ESCAPE '\'
            OR material LIKE $pattern ESCAPE '\'
            OR madeBy LIKE $pattern ESCAPE '\'
            OR comments LIKE $pattern ESCAPE '\'
            OR status LIKE $pattern ESCAPE '\'
            OR (CASE status WHEN 'ToBeCut' THEN 'To Be Cut' WHEN 'HasBeenCut' THEN 'Has Been Cut'
                ELSE status END) LIKE $pattern ESCAPE '\'
        """;

    // Fixed allowlist: request text never reaches the ORDER BY clause.
    private static string SortColumn(NestSortField field) => field switch
    {
        NestSortField.SavedAt => "savedAt",
        NestSortField.Name => "name COLLATE NOCASE",
        NestSortField.Customer => "customer COLLATE NOCASE",
        NestSortField.Status => "status COLLATE NOCASE",
        NestSortField.Material => "material COLLATE NOCASE",
        NestSortField.DateCreated => "dateCreated",
        NestSortField.DateModified => "dateModified",
        NestSortField.Thickness => "thickness",
        NestSortField.PlateCount => "plateCount",
        NestSortField.PartCount => "partCount",
        NestSortField.MadeBy => "madeBy COLLATE NOCASE",
        NestSortField.Comments => "comments COLLATE NOCASE",
        NestSortField.FileSize => "fileSize",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported sort column."),
    };

    private static void AddSearchParameter(SqliteCommand command, string search)
    {
        if (search.Length == 0)
            return;

        var escaped = search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        command.Parameters.AddWithValue("$pattern", "%" + escaped + "%");
    }

    private static void AddRecordParameters(
        SqliteCommand command, Guid id, NestRecord record, byte[]? file, bool updateFile)
    {
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$customer", record.Customer);
        command.Parameters.AddWithValue("$dateCreated", record.DateCreated.ToString("o"));
        command.Parameters.AddWithValue("$dateModified", record.DateModified.ToString("o"));
        command.Parameters.AddWithValue("$material", record.Material);
        command.Parameters.AddWithValue("$thickness", record.Thickness);
        command.Parameters.AddWithValue("$status", record.Status.ToString());
        command.Parameters.AddWithValue("$plateCount", record.PlateCount);
        command.Parameters.AddWithValue("$partCount", record.PartCount);
        command.Parameters.AddWithValue("$comments", record.Comments);
        command.Parameters.AddWithValue("$madeBy", record.MadeBy);
        command.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("o"));
        if (updateFile)
        {
            command.Parameters.AddWithValue("$file", file!);
            command.Parameters.AddWithValue("$fileSize", file!.LongLength);
        }
    }

    // Reads RecordColumns in order from column `first` on; a join may put other columns before them.
    private static NestRecord ReadRecord(SqliteDataReader reader, int first = 0)
    {
        return new NestRecord
        {
            Id = Guid.Parse(reader.GetString(first)),
            Name = reader.GetString(first + 1),
            Customer = reader.GetString(first + 2),
            DateCreated = DateTime.Parse(reader.GetString(first + 3)),
            DateModified = DateTime.Parse(reader.GetString(first + 4)),
            Material = reader.GetString(first + 5),
            Thickness = reader.GetDouble(first + 6),
            Status = Enum.TryParse<NestStatus>(reader.GetString(first + 7), true, out var s)
                ? s : NestStatus.Quote,
            PlateCount = (int)reader.GetInt64(first + 8),
            PartCount = (int)reader.GetInt64(first + 9),
            Comments = reader.GetString(first + 10),
            MadeBy = reader.GetString(first + 11),
            FileSize = reader.GetInt64(first + 12),
            SavedAt = DateTime.Parse(reader.GetString(first + 13)),
            Revision = reader.GetInt32(first + 14),
            Deleted = reader.GetInt32(first + 15) != 0,
            RestoredFromRevision = reader.IsDBNull(first + 16) ? null : reader.GetInt32(first + 16),
        };
    }
}

public sealed class RevisionConflictException(NestRecord current, string message = "The nest has changed. Reload or Save As.")
    : Exception(message)
{
    public NestRecord Current { get; } = current;
}
