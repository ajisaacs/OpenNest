using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OpenNest;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Drawing library storage. <c>drawing_packages</c> stores each distinct package once, keyed
/// by its SHA-256; <c>drawing_revisions</c> holds every immutable revision (details, nesting
/// settings, archived flag and the package hash it points at); <c>drawings</c> holds each
/// drawing's current revision for browsing. All share the nest database file and monitor.
/// Every change appends a revision in one transaction, and only a new package adds a blob.
/// </summary>
public sealed partial class NestDatabase
{
    private const string DrawingColumnDefinitions = """
            id                 TEXT NOT NULL,
            revision           INTEGER NOT NULL,
            name               TEXT NOT NULL,
            customer           TEXT NOT NULL DEFAULT '',
            nameKey            TEXT NOT NULL,
            customerKey        TEXT NOT NULL DEFAULT '',
            materialName       TEXT NOT NULL DEFAULT '',
            materialKey        TEXT NOT NULL DEFAULT '',
            materialGrade      TEXT NOT NULL DEFAULT '',
            gradeKey           TEXT NOT NULL DEFAULT '',
            thickness          REAL NOT NULL,
            units              TEXT NOT NULL,
            cutType            TEXT NOT NULL,
            notes              TEXT NOT NULL DEFAULT '',
            cutFirst           INTEGER NOT NULL DEFAULT 0,
            priority           INTEGER NOT NULL DEFAULT 0,
            stepAngle          REAL NOT NULL DEFAULT 0,
            startAngle         REAL NOT NULL DEFAULT 0,
            endAngle           REAL NOT NULL DEFAULT 0,
            allow180Equivalent INTEGER NOT NULL DEFAULT 0,
            colorArgb          INTEGER NOT NULL DEFAULT 0,
            archived           INTEGER NOT NULL DEFAULT 0,
            packageSha256      TEXT NOT NULL REFERENCES drawing_packages(sha256),
            packageSize        INTEGER NOT NULL,
            savedAt            TEXT NOT NULL,
            savedBy            TEXT NOT NULL DEFAULT ''
        """;

    // The v5 drawing tables: created directly on a pre-v4 database and by the v4 upgrade.
    private const string DrawingLibraryTables = $"""
        CREATE TABLE drawing_packages (
            sha256  TEXT NOT NULL PRIMARY KEY,
            size    INTEGER NOT NULL,
            package BLOB NOT NULL);
        CREATE TABLE drawing_revisions ({DrawingColumnDefinitions}, PRIMARY KEY (id, revision));
        CREATE TABLE drawings ({DrawingColumnDefinitions}, PRIMARY KEY (id));
        CREATE INDEX ix_drawings_customer_name ON drawings(customerKey, nameKey);
        CREATE INDEX ix_drawings_material ON drawings(materialKey);
        CREATE INDEX ix_drawings_thickness ON drawings(thickness);
        CREATE INDEX ix_drawings_cutFirst ON drawings(cutFirst);
        CREATE INDEX ix_drawings_savedAt ON drawings(savedAt DESC);
        """;

    private const string DrawingLibrarySchema = DrawingLibraryTables + "PRAGMA user_version=5;";

    // v4 kept one column set in both tables plus a package blob copied into every
    // revision. The upgrade stores each distinct blob once and copies every revision
    // and current row as it was; nesting settings take their defaults (v4 had none).
    private const string V4CopiedColumns = """
        id, revision, name, customer, nameKey, customerKey, materialName, materialKey,
        materialGrade, gradeKey, thickness, units, cutType, notes, archived,
        packageSha256, packageSize, savedAt, savedBy
        """;

