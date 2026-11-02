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
        "id, title, engineName, submittedBy, state, cancelRequested, submittedAt, startedAt, finishedAt, attempts, error, packageSize, seq, revision, planRevision, progress";

    public NestJobRecord InsertJob(Guid id, NestJobSubmission submission, byte[] package, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(package);
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO nest_jobs (id, seq, title, engineName, submittedBy, state, submittedAt, packageSize, package)
                VALUES ($id, (SELECT COALESCE(MAX(seq), 0) + 1 FROM nest_jobs), $title, $engine, $by, 'Queued', $now, $size, $package)
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$title", submission.Title);
            command.Parameters.AddWithValue("$engine", submission.EngineName);
            command.Parameters.AddWithValue("$by", submission.SubmittedBy);
            command.Parameters.AddWithValue("$now", Stamp(now));
            command.Parameters.AddWithValue("$size", package.LongLength);
            command.Parameters.AddWithValue("$package", package);
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
            if (!reader.Read())
                return null;
            var record = ReadJob(reader);
            if (record.State == NestJobState.Queued)
            {
                using var ahead = _connection.CreateCommand();
                ahead.CommandText = "SELECT COUNT(*) FROM nest_jobs WHERE state='Queued' AND seq < $seq";
                ahead.Parameters.AddWithValue("$seq", reader.GetInt64(12));
                record.QueuePosition = checked((int)(long)ahead.ExecuteScalar()!);
            }
            return record;
        }
    }

    /// <summary>The job's package while it is queued or running; finished jobs no longer keep it.</summary>
    public byte[]? GetJobPackage(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT package FROM nest_jobs WHERE id=$id AND state IN ('Queued', 'Running')";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() as byte[];
        }
    }

    /// <summary>A succeeded job's result; null for any other job.</summary>
    public string? GetJobResult(Guid id)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT result FROM nest_jobs WHERE id=$id AND state='Succeeded'";
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
    /// jobs past <see cref="NestJobRules.Retention"/> removed.
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
    public bool CompleteJob(Guid id, string workerId, string result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        return FinishJob(id, workerId, now, """
            state = CASE cancelRequested WHEN 1 THEN 'Cancelled' ELSE 'Succeeded' END,
            result = CASE cancelRequested WHEN 1 THEN NULL ELSE $result END
            """, ("$result", result));
    }

    public bool FailJob(Guid id, string workerId, string error, DateTimeOffset now) =>
        FinishJob(id, workerId, now, "state='Failed', error=$error", ("$error", Truncate(error)));

    /// <summary>The worker stopped the job after a cancel request.</summary>
    public bool ConfirmJobCancelled(Guid id, string workerId, DateTimeOffset now) =>
        FinishJob(id, workerId, now, "state='Cancelled'");

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
                UPDATE nest_jobs SET {set}, finishedAt=$now, workerId=NULL, leaseUntil=NULL, package=zeroblob(0),
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
            DELETE FROM nest_jobs WHERE finishedAt IS NOT NULL AND finishedAt < $expired;
            """;
        command.Parameters.AddWithValue("$max", NestJobRules.MaxAttempts);
        command.Parameters.AddWithValue("$now", Stamp(now));
        command.Parameters.AddWithValue("$expired", Stamp(now - NestJobRules.Retention));
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
