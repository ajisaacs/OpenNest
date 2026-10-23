using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Drawing set storage. <c>drawing_set_revisions</c> holds every immutable revision's details
/// and archived flag, <c>drawing_set_members</c> every revision's ordered members, and
/// <c>drawing_sets</c> each set's current revision for browsing. Members reference library
/// drawings by id and are read joined to each drawing's current record. Every change appends
/// a revision, with its members, in one transaction.
/// </summary>
public sealed partial class NestDatabase
{
    private const string SetColumnDefinitions = """
            id          TEXT NOT NULL,
            revision    INTEGER NOT NULL,
            name        TEXT NOT NULL,
            nameKey     TEXT NOT NULL,
            customer    TEXT NOT NULL DEFAULT '',
            customerKey TEXT NOT NULL DEFAULT '',
            notes       TEXT NOT NULL DEFAULT '',
            archived    INTEGER NOT NULL DEFAULT 0,
            savedAt     TEXT NOT NULL,
            savedBy     TEXT NOT NULL DEFAULT ''
        """;

    private const string DrawingSetSchema = $"""
        CREATE TABLE drawing_set_revisions ({SetColumnDefinitions}, PRIMARY KEY (id, revision));
        CREATE TABLE drawing_sets ({SetColumnDefinitions}, PRIMARY KEY (id));
        CREATE TABLE drawing_set_members (
            setId     TEXT NOT NULL,
            revision  INTEGER NOT NULL,
            position  INTEGER NOT NULL,
            drawingId TEXT NOT NULL REFERENCES drawings(id),
            quantity  INTEGER NOT NULL,
            PRIMARY KEY (setId, revision, position),
            UNIQUE (setId, revision, drawingId));
        CREATE INDEX ix_drawing_sets_name ON drawing_sets(customerKey, nameKey);
        CREATE INDEX ix_drawing_sets_savedAt ON drawing_sets(savedAt DESC);
        CREATE INDEX ix_drawing_set_members_drawing ON drawing_set_members(drawingId);
        {SetReleaseTablesV7}
        PRAGMA user_version=7;
        """;

    private const string SetColumns = "s.id, s.revision, s.name, s.customer, s.notes, s.archived, s.savedAt, s.savedBy";

    private const string SetStoredColumns = "id, revision, name, customer, notes, archived, savedAt, savedBy, nameKey, customerKey";

    // Read after SetColumns: the member count of the row's own revision.
    private const string SetMemberCount =
        "(SELECT COUNT(*) FROM drawing_set_members m WHERE m.setId = s.id AND m.revision = s.revision)";

    private const string SetSearchPredicate = """
        s.name LIKE $pattern ESCAPE '\' OR s.customer LIKE $pattern ESCAPE '\' OR s.notes LIKE $pattern ESCAPE '\'
        """;

    private static readonly string JoinedDrawingColumns =
        string.Join(", ", DrawingColumns.Split(',').Select(column => "d." + column.Trim()));

    /// <summary>The current revision with its members, or null for an unknown id.</summary>
    public DrawingSetRecord? GetDrawingSet(Guid id)
    {
        lock (_sync)
        {
            var record = ReadSet($"SELECT {SetColumns}, {SetMemberCount} FROM drawing_sets s WHERE s.id=$id", id, null);
            return record is null ? null : WithMembers(record);
        }
    }

    /// <summary>One immutable revision with its members, or null when the set or revision is unknown.</summary>
    public DrawingSetRecord? GetDrawingSetRevision(Guid id, int revision)
    {
        lock (_sync)
        {
            var record = ReadSet(
                $"SELECT {SetColumns}, {SetMemberCount} FROM drawing_set_revisions s WHERE s.id=$id AND s.revision=$revision",
                id, revision);
            return record is null ? null : WithMembers(record);
        }
    }

    /// <summary>One bounded page of matching sets (without members) plus the total, read in one monitor hold.</summary>
    public DrawingSetPage QueryDrawingSets(DrawingSetQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        var conditions = new List<string>();
        if (!query.IncludeArchived) conditions.Add("s.archived=0");
        if (query.NormalizedSearch.Length > 0) conditions.Add("(" + SetSearchPredicate + ")");
        if (SharedListNames.Key(query.Customer).Length > 0) conditions.Add("s.customerKey=$customer");
        var where = conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);

