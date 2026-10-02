using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// SQLite-backed nest storage: metadata columns plus the .nest archive as a BLOB.
/// The database is created on first use. Records are keyed by client-generated ids
/// so a PC that loses its local id mapping can still re-upload under a new id;
/// updates keep the id.
/// </summary>
public sealed class NestDatabase : IDisposable
{
    // One reentrant monitor owns each complete connection operation, including
    // readers, nested Get readbacks, health checks, and disposal.
    private readonly object _sync = new();
    private readonly SqliteConnection _connection;

    public NestDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
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
    }

    public IReadOnlyList<NestRecord> List()
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                $"SELECT {RecordColumns} FROM nests ORDER BY savedAt DESC";
            var records = new List<NestRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(ReadRecord(reader));
            return records;
        }
    }

    public NestRecord? Get(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {RecordColumns} FROM nests WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }
    }

    /// <summary>
    /// One bounded page of matching metadata plus the total match count. The count and
    /// the page are read in the same monitor hold, so they describe one state.
    /// </summary>
    public NestPage Query(NestQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        var search = query.NormalizedSearch;
        var where = search.Length == 0 ? "" : SearchPredicate;
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

    public byte[]? GetFile(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT file FROM nests WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            var value = command.ExecuteScalar();
            return value is byte[] bytes ? bytes : null;
        }
    }

    public NestRecord Insert(Guid id, NestRecord record, byte[] file)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
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
            return Get(id) ?? throw new InvalidOperationException("Insert did not persist.");
        }
    }

    public NestRecord? Update(Guid id, NestRecord record, byte[]? file)
    {
        lock (_sync)
        {
            if (Get(id) is null)
                return null;

            using var command = _connection.CreateCommand();
            var fileClause = file is null ? "" : ", file = $file, fileSize = $fileSize";
            command.CommandText = $"""
                UPDATE nests SET
                    name = $name, customer = $customer, dateCreated = $dateCreated,
                    dateModified = $dateModified, material = $material,
                    thickness = $thickness, status = $status, plateCount = $plateCount,
                    partCount = $partCount, comments = $comments, madeBy = $madeBy,
                    savedAt = $savedAt{fileClause}
                WHERE id = $id
                """;
            AddRecordParameters(command, id, record, file, updateFile: file is not null);
            command.ExecuteNonQuery();
            return Get(id);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM nests WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteNonQuery() > 0;
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
        status, plateCount, partCount, comments, madeBy, fileSize, savedAt
        """;

    // Case-insensitive (ASCII) substring over the text columns and the status's stored
    // and display names. The search text is always a bound, escaped LIKE parameter.
    private const string SearchPredicate = """
        WHERE name LIKE $pattern ESCAPE '\'
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

    private static NestRecord ReadRecord(SqliteDataReader reader)
    {
        return new NestRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            Customer = reader.GetString(2),
            DateCreated = DateTime.Parse(reader.GetString(3)),
            DateModified = DateTime.Parse(reader.GetString(4)),
            Material = reader.GetString(5),
            Thickness = reader.GetDouble(6),
            Status = Enum.TryParse<NestStatus>(reader.GetString(7), true, out var s)
                ? s : NestStatus.Quote,
            PlateCount = (int)reader.GetInt64(8),
            PartCount = (int)reader.GetInt64(9),
            Comments = reader.GetString(10),
            MadeBy = reader.GetString(11),
            FileSize = reader.GetInt64(12),
            SavedAt = DateTime.Parse(reader.GetString(13)),
        };
    }
}
