using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Release storage. <c>releases</c> records who released and whether it was undone;
/// <c>release_sets</c> which set revisions were released and how many kits of each;
/// <c>release_drawings</c> which library drawings were released on their own and how many;
/// <c>release_nests</c> the nests each release created or merged into, with the nest
/// revision before and after; <c>release_nest_items</c> the drawing quantities each set, or
/// each drawing on its own (no set), added to each nest. A created nest belongs to that release only; a merged nest may be in
/// several. Releases are append-only and read joined to each nest's current metadata.
/// </summary>
public sealed partial class NestDatabase
{
    // The v7 layout. Still created by the v7 step so every database reaches v9 by one path.
    private const string SetReleaseTablesV7 = """
        CREATE TABLE set_releases (
            id          TEXT NOT NULL PRIMARY KEY,
            setId       TEXT NOT NULL,
            setRevision INTEGER NOT NULL,
            multiplier  INTEGER NOT NULL,
            notes       TEXT NOT NULL DEFAULT '',
            releasedAt  TEXT NOT NULL,
            releasedBy  TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (setId, setRevision) REFERENCES drawing_set_revisions(id, revision));
        CREATE TABLE set_release_nests (
            releaseId     TEXT NOT NULL REFERENCES set_releases(id),
            position      INTEGER NOT NULL,
            nestId        TEXT NOT NULL REFERENCES nests(id),
            materialName  TEXT NOT NULL DEFAULT '',
            materialGrade TEXT NOT NULL DEFAULT '',
            thickness     REAL NOT NULL,
            units         TEXT NOT NULL,
            cutType       TEXT NOT NULL,
            PRIMARY KEY (releaseId, position));
        CREATE INDEX ix_set_releases_set ON set_releases(setId, releasedAt DESC);
        CREATE UNIQUE INDEX ix_set_release_nests_nest ON set_release_nests(nestId);
        """;

    // v8 -> v9. A v7 release becomes one set line (its multiplier is the quantity) and its
    // nests become created nests at their first revision, which is the one the release
    // uploaded. Their item quantities were never recorded, so they have none.
    private const string ReleaseUpgradeToV9 = """
        CREATE TABLE releases (
            id         TEXT NOT NULL PRIMARY KEY,
            notes      TEXT NOT NULL DEFAULT '',
            releasedAt TEXT NOT NULL,
            releasedBy TEXT NOT NULL DEFAULT '',
            undoneAt   TEXT,
            undoneBy   TEXT NOT NULL DEFAULT '');
        CREATE TABLE release_sets (
            releaseId   TEXT NOT NULL REFERENCES releases(id),
            position    INTEGER NOT NULL,
            setId       TEXT NOT NULL,
            setRevision INTEGER NOT NULL,
            quantity    INTEGER NOT NULL,
            PRIMARY KEY (releaseId, position),
            UNIQUE (releaseId, setId),
            FOREIGN KEY (setId, setRevision) REFERENCES drawing_set_revisions(id, revision));
        CREATE TABLE release_nests (
            releaseId      TEXT NOT NULL REFERENCES releases(id),
            position       INTEGER NOT NULL,
            nestId         TEXT NOT NULL REFERENCES nests(id),
            kind           TEXT NOT NULL,
            revisionBefore INTEGER,
            revisionAfter  INTEGER NOT NULL,
            undo           TEXT,
            undoRevision   INTEGER,
            materialName   TEXT NOT NULL DEFAULT '',
            materialGrade  TEXT NOT NULL DEFAULT '',
            thickness      REAL NOT NULL,
            units          TEXT NOT NULL,
            cutType        TEXT NOT NULL,
            PRIMARY KEY (releaseId, position),
            UNIQUE (releaseId, nestId));
        CREATE TABLE release_nest_items (
            releaseId       TEXT NOT NULL,
            nestId          TEXT NOT NULL,
            position        INTEGER NOT NULL,
            setId           TEXT NOT NULL,
            drawingId       TEXT NOT NULL,
            drawingRevision INTEGER NOT NULL,
            quantity        INTEGER NOT NULL,
            PRIMARY KEY (releaseId, nestId, position),
            UNIQUE (releaseId, nestId, setId, drawingId),
            FOREIGN KEY (releaseId, nestId) REFERENCES release_nests(releaseId, nestId));
        CREATE INDEX ix_releases_releasedAt ON releases(releasedAt DESC);
        CREATE INDEX ix_release_sets_set ON release_sets(setId);
        CREATE INDEX ix_release_nests_nest ON release_nests(nestId);

        INSERT INTO releases (id, notes, releasedAt, releasedBy)
            SELECT id, notes, releasedAt, releasedBy FROM set_releases;
        INSERT INTO release_sets (releaseId, position, setId, setRevision, quantity)
            SELECT id, 0, setId, setRevision, multiplier FROM set_releases;
        INSERT INTO release_nests (releaseId, position, nestId, kind, revisionBefore, revisionAfter,
                materialName, materialGrade, thickness, units, cutType)
            SELECT r.releaseId, r.position, r.nestId, 'Created', NULL,
                COALESCE((SELECT MIN(v.revision) FROM nest_revisions v WHERE v.id = r.nestId), 1),
                r.materialName, r.materialGrade, r.thickness, r.units, r.cutType
            FROM set_release_nests r;
        DROP INDEX ix_set_release_nests_nest;
        DROP INDEX ix_set_releases_set;
        DROP TABLE set_release_nests;
        DROP TABLE set_releases;
        PRAGMA user_version=9;
        """;

