using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// My Server Jobs on the database: list filters and queue positions, Keep and Discard each
/// happening once per result, removal, the worker's summary and configurable retention.
/// </summary>
public class NestJobListTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Package = { 1, 2, 3 };

    private static Guid Submit(NestDatabase db, string title, string by = "aj@pc1", DateTimeOffset? at = null, Guid? nestId = null)
    {
        var id = Guid.NewGuid();
        db.InsertJob(id, new NestJobSubmission { Title = title, EngineName = "Default", SubmittedBy = by, NestId = nestId }, Package, at ?? T0);
        return id;
    }

    // Claims the oldest queued job and completes it.
    private static Guid Finish(NestDatabase db, DateTimeOffset at, NestJobSummary? summary = null)
    {
        var job = db.ClaimJob("w1", at)!;
        Assert.True(db.CompleteJob(job.Id, "w1", "{}", at.AddMinutes(1), summary));
        return job.Id;
    }

    private static (ServerFactory Factory, NestDatabase Db) Open()
    {
        var factory = new ServerFactory();
        return (factory, new NestDatabase(factory.DatabasePath, pooling: false));
    }

    private static IEnumerable<string> Titles(IEnumerable<NestJobRecord> jobs) => jobs.Select(j => j.Title);

    [Fact]
    public void TheListFiltersByStateAndSubmitter()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "done", "AJ@pc1");
            Finish(db, T0);
            Submit(db, "running", "aj@pc2");
            db.ClaimJob("w1", T0.AddMinutes(2));
            Submit(db, "queued", "bob@pc3");
            Submit(db, "plain", "aj");
            Submit(db, "lookalike", "ajax@pc4");

            var now = T0.AddMinutes(3);
            Assert.Equal(new[] { "lookalike", "plain", "queued", "running", "done" },
                Titles(db.ListJobs(NestJobListFilter.All, null, 100, now)));
            Assert.Equal(new[] { "lookalike", "plain", "queued", "running" },
                Titles(db.ListJobs(NestJobListFilter.Active, null, 100, now)));
            Assert.Equal(new[] { "done" }, Titles(db.ListJobs(NestJobListFilter.Finished, null, 100, now)));
            Assert.Equal(new[] { "done" }, Titles(db.ListJobs(NestJobListFilter.Unreviewed, null, 100, now)));

            // A bare user name is that user on any workstation, ignoring case; a full one is one workstation.
            Assert.Equal(new[] { "plain", "running", "done" }, Titles(db.ListJobs(NestJobListFilter.All, "aj", 100, now)));
            Assert.Equal(new[] { "running" }, Titles(db.ListJobs(NestJobListFilter.All, "AJ@PC2", 100, now)));
            Assert.Equal(new[] { "running" }, Titles(db.ListJobs(NestJobListFilter.Active, "aj@pc2", 100, now)));
            Assert.Equal(new[] { "lookalike", "plain" }, Titles(db.ListJobs(NestJobListFilter.All, null, 2, now)));
        }
    }

    [Fact]
    public void ListedQueuedJobsShowTheirPlaceInTheQueue()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "a");
            Submit(db, "b");
            Submit(db, "c");
            db.ClaimJob("w1", T0);

            var jobs = db.ListJobs(NestJobListFilter.All, null, 100, T0).ToDictionary(j => j.Title);
            Assert.Equal(NestJobState.Running, jobs["a"].State);
            Assert.Equal(0, jobs["a"].QueuePosition);
            Assert.Equal(0, jobs["b"].QueuePosition);
            Assert.Equal(1, jobs["c"].QueuePosition);
            Assert.Equal(1, db.GetJob(jobs["c"].Id)!.QueuePosition);

            db.CancelJob(jobs["b"].Id, T0);
            Assert.Equal(0, db.ListJobs(NestJobListFilter.Active, null, 100, T0).Single(j => j.Title == "c").QueuePosition);
        }
    }

    [Fact]
    public void FinishedJobsCarryTheWorkersSummaryAndTheirNest()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            var nest = Guid.NewGuid();
            Submit(db, "with", nestId: nest);
            var with = Finish(db, T0, new NestJobSummary { PartsPlaced = 23, PartsUnplaced = 2, Sheets = 3, Utilization = 0.784 });
            Submit(db, "without");
            var without = Finish(db, T0.AddMinutes(2));

            var job = db.GetJob(with)!;
            Assert.Equal(nest, job.NestId);
            Assert.Equal(23, job.Summary!.PartsPlaced);
            Assert.Equal(2, job.Summary.PartsUnplaced);
            Assert.Equal(3, job.Summary.Sheets);
            Assert.Equal(0.784, job.Summary.Utilization, 6);
            Assert.True(job.IsUnreviewed);
            Assert.Null(db.GetJob(without)!.Summary);
            Assert.Null(db.GetJob(without)!.NestId);
        }
    }

    [Fact]
    public void AResultIsKeptOnlyOnce()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "n");
            var id = Finish(db, T0);
            var seen = db.GetJob(id)!;

            // Two desktops reviewed the same revision; the first Keep wins.
            var first = db.ReviewJob(id, NestJobReview.Kept, "aj@pc1", seen.Revision, T0.AddMinutes(5))!.Value;
            Assert.True(first.Changed);
            Assert.Equal(NestJobReview.Kept, first.Job.Review);
            Assert.Equal("aj@pc1", first.Job.ReviewedBy);
            Assert.Equal(T0.AddMinutes(5).UtcDateTime, first.Job.ReviewedAt);
            Assert.False(first.Job.IsUnreviewed);
            Assert.True(first.Job.Revision > seen.Revision);

            var second = db.ReviewJob(id, NestJobReview.Kept, "bob@pc2", seen.Revision, T0.AddMinutes(6))!.Value;
            Assert.False(second.Changed);
            Assert.Equal("aj@pc1", second.Job.ReviewedBy);
            // Even with the new revision a kept result cannot be kept or discarded again.
            Assert.False(db.ReviewJob(id, NestJobReview.Kept, "bob@pc2", first.Job.Revision, T0)!.Value.Changed);
            Assert.False(db.ReviewJob(id, NestJobReview.Discarded, "bob@pc2", first.Job.Revision, T0)!.Value.Changed);
            Assert.Empty(db.ListJobs(NestJobListFilter.Unreviewed, null, 100, T0));
            Assert.Null(db.ReviewJob(Guid.NewGuid(), NestJobReview.Kept, "", 1, T0));
        }
    }

    [Fact]
    public void ConcurrentKeepsLetOneDesktopKeep()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "n");
            var id = Finish(db, T0);
            var revision = db.GetJob(id)!.Revision;

            var kept = Enumerable.Range(0, 16).AsParallel()
                .Select(i => db.ReviewJob(id, NestJobReview.Kept, $"pc{i}", revision, T0)!.Value.Changed)
                .Count(changed => changed);
            Assert.Equal(1, kept);
        }
    }

    [Fact]
    public void ADiscardedResultCannotBeKeptAndOnlySucceededJobsAreReviewed()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "n");
            var id = Finish(db, T0);
            var revision = db.GetJob(id)!.Revision;
            Assert.True(db.ReviewJob(id, NestJobReview.Discarded, "aj@pc1", revision, T0)!.Value.Changed);
            var discarded = db.GetJob(id)!;
            Assert.Equal(NestJobReview.Discarded, discarded.Review);
            Assert.False(db.ReviewJob(id, NestJobReview.Kept, "aj@pc1", discarded.Revision, T0)!.Value.Changed);

            var queued = Submit(db, "q");
            Assert.False(db.ReviewJob(queued, NestJobReview.Kept, "aj@pc1", db.GetJob(queued)!.Revision, T0)!.Value.Changed);
            Assert.Throws<ArgumentOutOfRangeException>(() => db.ReviewJob(queued, NestJobReview.None, "", 1, T0));
        }
    }

    [Fact]
    public void OnlyFinishedJobsAtTheirRevisionAreRemoved()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            Submit(db, "n");
            var done = Finish(db, T0);
            var queued = Submit(db, "q");

            var stale = db.DeleteJob(done, db.GetJob(done)!.Revision - 1)!.Value;
            Assert.False(stale.Removed);
            Assert.NotNull(stale.Job);
            Assert.False(db.DeleteJob(queued, db.GetJob(queued)!.Revision)!.Value.Removed);

            Assert.True(db.DeleteJob(done, db.GetJob(done)!.Revision)!.Value.Removed);
            Assert.Null(db.GetJob(done));
            Assert.Null(db.DeleteJob(done, 1));
        }
    }

    [Fact]
    public void RetentionIsConfigurableAndAppliedWhenListing()
    {
        var (factory, db) = Open();
        using (factory) using (db)
        {
            db.JobRetention = TimeSpan.FromDays(2);
            Submit(db, "old");
            var old = Finish(db, T0);
            Submit(db, "recent", at: T0.AddDays(1));
            var recent = Finish(db, T0.AddDays(1));
            Submit(db, "waiting");

            var jobs = db.ListJobs(NestJobListFilter.All, null, 100, T0.AddDays(2).AddMinutes(2));
            Assert.Equal(new[] { "waiting", "recent" }, Titles(jobs));
            Assert.Null(db.GetJob(old));
            Assert.NotNull(db.GetJob(recent));
        }
    }

    [Fact]
    public void RetentionReadsDaysFromTheEnvironment()
    {
        Assert.Equal(NestJobRules.Retention, ServerOptions.JobRetention(null));
        Assert.Equal(NestJobRules.Retention, ServerOptions.JobRetention(" "));
        Assert.Equal(TimeSpan.FromDays(30), ServerOptions.JobRetention("30"));
        Assert.Equal(TimeSpan.FromHours(12), ServerOptions.JobRetention("0.5"));
        Assert.Throws<ArgumentException>(() => ServerOptions.JobRetention("0"));
        Assert.Throws<ArgumentException>(() => ServerOptions.JobRetention("-3"));
        Assert.Throws<ArgumentException>(() => ServerOptions.JobRetention("week"));
    }

    [Fact]
    public void AVersion16DatabaseGainsTheReviewColumns()
    {
        using var factory = new ServerFactory();
        Guid id;
        using (var db = new NestDatabase(factory.DatabasePath, pooling: false))
            id = Submit(db, "before");
        using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX ix_nest_jobs_seq;
                ALTER TABLE nest_jobs DROP COLUMN nestId; ALTER TABLE nest_jobs DROP COLUMN review;
                ALTER TABLE nest_jobs DROP COLUMN reviewedBy; ALTER TABLE nest_jobs DROP COLUMN reviewedAt;
                ALTER TABLE nest_jobs DROP COLUMN partsPlaced; ALTER TABLE nest_jobs DROP COLUMN partsUnplaced;
                ALTER TABLE nest_jobs DROP COLUMN sheets; ALTER TABLE nest_jobs DROP COLUMN utilization;
                PRAGMA user_version=16;
                """;
            command.ExecuteNonQuery();
        }

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);
        var job = upgraded.GetJob(id)!;
        Assert.Equal("before", job.Title);
        Assert.Equal(NestJobReview.None, job.Review);
        Assert.Null(job.Summary);
        Assert.Single(upgraded.ListJobs(NestJobListFilter.Active, "aj", 10, T0));
    }
}
