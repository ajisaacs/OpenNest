namespace OpenNest.Data;

/// <summary>Where a server nest job is. Succeeded, Failed and Cancelled are final.</summary>
public enum NestJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>A whole-job nest the server's worker solves; see <c>docs/server-nesting.md</c>.</summary>
public sealed class NestJobRecord
{
    public Guid Id { get; set; }

    /// <summary>What the job is for, for people: usually the nest's name.</summary>
    public string Title { get; set; } = "";

    public string EngineName { get; set; } = "";

    public string SubmittedBy { get; set; } = "";

    public NestJobState State { get; set; }

    /// <summary>Someone asked to stop the job while it ran; the worker stops at its next heartbeat.</summary>
    public bool CancelRequested { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>How many times a worker has taken the job; a worker that stops responding gives it back.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the job failed; empty otherwise.</summary>
    public string Error { get; set; } = "";

    /// <summary>Queued jobs ahead of this one (zero for a job that is not queued).</summary>
    public int QueuePosition { get; set; }

    public long PackageSize { get; set; }

    public bool IsFinished => State is NestJobState.Succeeded or NestJobState.Failed or NestJobState.Cancelled;
}

/// <summary>The description sent with a job's package.</summary>
public sealed class NestJobSubmission
{
    public string Title { get; set; } = "";

    public string EngineName { get; set; } = "";

    public string SubmittedBy { get; set; } = "";
}

/// <summary>A worker's message about the job it holds.</summary>
public sealed class NestJobWorkerMessage
{
    public string WorkerId { get; set; } = "";

    /// <summary>Why the job failed, for <c>fail</c>; ignored otherwise.</summary>
    public string Error { get; set; } = "";
}

/// <summary>The server's answer to a worker's heartbeat.</summary>
public sealed class NestJobHeartbeat
{
    public bool CancelRequested { get; set; }
}

/// <summary>Limits and timings both sides of server nesting share.</summary>
public static class NestJobRules
{
    /// <summary>Largest job package (drawings and job) the server accepts.</summary>
    public const long MaxPackageBytes = 256L * 1024 * 1024;

    /// <summary>Largest result the server accepts from a worker.</summary>
    public const long MaxResultBytes = 64L * 1024 * 1024;

    public const int MaxTextLength = 500;

    /// <summary>How long a running job stays with its worker without a heartbeat.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>How often a worker reports; well inside <see cref="Lease"/>.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    /// <summary>Workers that stop responding this many times fail the job instead of retrying it.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Finished jobs, their packages and results are removed after this long.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
}
