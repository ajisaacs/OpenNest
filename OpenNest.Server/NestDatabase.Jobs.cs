using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// The server nesting queue (<c>docs/server-nesting.md</c>). A worker claims the oldest queued
/// job and holds it on a lease it renews by heartbeat; only one job runs at a time. A lease that
/// lapses (the worker or its container stopped) puts the job back in the queue, or fails it
/// after <see cref="NestJobRules.MaxAttempts"/>. Every method takes the current time so the
/// queue can be tested without waiting. Times are stored as round-trip UTC text, which sorts.
/// </summary>
public sealed partial class NestDatabase
{
    private const string NestJobSchema = """
        CREATE TABLE IF NOT EXISTS nest_jobs (
            id              TEXT PRIMARY KEY,
            seq             INTEGER NOT NULL,
            title           TEXT NOT NULL DEFAULT '',
            engineName      TEXT NOT NULL,
            submittedBy     TEXT NOT NULL DEFAULT '',
            state           TEXT NOT NULL,
            cancelRequested INTEGER NOT NULL DEFAULT 0,
            submittedAt     TEXT NOT NULL,
            startedAt       TEXT,
            finishedAt      TEXT,
            attempts        INTEGER NOT NULL DEFAULT 0,
            error           TEXT NOT NULL DEFAULT '',
            workerId        TEXT,
            leaseUntil      TEXT,
            packageSize     INTEGER NOT NULL,
            package         BLOB NOT NULL,
            result          TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_nest_jobs_state ON nest_jobs(state, seq);
        PRAGMA user_version=15;
        """;