        lock (_sync)
        {
            using var count = _connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM drawing_sets s {where}";
            AddSetFilterParameters(count, query);
            var total = checked((int)(long)count.ExecuteScalar()!);

            using var command = _connection.CreateCommand();
            var direction = query.Descending ? "DESC" : "ASC";
            command.CommandText = $"""
                SELECT {SetColumns}, {SetMemberCount} FROM drawing_sets s {where}
                ORDER BY {SetSortColumn(query.Sort)} {direction}, s.id {direction}
                LIMIT $limit OFFSET $offset
                """;
            AddSetFilterParameters(command, query);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            return new DrawingSetPage { Items = ReadSets(command), Total = total, Offset = query.Offset, Limit = query.Limit };
        }
    }

    /// <summary>Stores revision 1 under a client-allocated id; an existing id is a conflict.</summary>
    public DrawingSetRecord InsertDrawingSet(Guid id, DrawingSetRecord record)
    {
        if (id == Guid.Empty) throw new ArgumentException("A set id is required.", nameof(id));
        ValidateSet(record);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (GetDrawingSet(id) is { } existing)
                throw new DrawingSetConflictException(existing, "A drawing set with this id already exists.");
            RequireMemberDrawings(record, transaction);
            WriteSetRevision(id, record, record.Members, 1, archived: false, transaction);
            transaction.Commit();
            return GetDrawingSet(id) ?? throw new InvalidOperationException("Insert did not persist.");
        }
    }

    /// <summary>
    /// Appends a revision with new details and members. Returns null for an unknown id;
    /// an identical save (details and members in order) is a no-op.
    /// </summary>
    public DrawingSetRecord? UpdateDrawingSet(Guid id, DrawingSetRecord record, int expectedRevision)
    {
        ValidateSet(record);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = GetDrawingSet(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision)
                throw new DrawingSetConflictException(current, "The drawing set has changed. Reload before saving.");
            if (current.Archived)
                throw new DrawingSetConflictException(current, "The drawing set is archived. Restore it before saving.");
            if (SameSet(current, record))
                return current;

            RequireMemberDrawings(record, transaction);
            WriteSetRevision(id, record, record.Members, checked(current.Revision + 1), archived: false, transaction);
            transaction.Commit();
            return GetDrawingSet(id);
        }
    }

    /// <summary>Archives or restores a set as a new revision with the same members. Returns null for an unknown id.</summary>
    public DrawingSetRecord? SetDrawingSetArchived(Guid id, bool archived, int expectedRevision)
    {
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var current = GetDrawingSet(id);
            if (current is null) return null;
            if (current.Revision != expectedRevision)
                throw new DrawingSetConflictException(current, "The drawing set has changed. Reload and try again.");
            if (current.Archived == archived)
                throw new DrawingSetConflictException(current,
                    archived ? "The drawing set is already archived." : "The drawing set is not archived.");

            WriteSetRevision(id, current, current.Members, checked(current.Revision + 1), archived, transaction);
            transaction.Commit();
            return GetDrawingSet(id);
        }
    }

    /// <summary>One page of revisions (without members), newest first, or null for an unknown id.</summary>
    public DrawingSetPage? DrawingSetHistory(Guid id, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_sync)
        {
            if (GetDrawingSet(id) is null) return null;
            var total = CountRevisions("drawing_set_revisions", id);
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SetColumns}, {SetMemberCount} FROM drawing_set_revisions s WHERE s.id=$id
                ORDER BY s.revision DESC LIMIT $limit OFFSET $offset
                """;
            BindRevisionPage(command, id, offset, limit);
            return new DrawingSetPage { Items = ReadSets(command), Total = total, Offset = offset, Limit = limit };
        }
    }

    /// <summary>
    /// The sets, archived ones included, whose current revision contains the drawing, by
    /// name; null for an unknown drawing.
    /// </summary>
    public DrawingSetPage? DrawingSetsContaining(Guid drawingId, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        const string where = """
            WHERE EXISTS (SELECT 1 FROM drawing_set_members m
                WHERE m.setId = s.id AND m.revision = s.revision AND m.drawingId = $drawing)
            """;
        lock (_sync)
        {
            if (GetDrawing(drawingId) is null) return null;
            using var count = _connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM drawing_sets s {where}";
            count.Parameters.AddWithValue("$drawing", drawingId.ToString());
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SetColumns}, {SetMemberCount} FROM drawing_sets s {where}
                ORDER BY s.name COLLATE NOCASE, s.id LIMIT $limit OFFSET $offset
                """;
            command.Parameters.AddWithValue("$drawing", drawingId.ToString());
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            return new DrawingSetPage { Items = ReadSets(command), Total = total, Offset = offset, Limit = limit };
        }
    }

    private static void ValidateSet(DrawingSetRecord record)
    {
        var error = DrawingSetRules.GetError(record);
        if (error is not null)
            throw new ArgumentException(error);
    }

    // Drawings are never deleted, so a member checked here stays valid; archived drawings are allowed.
    private void RequireMemberDrawings(DrawingSetRecord record, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM drawings WHERE id=$id";
        var parameter = command.Parameters.Add("$id", SqliteType.Text);
        foreach (var member in record.Members)
        {
            parameter.Value = member.DrawingId.ToString();
            if (command.ExecuteScalar() is null)
                throw new ArgumentException($"Drawing {member.DrawingId} is not in the library.");
        }
    }

    // Members compare by drawing and quantity in order; the joined drawings are not part of the set.
    private static bool SameSet(DrawingSetRecord current, DrawingSetRecord record) =>
        current.Name == record.Name && current.Customer == record.Customer && current.Notes == record.Notes &&
        current.Members.Select(m => (m.DrawingId, m.Quantity))
            .SequenceEqual(record.Members.Select(m => (m.DrawingId, m.Quantity)));

    // Called only under _sync with the caller's transaction; the current row, its revision
    // snapshot and the revision's members are one write.
    private void WriteSetRevision(Guid id, DrawingSetRecord record, IReadOnlyList<DrawingSetMember> members,
        int revision, bool archived, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT OR REPLACE INTO drawing_sets ({SetStoredColumns})
            VALUES ($id, $revision, $name, $customer, $notes, $archived, $savedAt, $savedBy, $nameKey, $customerKey)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$customer", record.Customer);
        command.Parameters.AddWithValue("$notes", record.Notes);
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        command.Parameters.AddWithValue("$savedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$savedBy", record.SavedBy);
        command.Parameters.AddWithValue("$nameKey", SharedListNames.Key(record.Name));
        command.Parameters.AddWithValue("$customerKey", SharedListNames.Key(record.Customer));
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = $"INSERT INTO drawing_set_revisions ({SetStoredColumns}) SELECT {SetStoredColumns} FROM drawing_sets WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO drawing_set_members (setId, revision, position, drawingId, quantity)
            VALUES ($id, $revision, $position, $drawing, $quantity)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$revision", revision);
        var position = command.Parameters.Add("$position", SqliteType.Integer);
        var drawing = command.Parameters.Add("$drawing", SqliteType.Text);
        var quantity = command.Parameters.Add("$quantity", SqliteType.Integer);
        for (var i = 0; i < members.Count; i++)
        {
            position.Value = i;
            drawing.Value = members[i].DrawingId.ToString();
            quantity.Value = members[i].Quantity;
            command.ExecuteNonQuery();
        }
    }

    // Called only under _sync. Reads one record row; the revision parameter is bound when given.
    private DrawingSetRecord? ReadSet(string sql, Guid id, int? revision)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        if (revision is { } value)
            command.Parameters.AddWithValue("$revision", value);
        return ReadSets(command).FirstOrDefault();
    }

    private static List<DrawingSetRecord> ReadSets(SqliteCommand command)
    {
        var records = new List<DrawingSetRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new DrawingSetRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                Revision = reader.GetInt32(1),
                Name = reader.GetString(2),
                Customer = reader.GetString(3),
                Notes = reader.GetString(4),
                Archived = reader.GetInt32(5) != 0,
                SavedAt = DateTime.Parse(reader.GetString(6)),
                SavedBy = reader.GetString(7),
                MemberCount = reader.GetInt32(8),
            });
        }
        return records;
    }

    // Called only under _sync. Members of the record's own revision, each joined to its drawing's current record.
    private DrawingSetRecord WithMembers(DrawingSetRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.drawingId, m.quantity, {JoinedDrawingColumns}
            FROM drawing_set_members m LEFT JOIN drawings d ON d.id = m.drawingId
            WHERE m.setId=$id AND m.revision=$revision ORDER BY m.position
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$revision", record.Revision);
        var members = new List<DrawingSetMember>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            members.Add(new DrawingSetMember
            {
                DrawingId = Guid.Parse(reader.GetString(0)),
                Quantity = reader.GetInt32(1),
                Drawing = reader.IsDBNull(2) ? null : ReadDrawing(reader, first: 2),
            });
        }
        record.Members = members;
        return record;
    }

    private static void AddSetFilterParameters(SqliteCommand command, DrawingSetQuery query)
    {
        AddSearchParameter(command, query.NormalizedSearch);
        if (SharedListNames.Key(query.Customer).Length > 0)
            command.Parameters.AddWithValue("$customer", SharedListNames.Key(query.Customer));
    }

    // Fixed allowlist: request text never reaches the ORDER BY clause.
    private static string SetSortColumn(DrawingSetSortField field) => field switch
    {
        DrawingSetSortField.SavedAt => "s.savedAt",
        DrawingSetSortField.Name => "s.name COLLATE NOCASE",
        DrawingSetSortField.Customer => "s.customer COLLATE NOCASE",
        DrawingSetSortField.Revision => "s.revision",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported sort column."),
    };
}

/// <summary>A guarded drawing set write found a different current state; nothing was changed.</summary>
public sealed class DrawingSetConflictException(DrawingSetRecord current, string message) : Exception(message)
{
    public DrawingSetRecord Current { get; } = current;
}