    // v10 -> v11. Releases may name library drawings on their own, and an item without a set
    // is such a drawing's quantity. SQLite cannot drop NOT NULL from a column, so the item
    // table is rebuilt with every row kept. IF NOT EXISTS keeps the step safe on a database
    // rewound to v10.
    private const string ReleaseUpgradeToV11 = """
        CREATE TABLE IF NOT EXISTS release_drawings (
            releaseId TEXT NOT NULL REFERENCES releases(id),
            position  INTEGER NOT NULL,
            drawingId TEXT NOT NULL REFERENCES drawings(id),
            quantity  INTEGER NOT NULL,
            PRIMARY KEY (releaseId, position),
            UNIQUE (releaseId, drawingId));
        CREATE INDEX IF NOT EXISTS ix_release_drawings_drawing ON release_drawings(drawingId);
        CREATE TABLE release_nest_items_v11 (
            releaseId       TEXT NOT NULL,
            nestId          TEXT NOT NULL,
            position        INTEGER NOT NULL,
            setId           TEXT,
            drawingId       TEXT NOT NULL,
            drawingRevision INTEGER NOT NULL,
            quantity        INTEGER NOT NULL,
            PRIMARY KEY (releaseId, nestId, position),
            UNIQUE (releaseId, nestId, setId, drawingId),
            FOREIGN KEY (releaseId, nestId) REFERENCES release_nests(releaseId, nestId));
        INSERT INTO release_nest_items_v11 (releaseId, nestId, position, setId, drawingId, drawingRevision, quantity)
            SELECT releaseId, nestId, position, setId, drawingId, drawingRevision, quantity FROM release_nest_items;
        DROP TABLE release_nest_items;
        ALTER TABLE release_nest_items_v11 RENAME TO release_nest_items;
        PRAGMA user_version=11;
        """;

    private const string ReleaseSelect =
        "SELECT x.id, x.notes, x.releasedAt, x.releasedBy, x.undoneAt, x.undoneBy FROM releases x";

    private static readonly string JoinedNestColumns =
        string.Join(", ", RecordColumns.Split(',').Select(column => "n." + column.Trim()));

    /// <summary>
    /// Records a release whose nests already exist. Set revisions and drawings released on
    /// their own must exist but need not be current, and sets and drawings may be archived: the nests already exist, so a release is never
    /// refused for a state the operator cannot fix. A merged nest need not still be at the
    /// revision the release wrote.
    /// </summary>
    public SetReleaseRecord InsertRelease(SetReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Id == Guid.Empty) throw new ArgumentException("A release id is required.");
        var error = SetReleaseRules.GetError(release);
        if (error is not null)
            throw new ArgumentException(error);

        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (ReadRelease(release.Id) is { } existing)
                throw new SetReleaseConflictException(existing, "A release with this id already exists.");

            foreach (var line in release.Sets)
            {
                if (!Exists("SELECT 1 FROM drawing_set_revisions WHERE id=$id AND revision=$revision", line.SetId, line.SetRevision))
                {
                    throw new ArgumentException(GetDrawingSet(line.SetId) is null
                        ? $"Set {line.SetId} does not exist."
                        : $"Set {line.SetId} has no revision {line.SetRevision}.");
                }
            }

