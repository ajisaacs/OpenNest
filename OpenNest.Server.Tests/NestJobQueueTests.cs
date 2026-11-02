using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// The server nesting queue's states and leases, driven with explicit times so lease expiry,
/// crash recovery and retention are tested without waiting.
/// </summary>
public class NestJobQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Package = { 1, 2, 3 };

    private static Guid Submit(NestDatabase db, string title = "N26-0001", DateTimeOffset? at = null)
    {
        var id = Guid.NewGuid();
        db.InsertJob(id, new NestJobSubmission { Title = title, EngineName = "Default", SubmittedBy = "aj@pc" }, Package, at ?? T0);
        return id;
    }

    private static (ServerFactory Factory, NestDatabase Db) Open()
    {
        var factory = new ServerFactory();
        return (factory, new NestDatabase(factory.DatabasePath, pooling: false));
    }

    [Fact]
    public void QueuedJobsReportHowManyAreAhead()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var first = Submit(db);
            var second = Submit(db, "N26-0002");

            var job = db.GetJob(second)!;
            Assert.Equal(NestJobState.Queued, job.State);
            Assert.Equal(1, job.QueuePosition);
            Assert.Equal(0, db.GetJob(first)!.QueuePosition);
            Assert.Equal("N26-0002", job.Title);
            Assert.Equal(3, job.PackageSize);
            Assert.Equal(Package, db.GetJobPackage(second));
            Assert.Null(db.GetJobResult(second));
        }
    }

    [Fact]
    public void OnlyOneJobRunsAtATime()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var first = Submit(db);
            var second = Submit(db, "second");

            var claimed = db.ClaimJob("w1", T0);
            Assert.Equal(first, claimed!.Id);
            Assert.Equal(NestJobState.Running, claimed.State);
            Assert.Equal(1, claimed.Attempts);
            Assert.Null(db.ClaimJob("w1", T0.AddSeconds(1)));
            Assert.Null(db.ClaimJob("w2", T0.AddSeconds(1)));
            Assert.Equal(0, db.GetJob(second)!.QueuePosition);

            Assert.True(db.CompleteJob(first, "w1", "{\"solves\":[]}", T0.AddMinutes(1)));
            Assert.Equal(second, db.ClaimJob("w2", T0.AddMinutes(1))!.Id);
        }
    }

    [Fact]
    public void ConcurrentClaimsGiveTheJobToOneWorker()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db);
            Submit(db, "second");

            var claims = Enumerable.Range(0, 16).AsParallel()
                .Select(i => db.ClaimJob($"w{i}", T0))
                .Where(job => job is not null)
                .ToList();

            Assert.Single(claims);
        }
    }

    [Fact]
    public void CompletedJobKeepsItsResultAndPackageUntilReviewed()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("w1", T0);

            Assert.False(db.CompleteJob(id, "someone-else", "{}", T0));
            Assert.True(db.CompleteJob(id, "w1", "{\"version\":1}", T0.AddMinutes(3)));

            var job = db.GetJob(id)!;
            Assert.Equal(NestJobState.Succeeded, job.State);
            Assert.Equal(T0.AddMinutes(3).UtcDateTime, job.FinishedAt);
            Assert.Equal("{\"version\":1}", db.GetJobResult(id));
            // A desktop reopening the result later rebuilds the job from its package.
            Assert.Equal(Package, db.GetJobPackage(id));
            Assert.Null(db.HeartbeatJob(id, "w1", T0.AddMinutes(4)));

            Assert.True(db.ReviewJob(id, NestJobReview.Kept, "aj@pc", job.Revision, T0.AddMinutes(5))!.Value.Changed);
            Assert.Null(db.GetJobPackage(id));
            Assert.Null(db.GetJobResult(id));
        }
    }

    [Fact]
    public void CancellingAQueuedJobEndsItAtOnce()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);

            var job = db.CancelJob(id, T0)!;

            Assert.Equal(NestJobState.Cancelled, job.State);
            Assert.Null(db.ClaimJob("w1", T0));
            Assert.Null(db.CancelJob(Guid.NewGuid(), T0));
        }
    }

    [Fact]
    public void CancellingARunningJobStopsItAtTheNextHeartbeat()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("w1", T0);
            Assert.False(db.HeartbeatJob(id, "w1", T0.AddSeconds(20))!.CancelRequested);

            var job = db.CancelJob(id, T0.AddSeconds(30))!;
            Assert.Equal(NestJobState.Running, job.State);
            Assert.True(job.CancelRequested);
            Assert.True(db.HeartbeatJob(id, "w1", T0.AddSeconds(40))!.CancelRequested);

            Assert.True(db.ConfirmJobCancelled(id, "w1", T0.AddSeconds(41)));
            Assert.Equal(NestJobState.Cancelled, db.GetJob(id)!.State);
        }
    }

    [Fact]
    public void AResultArrivingAfterACancelRequestIsDropped()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("w1", T0);
            db.CancelJob(id, T0.AddSeconds(5));

            Assert.True(db.CompleteJob(id, "w1", "{}", T0.AddSeconds(6),
                new NestJobSummary { PartsPlaced = 4, Sheets = 1, Utilization = 0.5 }));

            Assert.Equal(NestJobState.Cancelled, db.GetJob(id)!.State);
            Assert.Null(db.GetJobResult(id));
            Assert.Null(db.GetJob(id)!.Summary);
            Assert.Null(db.GetJobPackage(id));
        }
    }

    [Fact]
    public void AJobWhoseWorkerStoppedRespondingGoesBackToTheQueue()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("crashed", T0);

            // Inside the lease the job stays with its worker.
            Assert.Null(db.ClaimJob("w2", T0 + NestJobRules.Lease - TimeSpan.FromSeconds(1)));

            var retried = db.ClaimJob("w2", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(1));

            Assert.Equal(id, retried!.Id);
            Assert.Equal(2, retried.Attempts);
            Assert.Null(db.HeartbeatJob(id, "crashed", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(2)));
            Assert.False(db.CompleteJob(id, "crashed", "{}", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(2)));
            Assert.True(db.CompleteJob(id, "w2", "{}", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public void HeartbeatsKeepALongJobWithItsWorker()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("w1", T0);
            var now = T0;
            for (var i = 0; i < 30; i++)
            {
                now += NestJobRules.HeartbeatInterval;
                Assert.NotNull(db.HeartbeatJob(id, "w1", now));
                Assert.Null(db.ClaimJob("w2", now));
            }
            Assert.Equal(1, db.GetJob(id)!.Attempts);
        }
    }

    [Fact]
    public void AJobThatKeepsStoppingItsWorkerFails()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            var now = T0;
            for (var attempt = 1; attempt <= NestJobRules.MaxAttempts; attempt++)
            {
                Assert.Equal(attempt, db.ClaimJob($"w{attempt}", now)!.Attempts);
                now += NestJobRules.Lease + TimeSpan.FromSeconds(1);
            }

            Assert.Null(db.ClaimJob("next", now));

            var job = db.GetJob(id)!;
            Assert.Equal(NestJobState.Failed, job.State);
            Assert.Contains($"{NestJobRules.MaxAttempts} times", job.Error);
            Assert.Null(db.GetJobPackage(id));
        }
    }

    [Fact]
    public void ACancelRequestedJobWhoseWorkerStoppedEndsCancelled()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("crashed", T0);
            db.CancelJob(id, T0.AddSeconds(1));

            Assert.Null(db.ClaimJob("w2", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(1)));
            Assert.Equal(NestJobState.Cancelled, db.GetJob(id)!.State);
        }
    }

    [Fact]
    public void AReleasedJobGoesBackWithoutSpendingAnAttempt()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("stopping", T0);

            Assert.True(db.ReleaseJob(id, "stopping", T0.AddSeconds(5)));
            var job = db.GetJob(id)!;
            Assert.Equal(NestJobState.Queued, job.State);
            Assert.Equal(0, job.Attempts);
            Assert.Null(job.StartedAt);
            Assert.Equal(1, db.ClaimJob("w2", T0.AddSeconds(6))!.Attempts);
        }
    }

    [Fact]
    public void FailedJobKeepsItsError()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var id = Submit(db);
            db.ClaimJob("w1", T0);

            Assert.True(db.FailJob(id, "w1", new string('x', 2000), T0.AddSeconds(1)));

            var job = db.GetJob(id)!;
            Assert.Equal(NestJobState.Failed, job.State);
            Assert.Equal(NestJobRules.MaxTextLength, job.Error.Length);
        }
    }

    [Fact]
    public void FinishedJobsAreRemovedAfterTheRetention()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var old = Submit(db);
            db.CancelJob(old, T0);
            var waiting = Submit(db, "waiting", T0);

            db.ClaimJob("w1", T0 + NestJobRules.Retention + TimeSpan.FromMinutes(1));

            Assert.Null(db.GetJob(old));
            Assert.Equal(NestJobState.Running, db.GetJob(waiting)!.State);
        }
    }

    [Fact]
    public void JobsSurviveARestart()
    {
        using var factory = new ServerFactory();
        Guid id;
        using (var db = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            id = Submit(db);
            db.ClaimJob("w1", T0);
        }

        using (var db = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            Assert.Equal(NestJobState.Running, db.GetJob(id)!.State);
            Assert.Equal(id, db.ClaimJob("w2", T0 + NestJobRules.Lease + TimeSpan.FromSeconds(1))!.Id);
        }
    }
}