    private const string DrawingLibraryUpgradeFromV4 = $"""
        DROP INDEX ix_drawing_revisions_key;
        DROP INDEX ix_drawings_customer_name;
        DROP INDEX ix_drawings_material;
        DROP INDEX ix_drawings_thickness;
        DROP INDEX ix_drawings_savedAt;
        ALTER TABLE drawings RENAME TO drawings_v4;
        ALTER TABLE drawing_revisions RENAME TO drawing_revisions_v4;
        {DrawingLibraryTables}
        INSERT OR IGNORE INTO drawing_packages (sha256, size, package)
            SELECT packageSha256, packageSize, package FROM drawing_revisions_v4;
        INSERT INTO drawing_revisions ({V4CopiedColumns}) SELECT {V4CopiedColumns} FROM drawing_revisions_v4;
        INSERT INTO drawings ({V4CopiedColumns}) SELECT {V4CopiedColumns} FROM drawings_v4;
        DROP TABLE drawings_v4;
        DROP TABLE drawing_revisions_v4;
        PRAGMA user_version=5;
        """;

    private const string DrawingColumns = """
        id, revision, name, customer, materialName, materialGrade, thickness, units,
        cutType, notes, archived, packageSha256, packageSize, savedAt, savedBy,
        cutFirst, priority, stepAngle, startAngle, endAngle, allow180Equivalent, colorArgb
        """;

