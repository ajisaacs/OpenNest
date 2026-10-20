using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Set release storage. <c>set_releases</c> records which set revision was released and
/// how many times over; <c>set_release_nests</c> the nests it made, in order, with the
/// material group each was made for. A nest belongs to at most one release. Releases are
/// append-only and read joined to each nest's current metadata.
/// </summary>
public sealed partial class NestDatabase
{
    // Part of the v7 set schema.
    private const string SetReleaseTables = """
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

    private const string ReleaseSelect = """
        SELECT x.id, x.setId, x.setRevision, x.multiplier, x.notes, x.releasedAt, x.releasedBy, COALESCE(v.name, '')
        FROM set_releases x LEFT JOIN drawing_set_revisions v ON v.id = x.setId AND v.revision = x.setRevision
        """;

    private static readonly string JoinedNestColumns =
        string.Join(", ", RecordColumns.Split(',').Select(column => "n." + column.Trim()));

    /// <summary>
    /// Records a release of <paramref name="setId"/>. Returns null for an unknown set. The
    /// set revision must exist but need not be current, and the set may be archived: the
    /// nests already exist, so a release is never refused for a state the operator cannot fix.
    /// </summary>
    public SetReleaseRecord? InsertSetRelease(Guid setId, SetReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Id == Guid.Empty) throw new ArgumentException("A release id is required.");
        var error = SetReleaseRules.GetError(release);
        if (error is not null)
            throw new ArgumentException(error);

        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (GetDrawingSet(setId) is null) return null;
            if (ReadRelease(release.Id) is { } existing)
                throw new SetReleaseConflictException(existing, "A release with this id already exists.");
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT 1 FROM drawing_set_revisions WHERE id=$id AND revision=$revision";
                command.Parameters.AddWithValue("$id", setId.ToString());
                command.Parameters.AddWithValue("$revision", release.SetRevision);
                if (command.ExecuteScalar() is null)
                    throw new ArgumentException($"The set has no revision {release.SetRevision}.");
            }

            foreach (var nest in release.Nests)
            {
                if (Get(nest.NestId) is null)
                {
                    throw new ArgumentException(Get(nest.NestId, deleted: true) is null
                        ? $"Nest {nest.NestId} does not exist."
                        : $"Nest {nest.NestId} is deleted.");
                }

                if (ReleaseForNest(nest.NestId) is { } owner)
                    throw new SetReleaseConflictException(owner, $"Nest {nest.NestId} already belongs to a release.");
            }

            WriteRelease(setId, release, transaction);
            transaction.Commit();
            return ReadRelease(release.Id) ?? throw new InvalidOperationException("Release did not persist.");
        }
    }

    /// <summary>One page of the set's releases, newest first, or null for an unknown set.</summary>
    public SetReleasePage? SetReleases(Guid setId, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_sync)
        {
            if (GetDrawingSet(setId) is null) return null;
            using var count = _connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM set_releases WHERE setId=$id";
            count.Parameters.AddWithValue("$id", setId.ToString());
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                {ReleaseSelect} WHERE x.setId=$id
                ORDER BY x.releasedAt DESC, x.id DESC LIMIT $limit OFFSET $offset
                """;
            command.Parameters.AddWithValue("$id", setId.ToString());
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var releases = ReadReleases(command);
            foreach (var release in releases)
                release.Nests = ReadReleaseNests(release.Id);
            return new SetReleasePage { Items = releases, Total = total, Offset = offset, Limit = limit };
        }
    }

    /// <summary>The release a nest, deleted or not, came from, or null when it came from none.</summary>
    public SetReleaseRecord? ReleaseForNest(Guid nestId)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT releaseId FROM set_release_nests WHERE nestId=$id";
            command.Parameters.AddWithValue("$id", nestId.ToString());
            return command.ExecuteScalar() is string releaseId ? ReadRelease(Guid.Parse(releaseId)) : null;
        }
    }

    // Called only under _sync with the caller's transaction.
    private void WriteRelease(Guid setId, SetReleaseRecord release, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO set_releases (id, setId, setRevision, multiplier, notes, releasedAt, releasedBy)
            VALUES ($id, $setId, $setRevision, $multiplier, $notes, $releasedAt, $releasedBy)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        command.Parameters.AddWithValue("$setId", setId.ToString());
        command.Parameters.AddWithValue("$setRevision", release.SetRevision);
        command.Parameters.AddWithValue("$multiplier", release.Multiplier);
        command.Parameters.AddWithValue("$notes", release.Notes);
        command.Parameters.AddWithValue("$releasedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$releasedBy", release.ReleasedBy);
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO set_release_nests (releaseId, position, nestId, materialName, materialGrade, thickness, units, cutType)
            VALUES ($id, $position, $nest, $materialName, $materialGrade, $thickness, $units, $cutType)
            """;
        command.Parameters.AddWithValue("$id", release.Id.ToString());
        var position = command.Parameters.Add("$position", SqliteType.Integer);
        var nestId = command.Parameters.Add("$nest", SqliteType.Text);
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
            materialName.Value = nest.MaterialName;
            materialGrade.Value = nest.MaterialGrade;
            thickness.Value = DrawingLibraryRules.NormalizeThickness(nest.Thickness);
            units.Value = nest.Units.ToString();
            cutType.Value = nest.CutType.ToString();
            command.ExecuteNonQuery();
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
            release.Nests = ReadReleaseNests(release.Id);
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
                SetId = Guid.Parse(reader.GetString(1)),
                SetRevision = reader.GetInt32(2),
                Multiplier = reader.GetInt32(3),
                Notes = reader.GetString(4),
                ReleasedAt = DateTime.Parse(reader.GetString(5)),
                ReleasedBy = reader.GetString(6),
                SetName = reader.GetString(7),
            });
        }
        return releases;
    }

    // Called only under _sync. Each nest joined to its current metadata, deleted or not.
    private List<SetReleaseNest> ReadReleaseNests(Guid releaseId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.nestId, r.materialName, r.materialGrade, r.thickness, r.units, r.cutType, {JoinedNestColumns}
            FROM set_release_nests r LEFT JOIN nests n ON n.id = r.nestId
            WHERE r.releaseId=$id ORDER BY r.position
            """;
        command.Parameters.AddWithValue("$id", releaseId.ToString());
        var nests = new List<SetReleaseNest>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            nests.Add(new SetReleaseNest
            {
                NestId = Guid.Parse(reader.GetString(0)),
                MaterialName = reader.GetString(1),
                MaterialGrade = reader.GetString(2),
                Thickness = reader.GetDouble(3),
                Units = Enum.Parse<Units>(reader.GetString(4)),
                CutType = Enum.Parse<CutType>(reader.GetString(5)),
                Nest = reader.IsDBNull(6) ? null : ReadRecord(reader, first: 6),
            });
        }
        return nests;
    }
}

/// <summary>A release post collided with an existing release; nothing was changed.</summary>
public sealed class SetReleaseConflictException(SetReleaseRecord current, string message) : Exception(message)
{
    /// <summary>The release that already holds the id or the nest.</summary>
    public SetReleaseRecord Current { get; } = current;
}
