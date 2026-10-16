using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OpenNest;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Drawing library storage: <c>drawings</c> holds each drawing's current revision and
/// <c>drawing_revisions</c> every immutable revision, both in the nest database file
/// and under the same monitor. Every change appends a revision in one transaction.
/// </summary>
public sealed partial class NestDatabase
{
    // Schema v4 is additive: the nest tables are untouched.
    private const string DrawingColumnDefinitions = """
            id            TEXT NOT NULL,
            revision      INTEGER NOT NULL,
            name          TEXT NOT NULL,
            customer      TEXT NOT NULL DEFAULT '',
            nameKey       TEXT NOT NULL,
            customerKey   TEXT NOT NULL DEFAULT '',
            materialName  TEXT NOT NULL DEFAULT '',
            materialKey   TEXT NOT NULL DEFAULT '',
            materialGrade TEXT NOT NULL DEFAULT '',
            gradeKey      TEXT NOT NULL DEFAULT '',
            thickness     REAL NOT NULL,
            units         TEXT NOT NULL,
            cutType       TEXT NOT NULL,
            notes         TEXT NOT NULL DEFAULT '',
            archived      INTEGER NOT NULL DEFAULT 0,
            packageSha256 TEXT NOT NULL,
            packageSize   INTEGER NOT NULL,
            savedAt       TEXT NOT NULL,
            savedBy       TEXT NOT NULL DEFAULT '',
            package       BLOB NOT NULL
        """;

    private const string DrawingLibrarySchema = $"""
        CREATE TABLE drawings ({DrawingColumnDefinitions}, PRIMARY KEY (id));
        CREATE TABLE drawing_revisions ({DrawingColumnDefinitions});
        CREATE UNIQUE INDEX ix_drawing_revisions_key ON drawing_revisions(id, revision);
        CREATE INDEX ix_drawings_customer_name ON drawings(customerKey, nameKey);
        CREATE INDEX ix_drawings_material ON drawings(materialKey);
        CREATE INDEX ix_drawings_thickness ON drawings(thickness);
        CREATE INDEX ix_drawings_savedAt ON drawings(savedAt DESC);
        PRAGMA user_version=4;
        """;

    private const string DrawingColumns = """
        id, revision, name, customer, materialName, materialGrade, thickness, units,
        cutType, notes, archived, packageSha256, packageSize, savedAt, savedBy
        """;

    private const string DrawingStoredColumns = DrawingColumns + ", nameKey, customerKey, materialKey, gradeKey, package";

    // Case-insensitive (ASCII) substring; the text is always a bound, escaped LIKE parameter.
    private const string DrawingSearchPredicate = """
        name LIKE $pattern ESCAPE '\' OR customer LIKE $pattern ESCAPE '\' OR notes LIKE $pattern ESCAPE '\'
        """;

