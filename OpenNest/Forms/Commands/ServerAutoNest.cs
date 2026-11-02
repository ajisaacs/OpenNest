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
    /// package and followed through its event stream (polling when the stream drops) behind the
    /// same owned modal progress a local run uses, with the worker's best plan so far drawn in
    /// the same strip. The server's engine answers are then replayed here
    /// (<see cref="RemoteNestSubmission"/>), so validation, the review and Keep are exactly a
    /// local run's. Stop cancels the server job and discards the proposal. A job that ends while
    /// OpenNest is not in front shows a notification (<see cref="ServerNestNotice"/>). When the
    /// server cannot take the job, the user may nest on this PC instead.
    /// See docs/server-nesting.md.
    /// </summary>
    internal static class ServerAutoNest
    {
        private const string Caption = "Auto Nest";

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
            progressForm.ShowPlanStrip(target.PlateView.ColorScheme);
            var wait = WaitAsync(owner, jobs, job, submission, progressForm, cts);
            if (!wait.IsCompleted)
                progressForm.ShowDialog(owner);
            return await wait;
        }

        private static async Task<NestPipelineResult> WaitAsync(IWin32Window owner, RemoteNestJobs jobs, NestJobRecord job,
            RemoteNestSubmission submission, NestProgressForm progressForm, CancellationTokenSource cts)
        {
            var view = new LiveView(progressForm, submission.Request, cts.Token);
            var watch = new NestJobWatch(jobs)
            {
                Job = new Progress<NestJobRecord>(view.ShowJob),
                Plan = new Progress<string>(view.ShowPlan),
                Problem = new Progress<string>(view.ShowProblem),
            };
            try
            {
                job = await watch.RunAsync(job, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                switch (job.State)
                {
                    case NestJobState.Succeeded:
                        ServerNestNotice.ShowIfAway(owner, "Server nest finished",
                            $"{Title(job)} is ready to review. Click to open OpenNest.");
                        view.Show("Checking the server's nest on this PC");
                        var transcript = NestSolveTranscript.FromJson(await jobs.GetResultAsync(job.Id, cts.Token));
                        return await Task.Run(() => submission.Complete(transcript, cts.Token), cts.Token);
                    case NestJobState.Failed:
                        ServerNestNotice.ShowIfAway(owner, "Server nest failed",
                            $"The nest server could not nest {Title(job)}. Click to open OpenNest.");
                        throw new InvalidOperationException($"The nest server could not nest this job: {job.Error}");
                    default:
                        // Stopped from elsewhere: nothing to keep, as after Stop here.
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Best effort: the worker sees the request within one report interval. A job
                // left running finishes on the server and is never kept.
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
                view.Stop();
                progressForm.ShowCompleted();
                progressForm.Close();
            }
        }

        private static string Title(NestJobRecord job) =>
            string.IsNullOrWhiteSpace(job.Title) ? "Your nest" : job.Title;

        private static string Describe(NestJobRecord job) => job.State switch
        {
            NestJobState.Queued when job.QueuePosition > 0 =>
                $"Queued on the nest server, {job.QueuePosition} {(job.QueuePosition == 1 ? "job" : "jobs")} ahead",
            NestJobState.Queued => "Queued on the nest server",
            NestJobState.Running when job.CancelRequested => "Stopping on the nest server",
            NestJobState.Running when RemoteNestProgress.FromJson(job.Progress) is { } progress =>
                $"On the nest server: {JobEngineNest.Describe(job.EngineName, progress.ToProgress())}",
            NestJobState.Running => "Nesting on the nest server",
            _ => job.State.ToString(),
        };

        /// <summary>
        /// What the progress window shows of a server job, on the UI thread: its state or the
        /// worker's stage (or why updates are late), and its best plan so far in the strip.
        /// Plans are drawn off the UI thread, newest only: a plan that arrives while one is
        /// drawn replaces any still waiting.
        /// </summary>
        internal sealed class LiveView
        {
            private readonly NestProgressForm form;
            private readonly NestPipelineRequest request;
            private readonly CancellationToken token;
            private RemoteNestPlanPreview preview;
            private NestJobRecord job;
            private string problem = "";
            private string waitingPlan;
            private bool drawing;
            private bool stopped;

            public LiveView(NestProgressForm form, NestPipelineRequest request, CancellationToken token)
            {
                this.form = form;
                this.request = request;
                this.token = token;
            }

            /// <summary>Completes when no plan is being drawn; tests wait on it.</summary>
            internal Task Drawn { get; private set; } = Task.CompletedTask;

            public void ShowJob(NestJobRecord value)
            {
                job = value;
                Refresh();
            }

            public void ShowProblem(string value)
            {
                problem = value ?? "";
                Refresh();
            }

            public void ShowPlan(string json)
            {
                if (stopped)
                    return;
                waitingPlan = json;
                if (!drawing)
                    Drawn = DrawAsync();
            }

            public void Show(string text)
            {
                if (!form.IsDisposed)
                    form.UpdateProgress(new NestProgress { Phase = NestPhase.Custom, Description = text });
            }

            /// <summary>Late updates (posted before the run ended) are ignored.</summary>
            public void Stop() => stopped = true;

            private void Refresh()
            {
                if (!stopped && job != null)
                    Show(problem.Length > 0 ? problem : Describe(job));
            }

            private async Task DrawAsync()
            {
                drawing = true;
                try
                {
                    while (waitingPlan != null && !stopped)
                    {
                        var json = waitingPlan;
                        waitingPlan = null;
                        var sheets = await Task.Run(() =>
                        {
                            preview ??= new RemoteNestPlanPreview(request, token);
                            return preview.Sheets(json, token);
                        }, token);
                        if (sheets != null && !stopped && !form.IsDisposed)
                            form.UpdatePlan(sheets);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                finally
                {
                    drawing = false;
                }
            }
        }
    }
}