            foreach (var drawing in release.Drawings)
            {
                if (GetDrawing(drawing.DrawingId) is null)
                    throw new ArgumentException($"Drawing {drawing.DrawingId} does not exist.");
            }

            foreach (var nest in release.Nests)
            {
                if (Get(nest.NestId) is null)
                {
                    throw new ArgumentException(Get(nest.NestId, deleted: true) is null
                        ? $"Nest {nest.NestId} does not exist."
                        : $"Nest {nest.NestId} is deleted.");
                }

                foreach (var revision in new[] { nest.RevisionBefore, nest.RevisionAfter }.OfType<int>())
                {
                    if (!Exists("SELECT 1 FROM nest_revisions WHERE id=$id AND revision=$revision", nest.NestId, revision))
                        throw new ArgumentException($"Nest {nest.NestId} has no revision {revision}.");
                }

                // A created nest came from this release alone; only merges share a nest.
                if (nest.Kind == SetReleaseNestKind.Created && ReleasesOfNest(nest.NestId) is [var owner, ..])
                    throw new SetReleaseConflictException(ReadRelease(owner)!, $"Nest {nest.NestId} already belongs to a release.");
            }

            WriteRelease(release, transaction);
            transaction.Commit();
            return ReadRelease(release.Id) ?? throw new InvalidOperationException("Release did not persist.");
        }
    }

    public SetReleaseRecord? GetRelease(Guid id)
    {
        lock (_sync)
            return ReadRelease(id);
    }

    /// <summary>
    /// One page of releases, newest first, optionally only those that include
    /// <paramref name="setId"/>, <paramref name="nestId"/> or <paramref name="drawingId"/>
    /// released on its own.
    /// </summary>
    public SetReleasePage QueryReleases(Guid? setId, Guid? nestId, int offset, int limit, Guid? drawingId = null)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        var filters = new List<string>();
        if (setId is not null)
            filters.Add("EXISTS (SELECT 1 FROM release_sets s WHERE s.releaseId = x.id AND s.setId = $setId)");
        if (nestId is not null)
            filters.Add("EXISTS (SELECT 1 FROM release_nests r WHERE r.releaseId = x.id AND r.nestId = $nestId)");
        if (drawingId is not null)
            filters.Add("EXISTS (SELECT 1 FROM release_drawings d WHERE d.releaseId = x.id AND d.drawingId = $drawingId)");
        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);

        void AddFilters(SqliteCommand command)
        {
            if (setId is not null) command.Parameters.AddWithValue("$setId", setId.Value.ToString());
            if (nestId is not null) command.Parameters.AddWithValue("$nestId", nestId.Value.ToString());
            if (drawingId is not null) command.Parameters.AddWithValue("$drawingId", drawingId.Value.ToString());
        }

        lock (_sync)
        {
            using var count = _connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM releases x {where}";
            AddFilters(count);
            var total = checked((int)(long)count.ExecuteScalar()!);

            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                {ReleaseSelect} {where}
                ORDER BY x.releasedAt DESC, x.id DESC LIMIT $limit OFFSET $offset
                """;
            AddFilters(command);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var releases = ReadReleases(command);
            foreach (var release in releases)
                Complete(release);
            return new SetReleasePage { Items = releases, Total = total, Offset = offset, Limit = limit };
        }
    }

    /// <summary>
    /// Undoes a release in one transaction: each created nest is deleted and each merged nest
    /// restored to the revision before the merge, except the nests the request skips. Every
    /// other nest must still hold the revision the release wrote (at it, or restored to it);
    /// otherwise nothing changes and <see cref="ReleaseUndoRefusedException"/> lists the
    /// nests that moved on. So releases sharing a nest are undone newest first.
    /// Returns null for an unknown release.
    /// </summary>
    public SetReleaseRecord? UndoRelease(Guid id, SetReleaseUndoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var error = SetReleaseRules.GetUndoError(request);
        if (error is not null)
            throw new ArgumentException(error);

        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var release = ReadRelease(id);
            if (release is null) return null;
            if (release.IsUndone)
                throw new ReleaseUndoRefusedException(new SetReleaseUndoConflict
                {
                    Reason = SetReleaseUndoRefusal.AlreadyUndone,
                    Release = release,
                });

            var skipped = request.SkipNests.ToHashSet();
            foreach (var skip in skipped)
            {
                if (release.Nests.All(n => n.NestId != skip))
                    throw new ArgumentException($"Nest {skip} is not part of this release.");
            }

            // A nest still holds what the release wrote when it is at that revision, or when its
            // latest save restored it (such as undoing a later release that merged into it).
            var expected = new Dictionary<Guid, int>();
            var changed = new List<NestRecord>();
            foreach (var nest in release.Nests.Where(n => !skipped.Contains(n.NestId)))
            {
                var current = Get(nest.NestId) ?? Get(nest.NestId, deleted: true);
                if (current is { Deleted: false }
                    && (current.Revision == nest.RevisionAfter || current.RestoredFromRevision == nest.RevisionAfter))
                    expected[nest.NestId] = current.Revision;
                else
                    changed.Add(current ?? new NestRecord { Id = nest.NestId, Deleted = true });
            }
            if (changed.Count > 0)
                throw new ReleaseUndoRefusedException(new SetReleaseUndoConflict
                {
                    Reason = SetReleaseUndoRefusal.NestsChanged,
                    Release = release,
                    ChangedNests = changed,
                });

            using var mark = _connection.CreateCommand();
            mark.Transaction = transaction;
            mark.CommandText = "UPDATE release_nests SET undo=$undo, undoRevision=$revision WHERE releaseId=$id AND nestId=$nest";
            mark.Parameters.AddWithValue("$id", id.ToString());
            var undo = mark.Parameters.Add("$undo", SqliteType.Text);
            var revision = mark.Parameters.Add("$revision", SqliteType.Integer);
            var nestId = mark.Parameters.Add("$nest", SqliteType.Text);
            foreach (var nest in release.Nests)
            {
                SetReleaseNestUndo outcome;
                if (skipped.Contains(nest.NestId))
                {
                    outcome = SetReleaseNestUndo.Skipped;
                }
                else if (nest.Kind == SetReleaseNestKind.Created)
                {
                    if (!MarkDeleted(nest.NestId, true, expected[nest.NestId], transaction))
                        throw new InvalidOperationException($"Nest {nest.NestId} could not be deleted.");
                    outcome = SetReleaseNestUndo.Deleted;
                }
                else
                {
                    if (!RestoreRevision(nest.NestId, nest.RevisionBefore!.Value, expected[nest.NestId], transaction))
                        throw new InvalidOperationException($"Nest {nest.NestId} has no revision {nest.RevisionBefore}.");
                    outcome = SetReleaseNestUndo.Restored;
                }

                undo.Value = outcome.ToString();
                revision.Value = outcome == SetReleaseNestUndo.Skipped ? DBNull.Value : checked(expected[nest.NestId] + 1);
                nestId.Value = nest.NestId.ToString();
                mark.ExecuteNonQuery();
            }

            mark.Parameters.Clear();
            mark.CommandText = "UPDATE releases SET undoneAt=$at, undoneBy=$by WHERE id=$id";
            mark.Parameters.AddWithValue("$id", id.ToString());
            mark.Parameters.AddWithValue("$at", DateTime.Now.ToString("o"));
            mark.Parameters.AddWithValue("$by", request.UndoneBy);
            mark.ExecuteNonQuery();

            transaction.Commit();
            return ReadRelease(id);
        }
    }

    // Called only under _sync. The releases that include the nest, newest first.
    private List<Guid> ReleasesOfNest(Guid nestId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT x.id FROM releases x JOIN release_nests r ON r.releaseId = x.id
            WHERE r.nestId=$id ORDER BY x.releasedAt DESC, x.id DESC
            """;
        command.Parameters.AddWithValue("$id", nestId.ToString());
        var ids = new List<Guid>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    // Called only under _sync.
    private bool Exists(string sql, Guid id, int revision)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$revision", revision);
        return command.ExecuteScalar() is not null;
    }

    // Called only under _sync with the caller's transaction.
    private void WriteRelease(SetReleaseRecord release, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO releases (id, notes, releasedAt, releasedBy)
            VALUES ($id, $notes, $releasedAt, $releasedBy)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        command.Parameters.AddWithValue("$notes", release.Notes);
        command.Parameters.AddWithValue("$releasedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$releasedBy", release.ReleasedBy);
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO release_sets (releaseId, position, setId, setRevision, quantity)
            VALUES ($id, $position, $set, $revision, $quantity)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        var position = command.Parameters.Add("$position", SqliteType.Integer);
        var setId = command.Parameters.Add("$set", SqliteType.Text);
        var setRevision = command.Parameters.Add("$revision", SqliteType.Integer);
        var quantity = command.Parameters.Add("$quantity", SqliteType.Integer);
        for (var i = 0; i < release.Sets.Count; i++)
        {
            var line = release.Sets[i];
            position.Value = i;
            setId.Value = line.SetId.ToString();
            setRevision.Value = line.SetRevision;
            quantity.Value = line.Quantity;
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO release_drawings (releaseId, position, drawingId, quantity)
            VALUES ($id, $position, $drawing, $quantity)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        position = command.Parameters.Add("$position", SqliteType.Integer);
        var drawing = command.Parameters.Add("$drawing", SqliteType.Text);
        quantity = command.Parameters.Add("$quantity", SqliteType.Integer);
        for (var i = 0; i < release.Drawings.Count; i++)
        {
            position.Value = i;
            drawing.Value = release.Drawings[i].DrawingId.ToString();
            quantity.Value = release.Drawings[i].Quantity;
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO release_nests (releaseId, position, nestId, kind, revisionBefore, revisionAfter,
                materialName, materialGrade, thickness, units, cutType)
            VALUES ($id, $position, $nest, $kind, $before, $after,
                $materialName, $materialGrade, $thickness, $units, $cutType)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        position = command.Parameters.Add("$position", SqliteType.Integer);
        var nestId = command.Parameters.Add("$nest", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var before = command.Parameters.Add("$before", SqliteType.Integer);
        var after = command.Parameters.Add("$after", SqliteType.Integer);
        var materialName = command.Parameters.Add("$materialName", SqliteType.Text);
        var materialGrade = command.Parameters.Add("$materialGrade", SqliteType.Text);
        var thickness = command.Parameters.Add("$thickness", SqliteType.Real);
        var units = command.Parameters.Add("$units", SqliteType.Text);
        var cutType = command.Parameters.Add("$cutType", SqliteType.Text);
        for (var i = 0; i < release.Nests.Count; i++)
        {
            var nest = release.Nests[i];
            position.Value = i;
            nestId.Value = nest.NestId.ToString();
            kind.Value = nest.Kind.ToString();
            before.Value = (object?)nest.RevisionBefore ?? DBNull.Value;
            after.Value = nest.RevisionAfter;
            materialName.Value = nest.MaterialName;
            materialGrade.Value = nest.MaterialGrade;
            thickness.Value = DrawingLibraryRules.NormalizeThickness(nest.Thickness);
            units.Value = nest.Units.ToString();
            cutType.Value = nest.CutType.ToString();
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO release_nest_items (releaseId, nestId, position, setId, drawingId, drawingRevision, quantity)
            VALUES ($id, $nest, $position, $set, $drawing, $revision, $quantity)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        nestId = command.Parameters.Add("$nest", SqliteType.Text);
        position = command.Parameters.Add("$position", SqliteType.Integer);
        setId = command.Parameters.Add("$set", SqliteType.Text);
        var drawingId = command.Parameters.Add("$drawing", SqliteType.Text);
        var drawingRevision = command.Parameters.Add("$revision", SqliteType.Integer);
        quantity = command.Parameters.Add("$quantity", SqliteType.Integer);
        foreach (var nest in release.Nests)
        {
            nestId.Value = nest.NestId.ToString();
            for (var i = 0; i < nest.Items.Count; i++)
            {
                var item = nest.Items[i];
                position.Value = i;
                setId.Value = (object?)item.SetId?.ToString() ?? DBNull.Value;
                drawingId.Value = item.DrawingId.ToString();
                drawingRevision.Value = item.DrawingRevision;
                quantity.Value = item.Quantity;
                command.ExecuteNonQuery();
            }
        }
    }

    // Called only under _sync.
    private SetReleaseRecord? ReadRelease(Guid releaseId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"{ReleaseSelect} WHERE x.id=$id";
        command.Parameters.AddWithValue("$id", releaseId.ToString());
        var release = ReadReleases(command).FirstOrDefault();
        if (release is not null)
            Complete(release);
        return release;
    }

    private static List<SetReleaseRecord> ReadReleases(SqliteCommand command)
    {
        var releases = new List<SetReleaseRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            releases.Add(new SetReleaseRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                Notes = reader.GetString(1),
                ReleasedAt = DateTime.Parse(reader.GetString(2)),
                ReleasedBy = reader.GetString(3),
                UndoneAt = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                UndoneBy = reader.GetString(5),
            });
        }
        return releases;
    }

    // Called only under _sync. Reads the release's sets (named as at their revision), its
    // drawings on their own (named as now) and its nests, each joined to its current metadata,
    // deleted or not, with its items.
    private void Complete(SetReleaseRecord release)
    {
        var id = release.Id.ToString();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.setId, s.setRevision, s.quantity, COALESCE(v.name, '')
                FROM release_sets s LEFT JOIN drawing_set_revisions v ON v.id = s.setId AND v.revision = s.setRevision
                WHERE s.releaseId=$id ORDER BY s.position
                """;
            command.Parameters.AddWithValue("$id", id);
            var sets = new List<SetReleaseLine>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                sets.Add(new SetReleaseLine
                {
                    SetId = Guid.Parse(reader.GetString(0)),
                    SetRevision = reader.GetInt32(1),
                    Quantity = reader.GetInt32(2),
                    SetName = reader.GetString(3),
                });
            }
            release.Sets = sets;
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT d.drawingId, d.quantity, COALESCE(x.name, '')
                FROM release_drawings d LEFT JOIN drawings x ON x.id = d.drawingId
                WHERE d.releaseId=$id ORDER BY d.position
                """;
            command.Parameters.AddWithValue("$id", id);
            var drawings = new List<SetReleaseDrawing>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                drawings.Add(new SetReleaseDrawing
                {
                    DrawingId = Guid.Parse(reader.GetString(0)),
                    Quantity = reader.GetInt32(1),
                    DrawingName = reader.GetString(2),
                });
            }
            release.Drawings = drawings;
        }

        var items = new Dictionary<Guid, List<SetReleaseNestItem>>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT nestId, setId, drawingId, drawingRevision, quantity
                FROM release_nest_items WHERE releaseId=$id ORDER BY nestId, position
                """;
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var nestId = Guid.Parse(reader.GetString(0));
                if (!items.TryGetValue(nestId, out var list))
                    items[nestId] = list = new List<SetReleaseNestItem>();
                list.Add(new SetReleaseNestItem
                {
                    SetId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                    DrawingId = Guid.Parse(reader.GetString(2)),
                    DrawingRevision = reader.GetInt32(3),
                    Quantity = reader.GetInt32(4),
                });
            }
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT r.nestId, r.kind, r.revisionBefore, r.revisionAfter, r.undo, r.undoRevision,
                    r.materialName, r.materialGrade, r.thickness, r.units, r.cutType, {JoinedNestColumns}
                FROM release_nests r LEFT JOIN nests n ON n.id = r.nestId
                WHERE r.releaseId=$id ORDER BY r.position
                """;
            command.Parameters.AddWithValue("$id", id);
            var nests = new List<SetReleaseNest>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var nestId = Guid.Parse(reader.GetString(0));
                nests.Add(new SetReleaseNest
                {
                    NestId = nestId,
                    Kind = Enum.Parse<SetReleaseNestKind>(reader.GetString(1)),
                    RevisionBefore = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    RevisionAfter = reader.GetInt32(3),
                    Undo = reader.IsDBNull(4) ? null : Enum.Parse<SetReleaseNestUndo>(reader.GetString(4)),
                    UndoRevision = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    MaterialName = reader.GetString(6),
                    MaterialGrade = reader.GetString(7),
                    Thickness = reader.GetDouble(8),
                    Units = Enum.Parse<Units>(reader.GetString(9)),
                    CutType = Enum.Parse<CutType>(reader.GetString(10)),
                    Items = items.TryGetValue(nestId, out var list) ? list : Array.Empty<SetReleaseNestItem>(),
                    Nest = reader.IsDBNull(11) ? null : ReadRecord(reader, first: 11),
                });
            }
            release.Nests = nests;
        }
    }
}

/// <summary>A release post collided with an existing release; nothing was changed.</summary>
public sealed class SetReleaseConflictException(SetReleaseRecord current, string message) : Exception(message)
{
    /// <summary>The release that already holds the id or the created nest.</summary>
    public SetReleaseRecord Current { get; } = current;
}

/// <summary>An undo was refused; nothing was changed.</summary>
public sealed class ReleaseUndoRefusedException(SetReleaseUndoConflict conflict)
    : Exception(conflict.Reason == SetReleaseUndoRefusal.AlreadyUndone
        ? "The release was already undone."
        : "Nests of the release have changed since it was made.")
{
    public SetReleaseUndoConflict Conflict { get; } = conflict;
}