    public DrawingLibraryRecord? GetDrawing(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {DrawingColumns} FROM drawings WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadDrawing(reader) : null;
        }
    }

    public byte[]? GetDrawingPackage(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT package FROM drawings WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() as byte[];
        }
    }

    /// <summary>One bounded page of matching drawings plus the total, read in one monitor hold.</summary>
    public DrawingLibraryPage QueryDrawings(DrawingLibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        var conditions = new List<string>();
        if (!query.IncludeArchived) conditions.Add("archived=0");
        if (query.NormalizedSearch.Length > 0) conditions.Add("(" + DrawingSearchPredicate + ")");
        if (SharedListNames.Key(query.Customer).Length > 0) conditions.Add("customerKey=$customer");
        if (SharedListNames.Key(query.MaterialName).Length > 0) conditions.Add("materialKey=$material");
        if (SharedListNames.Key(query.MaterialGrade).Length > 0) conditions.Add("gradeKey=$grade");
        if (query.Thickness is not null) conditions.Add("thickness=$thickness");
        if (query.CutType is not null) conditions.Add("cutType=$cutType");
        var where = conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);

        lock (_sync)
        {
            using var count = _connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM drawings {where}";
            AddDrawingFilterParameters(count, query);
            var total = checked((int)(long)count.ExecuteScalar()!);

            using var command = _connection.CreateCommand();
            var direction = query.Descending ? "DESC" : "ASC";
            command.CommandText = $"""
                SELECT {DrawingColumns} FROM drawings {where}
                ORDER BY {DrawingSortColumn(query.Sort)} {direction}, id {direction}
                LIMIT $limit OFFSET $offset
                """;
            AddDrawingFilterParameters(command, query);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var records = new List<DrawingLibraryRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(ReadDrawing(reader));
            return new DrawingLibraryPage { Items = records, Total = total, Offset = query.Offset, Limit = query.Limit };
        }
    }

    /// <summary>Stores revision 1 under a client-allocated id; an existing id is a conflict.</summary>
    public DrawingLibraryRecord InsertDrawing(Guid id, DrawingLibraryRecord record, byte[] package)
    {
        if (id == Guid.Empty) throw new ArgumentException("A drawing id is required.", nameof(id));
        ValidateDrawing(record, package);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (GetDrawing(id) is { } existing)
                throw new DrawingConflictException(existing, "A library drawing with this id already exists.");
            WriteDrawingRevision(id, record, package, 1, archived: false, insert: true, transaction);
            transaction.Commit();
            return GetDrawing(id) ?? throw new InvalidOperationException("Insert did not persist.");
        }
    }

    /// <summary>
    /// Appends a revision with new metadata and, when <paramref name="package"/> is not null,
    /// new geometry. Returns null for an unknown id; an identical save is a no-op.
    /// </summary>
    public DrawingLibraryRecord? UpdateDrawing(Guid id, DrawingLibraryRecord record, byte[]? package, int expectedRevision)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = GetDrawing(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision)
                throw new DrawingConflictException(current, "The library drawing has changed. Reload before saving.");
            if (current.Archived)
                throw new DrawingConflictException(current, "The library drawing is archived. Restore it before saving.");
            package ??= GetDrawingPackage(id)!;
            ValidateDrawing(record, package);
            if (SameDrawingMetadata(current, record) && GetDrawingPackage(id)!.AsSpan().SequenceEqual(package))
                return current;

            WriteDrawingRevision(id, record, package, checked(current.Revision + 1), archived: false, insert: false, transaction);
            transaction.Commit();
            return GetDrawing(id);
        }
    }

    /// <summary>Archives or restores a drawing as a new revision. Returns null for an unknown id.</summary>
    public DrawingLibraryRecord? SetDrawingArchived(Guid id, bool archived, int expectedRevision)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = GetDrawing(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision)
                throw new DrawingConflictException(current, "The library drawing has changed. Reload and try again.");
            if (current.Archived == archived)
                throw new DrawingConflictException(current,
                    archived ? "The library drawing is already archived." : "The library drawing is not archived.");

            WriteDrawingRevision(id, current, GetDrawingPackage(id)!, checked(current.Revision + 1), archived, insert: false, transaction);
            transaction.Commit();
            return GetDrawing(id);
        }
    }

    public DrawingLibraryPage? DrawingHistory(Guid id, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_sync)
        {
            if (GetDrawing(id) is null) return null;
            using var count = _connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM drawing_revisions WHERE id=$id";
            count.Parameters.AddWithValue("$id", id.ToString());
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {DrawingColumns} FROM drawing_revisions WHERE id=$id ORDER BY revision DESC LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var records = new List<DrawingLibraryRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) records.Add(ReadDrawing(reader));
            return new DrawingLibraryPage { Items = records, Total = total, Offset = offset, Limit = limit };
        }
    }

    public byte[]? GetDrawingRevisionPackage(Guid id, int revision)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT package FROM drawing_revisions WHERE id=$id AND revision=$revision";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$revision", revision);
            return command.ExecuteScalar() as byte[];
        }
    }

    private static void ValidateDrawing(DrawingLibraryRecord record, byte[] package)
    {
        var error = DrawingLibraryRules.GetMetadataError(record) ?? DrawingLibraryRules.GetPackageError(package);
        if (error is not null)
            throw new ArgumentException(error, nameof(record));
    }

    private static bool SameDrawingMetadata(DrawingLibraryRecord a, DrawingLibraryRecord b) =>
        a.Name == b.Name && a.Customer == b.Customer && a.MaterialName == b.MaterialName &&
        a.MaterialGrade == b.MaterialGrade && a.Thickness == DrawingLibraryRules.NormalizeThickness(b.Thickness) &&
        a.Units == b.Units && a.CutType == b.CutType && a.Notes == b.Notes;

    // Called only under _sync with the caller's transaction; promotion and snapshot are one write.
    private void WriteDrawingRevision(Guid id, DrawingLibraryRecord record, byte[] package, int revision,
        bool archived, bool insert, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert
            ? $"""
                INSERT INTO drawings ({DrawingStoredColumns})
                VALUES ($id, $revision, $name, $customer, $materialName, $materialGrade, $thickness, $units,
                    $cutType, $notes, $archived, $sha, $size, $savedAt, $savedBy,
                    $nameKey, $customerKey, $materialKey, $gradeKey, $package)
                """
            : """
                UPDATE drawings SET revision=$revision, name=$name, customer=$customer,
                    materialName=$materialName, materialGrade=$materialGrade, thickness=$thickness,
                    units=$units, cutType=$cutType, notes=$notes, archived=$archived, packageSha256=$sha,
                    packageSize=$size, savedAt=$savedAt, savedBy=$savedBy, nameKey=$nameKey,
                    customerKey=$customerKey, materialKey=$materialKey, gradeKey=$gradeKey, package=$package
                WHERE id=$id
                """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$customer", record.Customer);
        command.Parameters.AddWithValue("$materialName", record.MaterialName);
        command.Parameters.AddWithValue("$materialGrade", record.MaterialGrade);
        command.Parameters.AddWithValue("$thickness", DrawingLibraryRules.NormalizeThickness(record.Thickness));
        command.Parameters.AddWithValue("$units", record.Units.ToString());
        command.Parameters.AddWithValue("$cutType", record.CutType.ToString());
        command.Parameters.AddWithValue("$notes", record.Notes);
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        command.Parameters.AddWithValue("$sha", Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant());
        command.Parameters.AddWithValue("$size", package.LongLength);
        command.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$savedBy", record.SavedBy);
        command.Parameters.AddWithValue("$nameKey", SharedListNames.Key(record.Name));
        command.Parameters.AddWithValue("$customerKey", SharedListNames.Key(record.Customer));
        command.Parameters.AddWithValue("$materialKey", SharedListNames.Key(record.MaterialName));
        command.Parameters.AddWithValue("$gradeKey", SharedListNames.Key(record.MaterialGrade));
        command.Parameters.AddWithValue("$package", package);
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = $"INSERT INTO drawing_revisions ({DrawingStoredColumns}) SELECT {DrawingStoredColumns} FROM drawings WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private static void AddDrawingFilterParameters(SqliteCommand command, DrawingLibraryQuery query)
    {
        AddSearchParameter(command, query.NormalizedSearch);
        if (SharedListNames.Key(query.Customer).Length > 0)
            command.Parameters.AddWithValue("$customer", SharedListNames.Key(query.Customer));
        if (SharedListNames.Key(query.MaterialName).Length > 0)
            command.Parameters.AddWithValue("$material", SharedListNames.Key(query.MaterialName));
        if (SharedListNames.Key(query.MaterialGrade).Length > 0)
            command.Parameters.AddWithValue("$grade", SharedListNames.Key(query.MaterialGrade));
        if (query.Thickness is { } thickness)
            command.Parameters.AddWithValue("$thickness", DrawingLibraryRules.NormalizeThickness(thickness));
        if (query.CutType is { } cutType)
            command.Parameters.AddWithValue("$cutType", cutType.ToString());
    }

    // Fixed allowlist: request text never reaches the ORDER BY clause.
    private static string DrawingSortColumn(DrawingSortField field) => field switch
    {
        DrawingSortField.SavedAt => "savedAt",
        DrawingSortField.Name => "name COLLATE NOCASE",
        DrawingSortField.Customer => "customer COLLATE NOCASE",
        DrawingSortField.MaterialName => "materialName COLLATE NOCASE",
        DrawingSortField.MaterialGrade => "materialGrade COLLATE NOCASE",
        DrawingSortField.Thickness => "thickness",
        DrawingSortField.CutType => "cutType",
        DrawingSortField.Revision => "revision",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported sort column."),
    };

    private static DrawingLibraryRecord ReadDrawing(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Revision = reader.GetInt32(1),
        Name = reader.GetString(2),
        Customer = reader.GetString(3),
        MaterialName = reader.GetString(4),
        MaterialGrade = reader.GetString(5),
        Thickness = reader.GetDouble(6),
        Units = Enum.Parse<Units>(reader.GetString(7)),
        CutType = Enum.Parse<CutType>(reader.GetString(8)),
        Notes = reader.GetString(9),
        Archived = reader.GetInt32(10) != 0,
        PackageSha256 = reader.GetString(11),
        PackageSize = reader.GetInt64(12),
        SavedAt = DateTime.Parse(reader.GetString(13)),
        SavedBy = reader.GetString(14),
    };
}

/// <summary>A guarded drawing write found a different current state; nothing was changed.</summary>
public sealed class DrawingConflictException(DrawingLibraryRecord current, string message) : Exception(message)
{
    public DrawingLibraryRecord Current { get; } = current;
}
