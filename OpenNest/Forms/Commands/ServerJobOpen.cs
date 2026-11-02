using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Api;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Reopens a finished server job's result from My Server Jobs, in this session or a later
    /// one: the job is rebuilt from the package it was sent as, bound to the target nest's own
    /// drawings (<see cref="RemoteNestSubmission.Reopen"/>, which refuses drawings changed since
    /// it was sent), replayed here and reviewed exactly as the session that sent it would have:
    /// validation review, Keep or Discard, the Keep recorded on the server before the commit.
    /// The caller owns the busy guard, the nesting lockout and the cancellation source.
    /// See docs/server-nesting.md.
    /// </summary>
    internal static class ServerJobOpen
    {
        private const string Caption = "Open Server Nest";

        /// <summary>Returns the plates kept, or null when nothing was.</summary>
        internal static async Task<IReadOnlyList<Plate>> RunAsync(IWin32Window owner, EditNestForm target, NestJobRecord job,
            CancellationTokenSource cts)
        {
            using var jobs = new RemoteNestJobs(NestStorage.Settings.ServerUrl);
            try
            {
                using var progressForm = new NestProgressForm(cts, showPlateRow: false)
                {
                    AllowAccept = false,
                    HoldOpenUntilCompleted = true,
                };
                var load = LoadAsync(jobs, target, job, progressForm, cts.Token);
                // Owned modal progress holds the drawings stable through the download and replay.
                if (!load.IsCompleted)
                    progressForm.ShowDialog(owner);
                var (result, current) = await load;
                cts.Token.ThrowIfCancellationRequested();
                return await AutoNestCommand.ReviewAndCommitAsync(owner, target, result, new ServerNestDecision(current),
                    applying: null, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                or InvalidOperationException or FormatException)
            {
                // A changed drawing (NestReplayMismatchException) leaves the job unreviewed, so it
                // can be opened again once the drawing is back as it was sent.
                MessageBox.Show(owner, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            finally
            {
                ServerNestDecision.RaiseJobsChanged();
            }
        }

        private static async Task<(NestPipelineResult, NestJobRecord)> LoadAsync(RemoteNestJobs jobs, EditNestForm target,
            NestJobRecord job, NestProgressForm progressForm, CancellationToken token)
        {
            void Show(string text)
            {
                if (!progressForm.IsDisposed)
                    progressForm.UpdateProgress(new NestProgress { Phase = NestPhase.Custom, Description = text });
            }

            try
            {
                Show("Reading the result from the nest server");
                var current = await jobs.GetAsync(job.Id, token)
                    ?? throw new InvalidOperationException("The nest server no longer has this job.");
                if (!current.IsUnreviewed)
                    throw new InvalidOperationException(current.Review == NestJobReview.None
                        ? $"This job has no result to review: it {Describe(current.State)}."
                        : new NestJobConflictException(current).Message);
                var package = await jobs.GetPackageAsync(job.Id, token);
                var transcript = await jobs.GetResultAsync(job.Id, token);
                Show("Checking the server's nest on this PC");
                var drawings = new List<Drawing>(target.Nest.Drawings);
                var result = await Task.Run(() =>
                {
                    var submission = RemoteNestSubmission.Reopen(NestJobPackage.ReadDocument(package), drawings);
                    return submission.Complete(NestSolveTranscript.FromJson(transcript), token);
                }, token);
                return (result, current);
            }
            finally
            {
                progressForm.ShowCompleted();
                progressForm.Close();
            }
        }

        private static string Describe(NestJobState state) => state switch
        {
            NestJobState.Queued => "is still queued",
            NestJobState.Running => "is still running",
            NestJobState.Failed => "failed",
            _ => "was stopped",
        };
    }
}