    // v16: live progress. Revision counts the changes a watcher shows; progress and plan are
    // the worker's latest reports, opaque here like packages and results. Columns already
    // present (a database set back to an older version) are kept.
    private static void AddJobProgress(SqliteCommand command)
    {
        foreach (var (name, definition) in new[]
        {
            ("revision", "INTEGER NOT NULL DEFAULT 1"),
            ("planRevision", "INTEGER NOT NULL DEFAULT 0"),
            ("progress", "TEXT NOT NULL DEFAULT ''"),
            ("plan", "TEXT"),
        })
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('nest_jobs') WHERE name='{name}'";
            if ((long)command.ExecuteScalar()! == 0)
            {
                command.CommandText = $"ALTER TABLE nest_jobs ADD COLUMN {name} {definition}";
                command.ExecuteNonQuery();
            }
        }
        command.CommandText = "PRAGMA user_version=16";
        command.ExecuteNonQuery();
    }

    // v17: job lists. The nest a job was sent from, whether its result was kept or discarded
    // (each at most once), and the worker's summary of the result.
    private static void AddJobReview(SqliteCommand command)
    {
        foreach (var (name, definition) in new[]
        {
            ("nestId", "TEXT"),
            ("review", "TEXT NOT NULL DEFAULT ''"),
            ("reviewedBy", "TEXT NOT NULL DEFAULT ''"),
            ("reviewedAt", "TEXT"),
            ("partsPlaced", "INTEGER"),
            ("partsUnplaced", "INTEGER"),
            ("sheets", "INTEGER"),
            ("utilization", "REAL"),
        })
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('nest_jobs') WHERE name='{name}'";
            if ((long)command.ExecuteScalar()! == 0)
            {
                command.CommandText = $"ALTER TABLE nest_jobs ADD COLUMN {name} {definition}";
                command.ExecuteNonQuery();
            }
        }
        command.CommandText = "CREATE INDEX IF NOT EXISTS ix_nest_jobs_seq ON nest_jobs(seq); PRAGMA user_version=17";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// How long finished jobs are kept, with their packages and results, after they finish
    /// (<see cref="NestJobRules.Retention"/> unless the server is configured otherwise).
    /// </summary>
    public TimeSpan JobRetention { get; set; } = NestJobRules.Retention;

    // Every write that ends or restarts an attempt drops its progress and plan.
    private const string ClearProgress = "progress='', plan=NULL, planRevision=0";

    private TaskCompletionSource _jobsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes at the next change to any job in this process (event streams wait on it). Read
    /// it before reading the job, so a change between the two is not missed.
    /// </summary>
    public Task JobsChanged => Volatile.Read(ref _jobsChanged).Task;

    private void PulseJobs() =>
        Interlocked.Exchange(ref _jobsChanged, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private const string JobColumns =
        "id, title, engineName, submittedBy, state, cancelRequested, submittedAt, startedAt, finishedAt, attempts, error, packageSize, seq, revision, planRevision, progress, "
        + "nestId, review, reviewedBy, reviewedAt, partsPlaced, partsUnplaced, sheets, utilization, "
        // Queued jobs ahead of a queued one.
        + "CASE WHEN state='Queued' THEN (SELECT COUNT(*) FROM nest_jobs ahead WHERE ahead.state='Queued' AND ahead.seq < nest_jobs.seq) ELSE 0 END";

    public NestJobRecord InsertJob(Guid id, NestJobSubmission submission, byte[] package, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(package);
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO nest_jobs (id, seq, title, engineName, submittedBy, state, submittedAt, packageSize, package, nestId)
                VALUES ($id, (SELECT COALESCE(MAX(seq), 0) + 1 FROM nest_jobs), $title, $engine, $by, 'Queued', $now, $size, $package, $nest)
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$title", submission.Title);
            command.Parameters.AddWithValue("$engine", submission.EngineName);
            command.Parameters.AddWithValue("$by", submission.SubmittedBy);
            command.Parameters.AddWithValue("$now", Stamp(now));
            command.Parameters.AddWithValue("$size", package.LongLength);
            command.Parameters.AddWithValue("$package", package);
            command.Parameters.AddWithValue("$nest", submission.NestId is { } nest && nest != Guid.Empty ? nest.ToString() : DBNull.Value);
            command.ExecuteNonQuery();
            PulseJobs();
            return GetJob(id)!;
        }
    }

    public NestJobRecord? GetJob(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {JobColumns} FROM nest_jobs WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadJob(reader) : null;
        }
    }

    /// <summary>
    /// Jobs newest first, at most <paramref name="limit"/>. <paramref name="submittedBy"/>
    /// (ignoring case) keeps one submitter's jobs: a bare user name also matches that user on
    /// any workstation (<c>user@machine</c>). Finished jobs past <see cref="JobRetention"/> are
    /// removed first.
    /// </summary>
    public IReadOnlyList<NestJobRecord> ListJobs(NestJobListFilter filter, string? submittedBy, int limit, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (PurgeJobs(null, now))
                PulseJobs();
            using var command = _connection.CreateCommand();
            var where = new List<string>();
            switch (filter)
            {
                case NestJobListFilter.Active: where.Add("state IN ('Queued', 'Running')"); break;
                case NestJobListFilter.Finished: where.Add("state IN ('Succeeded', 'Failed', 'Cancelled')"); break;
                case NestJobListFilter.Unreviewed: where.Add("state='Succeeded' AND review=''"); break;
            }
            if (!string.IsNullOrWhiteSpace(submittedBy))
            {
                where.Add("(lower(submittedBy) = lower($by) OR substr(lower(submittedBy), 1, length($by) + 1) = lower($by) || '@')");
                command.Parameters.AddWithValue("$by", submittedBy.Trim());
            }
            command.CommandText = $"SELECT {JobColumns} FROM nest_jobs"
                + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                + " ORDER BY seq DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", System.Math.Clamp(limit, 1, NestJobRules.MaxListLimit));
            var jobs = new List<NestJobRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                jobs.Add(ReadJob(reader));
            return jobs;
        }
    }

    /// <summary>
    /// Marks a succeeded job's result Kept or Discarded, once: only while it is unreviewed and
    /// still at <paramref name="expectedRevision"/>, so of two desktops reviewing the same
    /// result only the first can keep it. Its package and result are dropped. Null when there is
    /// no such job; otherwise whether it changed and the job as it now is.
    /// </summary>
    public (bool Changed, NestJobRecord Job)? ReviewJob(Guid id, NestJobReview review, string reviewedBy, long expectedRevision, DateTimeOffset now)
    {
        if (review == NestJobReview.None)
            throw new ArgumentOutOfRangeException(nameof(review));
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE nest_jobs SET review=$review, reviewedBy=$by, reviewedAt=$now,
                    package=zeroblob(0), result=NULL, revision=revision+1
                WHERE id=$id AND state='Succeeded' AND review='' AND revision=$expected
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$review", review.ToString());
            command.Parameters.AddWithValue("$by", Truncate(reviewedBy));
            command.Parameters.AddWithValue("$now", Stamp(now));
            command.Parameters.AddWithValue("$expected", expectedRevision);
            var changed = Changed(command.ExecuteNonQuery() == 1);
            return GetJob(id) is { } job ? (changed, job) : null;
        }
    }

    /// <summary>
    /// Removes a finished job at <paramref name="expectedRevision"/> with its package and result.
    /// Null when there is no such job; otherwise whether it was removed and, when it was not
    /// (still active, or changed meanwhile), the job as it is.
    /// </summary>
    public (bool Removed, NestJobRecord? Job)? DeleteJob(Guid id, long expectedRevision)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                DELETE FROM nest_jobs
                WHERE id=$id AND state IN ('Succeeded', 'Failed', 'Cancelled') AND revision=$expected
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$expected", expectedRevision);
            if (Changed(command.ExecuteNonQuery() == 1))
                return (true, null);
            return GetJob(id) is { } job ? (false, job) : null;
        }
    }

    /// <summary>
    /// The job's package while it is queued or running, or succeeded and unreviewed (a desktop
    /// reopening the result reads the job from it); other finished jobs no longer keep it.
    /// </summary>
    public byte[]? GetJobPackage(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT package FROM nest_jobs
                WHERE id=$id AND (state IN ('Queued', 'Running') OR (state='Succeeded' AND review=''))
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() as byte[];
        }
    }

    /// <summary>A succeeded, unreviewed job's result; null for any other job.</summary>
    public string? GetJobResult(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT result FROM nest_jobs WHERE id=$id AND state='Succeeded' AND review=''";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() as string;
        }
    }

    /// <summary>
    /// A queued job is cancelled at once; a running one is marked so its worker stops at the
    /// next heartbeat. A finished job is returned unchanged. Null when there is no such job.
    /// </summary>
    public NestJobRecord? CancelJob(Guid id, DateTimeOffset now)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE nest_jobs SET
                    state = CASE state WHEN 'Queued' THEN 'Cancelled' ELSE state END,
                    finishedAt = CASE state WHEN 'Queued' THEN $now ELSE finishedAt END,
                    package = CASE state WHEN 'Queued' THEN zeroblob(0) ELSE package END,
                    cancelRequested = CASE state WHEN 'Running' THEN 1 ELSE cancelRequested END,
                    progress = CASE state WHEN 'Queued' THEN '' ELSE progress END,
                    revision = revision + 1
                WHERE id=$id AND (state='Queued' OR (state='Running' AND cancelRequested=0))
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$now", Stamp(now));
            if (command.ExecuteNonQuery() > 0)
                PulseJobs();
            return GetJob(id);
        }
    }

    /// <summary>
    /// Gives <paramref name="workerId"/> the oldest queued job on a fresh lease, or null when
    /// none is queued or another job still runs. Lapsed leases are settled first, and finished
    /// jobs past <see cref="JobRetention"/> removed.
    /// </summary>
    public NestJobRecord? ClaimJob(string workerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            if (SettleJobLeases(transaction, now))
                PulseJobs();

            using var running = _connection.CreateCommand();
            running.Transaction = transaction;
            running.CommandText = "SELECT COUNT(*) FROM nest_jobs WHERE state='Running'";
            if ((long)running.ExecuteScalar()! > 0)
            {
                transaction.Commit();
                return null;
            }

            using var next = _connection.CreateCommand();
            next.Transaction = transaction;
            next.CommandText = "SELECT id FROM nest_jobs WHERE state='Queued' ORDER BY seq LIMIT 1";
            if (next.ExecuteScalar() is not string id)
            {
                transaction.Commit();
                return null;
            }

            using var claim = _connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE nest_jobs SET state='Running', workerId=$worker, leaseUntil=$lease,
                    startedAt=$now, attempts=attempts+1, revision=revision+1
                WHERE id=$id
                """;
            claim.Parameters.AddWithValue("$id", id);
            claim.Parameters.AddWithValue("$worker", workerId);
            claim.Parameters.AddWithValue("$lease", Stamp(now + NestJobRules.Lease));
            claim.Parameters.AddWithValue("$now", Stamp(now));
            claim.ExecuteNonQuery();
            transaction.Commit();
            PulseJobs();
            return GetJob(Guid.Parse(id));
        }
    }

    /// <summary>Renews the lease. Null when <paramref name="workerId"/> no longer holds the job.</summary>
    public NestJobHeartbeat? HeartbeatJob(Guid id, string workerId, DateTimeOffset now)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE nest_jobs SET leaseUntil=$lease
                WHERE id=$id AND state='Running' AND workerId=$worker
                RETURNING cancelRequested
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$worker", workerId);
            command.Parameters.AddWithValue("$lease", Stamp(now + NestJobRules.Lease));
            return command.ExecuteScalar() is long cancel ? new NestJobHeartbeat { CancelRequested = cancel != 0 } : null;
        }
    }

    /// <summary>
    /// Stores the worker's latest progress report and renews the lease, like a heartbeat. Null
    /// when <paramref name="workerId"/> no longer holds the job.
    /// </summary>
    public NestJobHeartbeat? ReportJobProgress(Guid id, string workerId, string progress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return Report(id, workerId, now, "progress=$value, revision=revision+1", progress);
    }

    /// <summary>Stores the best plan so far, as <see cref="ReportJobProgress"/> does a report.</summary>
    public NestJobHeartbeat? ReportJobPlan(Guid id, string workerId, string plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // Right-hand sides read the old row, so planRevision is the new revision.
        return Report(id, workerId, now, "plan=$value, planRevision=revision+1, revision=revision+1", plan);
    }

    private NestJobHeartbeat? Report(Guid id, string workerId, DateTimeOffset now, string set, string value)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                UPDATE nest_jobs SET {set}, leaseUntil=$lease
                WHERE id=$id AND state='Running' AND workerId=$worker
                RETURNING cancelRequested
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$worker", workerId);
            command.Parameters.AddWithValue("$lease", Stamp(now + NestJobRules.Lease));
            command.Parameters.AddWithValue("$value", value);
            if (command.ExecuteScalar() is not long cancel)
                return null;
            PulseJobs();
            return new NestJobHeartbeat { CancelRequested = cancel != 0 };
        }
    }

    /// <summary>A running job's best plan so far and the revision it was stored at; null when it has none.</summary>
    public (string Plan, long Revision)? GetJobPlan(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT plan, planRevision FROM nest_jobs WHERE id=$id AND plan IS NOT NULL";
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetString(0), reader.GetInt64(1)) : null;
        }
    }

    /// <summary>
    /// Stores the worker's result. A job someone cancelled meanwhile ends Cancelled and the
    /// result is dropped. False when <paramref name="workerId"/> no longer holds the job.
    /// </summary>
    public bool CompleteJob(Guid id, string workerId, string result, DateTimeOffset now, NestJobSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        // A succeeded job keeps its package until it is reviewed: a desktop reopening the
        // result rebuilds the job from it. A cancelled one keeps no summary of a dropped result.
        return FinishJob(id, workerId, now, """
            state = CASE cancelRequested WHEN 1 THEN 'Cancelled' ELSE 'Succeeded' END,
            result = CASE cancelRequested WHEN 1 THEN NULL ELSE $result END,
            package = CASE cancelRequested WHEN 1 THEN zeroblob(0) ELSE package END,
            partsPlaced = CASE cancelRequested WHEN 1 THEN NULL ELSE $placed END,
            partsUnplaced = CASE cancelRequested WHEN 1 THEN NULL ELSE $unplaced END,
            sheets = CASE cancelRequested WHEN 1 THEN NULL ELSE $sheets END,
            utilization = CASE cancelRequested WHEN 1 THEN NULL ELSE $utilization END
            """, ("$result", result),
            ("$placed", summary is null ? DBNull.Value : summary.PartsPlaced),
            ("$unplaced", summary is null ? DBNull.Value : summary.PartsUnplaced),
            ("$sheets", summary is null ? DBNull.Value : summary.Sheets),
            ("$utilization", summary is null ? DBNull.Value : summary.Utilization));
    }

    public bool FailJob(Guid id, string workerId, string error, DateTimeOffset now) =>
        FinishJob(id, workerId, now, "state='Failed', error=$error, package=zeroblob(0)", ("$error", Truncate(error)));

    /// <summary>The worker stopped the job after a cancel request.</summary>
    public bool ConfirmJobCancelled(Guid id, string workerId, DateTimeOffset now) =>
        FinishJob(id, workerId, now, "state='Cancelled', package=zeroblob(0)");

    /// <summary>
    /// Puts a running job back at the front of the queue without spending an attempt: its
    /// worker is shutting down, not failing. A job someone cancelled ends Cancelled instead.
    /// False when <paramref name="workerId"/> no longer holds the job.
    /// </summary>
    public bool ReleaseJob(Guid id, string workerId, DateTimeOffset now)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                UPDATE nest_jobs SET
                    state = CASE cancelRequested WHEN 1 THEN 'Cancelled' ELSE 'Queued' END,
                    finishedAt = CASE cancelRequested WHEN 1 THEN $now ELSE NULL END,
                    package = CASE cancelRequested WHEN 1 THEN zeroblob(0) ELSE package END,
                    startedAt = CASE cancelRequested WHEN 1 THEN startedAt ELSE NULL END,
                    attempts = CASE cancelRequested WHEN 1 THEN attempts ELSE MAX(attempts - 1, 0) END,
                    workerId = NULL, leaseUntil = NULL, revision = revision + 1, {ClearProgress}
                WHERE id=$id AND state='Running' AND workerId=$worker
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$worker", workerId);
            command.Parameters.AddWithValue("$now", Stamp(now));
            return Changed(command.ExecuteNonQuery() == 1);
        }
    }

    private bool FinishJob(Guid id, string workerId, DateTimeOffset now, string set, params (string Name, object Value)[] values)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                UPDATE nest_jobs SET {set}, finishedAt=$now, workerId=NULL, leaseUntil=NULL,
                    plan=NULL, revision=revision+1
                WHERE id=$id AND state='Running' AND workerId=$worker
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$worker", workerId);
            command.Parameters.AddWithValue("$now", Stamp(now));
            foreach (var (name, value) in values)
                command.Parameters.AddWithValue(name, value);
            return Changed(command.ExecuteNonQuery() == 1);
        }
    }

    private bool Changed(bool changed)
    {
        if (changed)
            PulseJobs();
        return changed;
    }

    // A lapsed lease means the worker stopped: a cancel request ends the job, otherwise it goes
    // back to the queue until it has used its attempts.
    private bool SettleJobLeases(SqliteTransaction transaction, DateTimeOffset now)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE nest_jobs SET
                state = CASE WHEN cancelRequested=1 THEN 'Cancelled'
                             WHEN attempts >= $max THEN 'Failed' ELSE 'Queued' END,
                error = CASE WHEN cancelRequested=0 AND attempts >= $max
                             THEN 'The nest worker stopped responding ' || attempts || ' times while running this job.'
                             ELSE error END,
                finishedAt = CASE WHEN cancelRequested=1 OR attempts >= $max THEN $now ELSE NULL END,
                package = CASE WHEN cancelRequested=1 OR attempts >= $max THEN zeroblob(0) ELSE package END,
                startedAt = CASE WHEN cancelRequested=1 OR attempts >= $max THEN startedAt ELSE NULL END,
                workerId = NULL, leaseUntil = NULL, revision = revision + 1, {ClearProgress}
            WHERE state='Running' AND leaseUntil < $now;
            """;
        command.Parameters.AddWithValue("$max", NestJobRules.MaxAttempts);
        command.Parameters.AddWithValue("$now", Stamp(now));
        var settled = command.ExecuteNonQuery() > 0;
        return PurgeJobs(transaction, now) || settled;
    }

    // Retention: finished jobs go, package and result with them, JobRetention after they finish.
    private bool PurgeJobs(SqliteTransaction? transaction, DateTimeOffset now)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM nest_jobs WHERE finishedAt IS NOT NULL AND finishedAt < $expired";
        command.Parameters.AddWithValue("$expired", Stamp(now - JobRetention));
        return command.ExecuteNonQuery() > 0;
    }

    private static NestJobRecord ReadJob(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Title = reader.GetString(1),
        EngineName = reader.GetString(2),
        SubmittedBy = reader.GetString(3),
        State = Enum.Parse<NestJobState>(reader.GetString(4)),
        CancelRequested = reader.GetInt64(5) != 0,
        SubmittedAt = ParseStamp(reader.GetString(6)),
        StartedAt = reader.IsDBNull(7) ? null : ParseStamp(reader.GetString(7)),
        FinishedAt = reader.IsDBNull(8) ? null : ParseStamp(reader.GetString(8)),
        Attempts = reader.GetInt32(9),
        Error = reader.GetString(10),
        PackageSize = reader.GetInt64(11),
        Revision = reader.GetInt64(13),
        PlanRevision = reader.GetInt64(14),
        Progress = reader.GetString(15),
        NestId = reader.IsDBNull(16) ? null : Guid.Parse(reader.GetString(16)),
        Review = reader.GetString(17) is { Length: > 0 } review ? Enum.Parse<NestJobReview>(review) : NestJobReview.None,
        ReviewedBy = reader.GetString(18),
        ReviewedAt = reader.IsDBNull(19) ? null : ParseStamp(reader.GetString(19)),
        Summary = reader.IsDBNull(20) ? null : new NestJobSummary
        {
            PartsPlaced = reader.GetInt32(20),
            PartsUnplaced = reader.IsDBNull(21) ? 0 : reader.GetInt32(21),
            Sheets = reader.IsDBNull(22) ? 0 : reader.GetInt32(22),
            Utilization = reader.IsDBNull(23) ? 0 : reader.GetDouble(23),
        },
        QueuePosition = checked((int)reader.GetInt64(24)),
    };

    private static string Truncate(string? text)
    {
        text ??= "";
        return text.Length <= NestJobRules.MaxTextLength ? text : text[..NestJobRules.MaxTextLength];
    }

    private static string Stamp(DateTimeOffset time) => time.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseStamp(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
