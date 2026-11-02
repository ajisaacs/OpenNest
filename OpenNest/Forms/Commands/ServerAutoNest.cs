using System;
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
    /// Auto Nest solved by the nest server's worker (Database mode): the job is sent as a
    /// package, its state polled behind the same owned modal progress a local run uses, and the
    /// server's engine answers replayed here (<see cref="RemoteNestSubmission"/>), so validation,
    /// the review and Keep are exactly a local run's. Stop cancels the server job and discards
    /// the proposal. When the server cannot take the job, the user may nest on this PC instead.
    /// See docs/server-nesting.md.
    /// </summary>
    internal static class ServerAutoNest
    {
        private const string Caption = "Auto Nest";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        internal static async Task<NestPipelineResult> SolveAsync(IWin32Window owner, EditNestForm target,
            NestPipelineRequest request, CancellationTokenSource cts)
        {
            var submission = new RemoteNestSubmission(request);
            using var jobs = new RemoteNestJobs(NestStorage.Settings.ServerUrl);
            NestJobRecord job;
            try
            {
                var package = await Task.Run(() => NestJobPackage.Write(request, target.Nest.Units), cts.Token);
                job = await jobs.SubmitAsync(new NestJobSubmission
                {
                    Title = target.Nest.Name ?? "",
                    EngineName = request.EngineName,
                    SubmittedBy = $"{Environment.UserName}@{Environment.MachineName}",
                }, package, cts.Token);
            }
            catch (Exception ex) when (!cts.IsCancellationRequested
                && ex is IOException or HttpRequestException or TaskCanceledException)
            {
                var choice = MessageBox.Show(owner,
                    $"The nest server could not take this nest:\n\n{ex.Message}\n\nNest on this PC instead?",
                    Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes)
                    return await AutoNestCommand.SolveAsync(owner, target, request, cts);
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }

            using var progressForm = new NestProgressForm(cts, showPlateRow: false)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            var wait = WaitAsync(jobs, job, submission, progressForm, cts);
            if (!wait.IsCompleted)
                progressForm.ShowDialog(owner);
            return await wait;
        }

        private static async Task<NestPipelineResult> WaitAsync(RemoteNestJobs jobs, NestJobRecord job,
            RemoteNestSubmission submission, NestProgressForm progressForm, CancellationTokenSource cts)
        {
            string problem = null;
            try
            {
                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    Show(progressForm, problem ?? Describe(job));
                    switch (job.State)
                    {
                        case NestJobState.Succeeded:
                            Show(progressForm, "Checking the server's nest on this PC");
                            var transcript = NestSolveTranscript.FromJson(await jobs.GetResultAsync(job.Id, cts.Token));
                            return await Task.Run(() => submission.Complete(transcript, cts.Token), cts.Token);
                        case NestJobState.Failed:
                            throw new InvalidOperationException($"The nest server could not nest this job: {job.Error}");
                        case NestJobState.Cancelled:
                            // Stopped from elsewhere: nothing to keep, as after Stop here.
                            cts.Cancel();
                            cts.Token.ThrowIfCancellationRequested();
                            break;
                    }

                    await Task.Delay(PollInterval, cts.Token);
                    try
                    {
                        job = await jobs.GetAsync(job.Id, cts.Token)
                            ?? throw new InvalidOperationException("The nest server no longer has this job.");
                        problem = null;
                    }
                    catch (Exception ex) when (!cts.IsCancellationRequested
                        && ex is IOException or HttpRequestException or TaskCanceledException)
                    {
                        // The job keeps running on the server; keep asking.
                        problem = $"Waiting for the nest server: {ex.Message}";
                    }
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Best effort: a job left running finishes on the server and is never kept.
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await jobs.CancelAsync(job.Id, timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) { }
                throw;
            }
            finally
            {
                progressForm.ShowCompleted();
                progressForm.Close();
            }
        }

        private static string Describe(NestJobRecord job) => job.State switch
        {
            NestJobState.Queued when job.QueuePosition > 0 =>
                $"Queued on the nest server, {job.QueuePosition} {(job.QueuePosition == 1 ? "job" : "jobs")} ahead",
            NestJobState.Queued => "Queued on the nest server",
            NestJobState.Running when job.CancelRequested => "Stopping on the nest server",
            NestJobState.Running => "Nesting on the nest server",
            _ => job.State.ToString(),
        };

        private static void Show(NestProgressForm form, string text)
        {
            if (!form.IsDisposed)
                form.UpdateProgress(new NestProgress { Description = text });
        }
    }
}