    private const string DrawingStoredColumns = DrawingColumns + ", nameKey, customerKey, materialKey, gradeKey";

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
            command.CommandText = """
                SELECT p.package FROM drawings d JOIN drawing_packages p ON p.sha256 = d.packageSha256
                WHERE d.id=$id
                """;
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
        if (query.CutFirst is not null) conditions.Add("cutFirst=$cutFirst");
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
            var sha = StorePackage(package, transaction);
            WriteDrawingRevision(id, record, sha, package.LongLength, 1, archived: false, transaction);
            transaction.Commit();
            return GetDrawing(id) ?? throw new InvalidOperationException("Insert did not persist.");
        }
    }

    /// <summary>
    /// Appends a revision with new details and settings and, when <paramref name="package"/>
    /// is not null, new geometry; otherwise the revision points at the current package and
    /// stores no blob. Returns null for an unknown id; an identical save is a no-op.
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
            var error = DrawingLibraryRules.GetMetadataError(record)
                ?? (package is null ? null : DrawingLibraryRules.GetPackageError(package));
            if (error is not null)
                throw new ArgumentException(error, nameof(record));

            var sha = package is null ? current.PackageSha256 : PackageSha256(package);
            var size = package?.LongLength ?? current.PackageSize;
            if (SameDrawingMetadata(current, record) && sha == current.PackageSha256)
                return current;

            if (package is not null)
                StorePackage(package, transaction);
            WriteDrawingRevision(id, record, sha, size, checked(current.Revision + 1), archived: false, transaction);
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

            WriteDrawingRevision(id, current, current.PackageSha256, current.PackageSize,
                checked(current.Revision + 1), archived, transaction);
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
            command.CommandText = """
                SELECT p.package FROM drawing_revisions r JOIN drawing_packages p ON p.sha256 = r.packageSha256
                WHERE r.id=$id AND r.revision=$revision
                """;
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

    private static string PackageSha256(byte[] package) =>
        Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();

    private static bool SameDrawingMetadata(DrawingLibraryRecord a, DrawingLibraryRecord b) =>
        a.Name == b.Name && a.Customer == b.Customer && a.MaterialName == b.MaterialName &&
        a.MaterialGrade == b.MaterialGrade && a.Thickness == DrawingLibraryRules.NormalizeThickness(b.Thickness) &&
        a.Units == b.Units && a.CutType == b.CutType && a.Notes == b.Notes && Equals(a.Settings, b.Settings);

    // Content-addressed: an identical package already stored is reused, never rewritten.
    private string StorePackage(byte[] package, SqliteTransaction transaction)
    {
        var sha = PackageSha256(package);
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO drawing_packages (sha256, size, package) VALUES ($sha, $size, $package)";
        command.Parameters.AddWithValue("$sha", sha);
        command.Parameters.AddWithValue("$size", package.LongLength);
        command.Parameters.AddWithValue("$package", package);
        command.ExecuteNonQuery();
        return sha;
    }

    // Called only under _sync with the caller's transaction; promotion and snapshot are one
    // write. The package must already be stored under packageSha256.
    private void WriteDrawingRevision(Guid id, DrawingLibraryRecord record, string packageSha256, long packageSize,
        int revision, bool archived, SqliteTransaction transaction)
    {
        var settings = record.Settings ?? new DrawingNestingSettings();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT OR REPLACE INTO drawings ({DrawingStoredColumns})
            VALUES ($id, $revision, $name, $customer, $materialName, $materialGrade, $thickness, $units,
                $cutType, $notes, $archived, $sha, $size, $savedAt, $savedBy,
                $cutFirst, $priority, $stepAngle, $startAngle, $endAngle, $allow180, $color,
                $nameKey, $customerKey, $materialKey, $gradeKey)
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
        command.Parameters.AddWithValue("$sha", packageSha256);
        command.Parameters.AddWithValue("$size", packageSize);
        command.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$savedBy", record.SavedBy);
        command.Parameters.AddWithValue("$cutFirst", settings.CutFirst ? 1 : 0);
        command.Parameters.AddWithValue("$priority", settings.Priority);
        command.Parameters.AddWithValue("$stepAngle", settings.StepAngle);
        command.Parameters.AddWithValue("$startAngle", settings.StartAngle);
        command.Parameters.AddWithValue("$endAngle", settings.EndAngle);
        command.Parameters.AddWithValue("$allow180", settings.Allow180Equivalent ? 1 : 0);
        command.Parameters.AddWithValue("$color", settings.ColorArgb);
        command.Parameters.AddWithValue("$nameKey", SharedListNames.Key(record.Name));
        command.Parameters.AddWithValue("$customerKey", SharedListNames.Key(record.Customer));
        command.Parameters.AddWithValue("$materialKey", SharedListNames.Key(record.MaterialName));
        command.Parameters.AddWithValue("$gradeKey", SharedListNames.Key(record.MaterialGrade));
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
        if (query.CutFirst is { } cutFirst)
            command.Parameters.AddWithValue("$cutFirst", cutFirst ? 1 : 0);
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

    // Reads DrawingColumns in order from column `first` on; a join may put other columns before them.
    private static DrawingLibraryRecord ReadDrawing(SqliteDataReader reader, int first = 0) => new()
    {
        Id = Guid.Parse(reader.GetString(first)),
        Revision = reader.GetInt32(first + 1),
        Name = reader.GetString(first + 2),
        Customer = reader.GetString(first + 3),
        MaterialName = reader.GetString(first + 4),
        MaterialGrade = reader.GetString(first + 5),
        Thickness = reader.GetDouble(first + 6),
        Units = Enum.Parse<Units>(reader.GetString(first + 7)),
        CutType = Enum.Parse<CutType>(reader.GetString(first + 8)),
        Notes = reader.GetString(first + 9),
        Archived = reader.GetInt32(first + 10) != 0,
        PackageSha256 = reader.GetString(first + 11),
        PackageSize = reader.GetInt64(first + 12),
        SavedAt = DateTime.Parse(reader.GetString(first + 13)),
        SavedBy = reader.GetString(first + 14),
        Settings = new DrawingNestingSettings
        {
            CutFirst = reader.GetInt32(first + 15) != 0,
            Priority = reader.GetInt32(first + 16),
            StepAngle = reader.GetDouble(first + 17),
            StartAngle = reader.GetDouble(first + 18),
            EndAngle = reader.GetDouble(first + 19),
            Allow180Equivalent = reader.GetInt32(first + 20) != 0,
            ColorArgb = reader.GetInt32(first + 21),
        },
    };
}

/// <summary>A guarded drawing write found a different current state; nothing was changed.</summary>
public sealed class DrawingConflictException(DrawingLibraryRecord current, string message) : Exception(message)
{
    public DrawingLibraryRecord Current { get; } = current;
}
