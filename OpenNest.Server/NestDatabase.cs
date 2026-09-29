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
        using var command = _connection.CreateCommand();
        command.CommandText =
            $"SELECT {RecordColumns} FROM nests ORDER BY savedAt DESC";
        var records = new List<NestRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            records.Add(ReadRecord(reader));
        return records;
    }

    public NestRecord? Get(Guid id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT {RecordColumns} FROM nests WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    public byte[]? GetFile(Guid id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT file FROM nests WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        var value = command.ExecuteScalar();
        return value is byte[] bytes ? bytes : null;
    }

    public NestRecord Insert(Guid id, NestRecord record, byte[] file)
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

    public NestRecord? Update(Guid id, NestRecord record, byte[]? file)
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

    public bool Delete(Guid id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM nests WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    public void Dispose() => _connection.Dispose();

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
