using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms.Commands;

namespace OpenNest.Forms
{
    /// <summary>
    /// My Server Jobs (Database mode): the server nest jobs, newest first, kept current by
    /// polling the job list every three seconds; the list's ETag makes an unchanged poll a 304.
    /// Polling rather than the per-job event stream because the list must also show jobs other
    /// desktops submit and jobs removed or expired, which no one job's stream announces, and one
    /// conditional request covers every row. Open reopens a result that is ready to review (Keep
    /// or Discard as after a local run, kept at most once); Cancel Job stops a queued or running
    /// job; Remove deletes a finished job and its result from the server.
    /// See docs/server-nesting.md.
    /// </summary>
    internal partial class ServerJobsForm : Form
    {
        private readonly Func<NestJobRecord, Task> open;
        private readonly Func<RemoteNestJobs> connect;
        private readonly string user;
        private IReadOnlyList<NestJobRecord> jobs = Array.Empty<NestJobRecord>();
        private string etag;
        private bool refreshing;
        private bool refreshAgain;
        private bool busy;
        private System.Drawing.Font boldFont;

        /// <param name="open">Opens a result for review; the form refreshes when it completes.</param>
        /// <param name="connect">The server client (tests pass their own).</param>
        /// <param name="user">Whose jobs "Only my jobs" shows: this user on any workstation.</param>
        public ServerJobsForm(Func<NestJobRecord, Task> open, Func<RemoteNestJobs> connect = null, string user = null)
        {
            InitializeComponent();
            this.open = open ?? throw new ArgumentNullException(nameof(open));
            this.connect = connect ?? (() => new RemoteNestJobs(NestStorage.Settings.ServerUrl));
            this.user = user ?? Environment.UserName;
            showComboBox.SelectedIndex = 0;
            ServerNestDecision.JobsChanged += Decision_JobsChanged;
        }

        /// <summary>The jobs listed, newest first.</summary>
        internal IReadOnlyList<NestJobRecord> Jobs => jobs;

        internal NestJobRecord SelectedJob =>
            jobsListView.SelectedItems.Count == 1 ? jobsListView.SelectedItems[0].Tag as NestJobRecord : null;

        internal ListView JobsList => jobsListView;

        internal Button OpenButton => openButton;

        internal Button CancelJobButton => cancelJobButton;

        internal Button RemoveButton => removeButton;

        internal string StatusText => statusLabel.Text;

        /// <summary>Asks for confirmation before removing an unreviewed result; tests answer it.</summary>
        internal Func<string, bool> Confirm { get; set; }

        internal NestJobListFilter Filter => showComboBox.SelectedIndex switch
        {
            1 => NestJobListFilter.Active,
            2 => NestJobListFilter.Finished,
            3 => NestJobListFilter.Unreviewed,
            _ => NestJobListFilter.All,
        };

        internal void Select(Guid id)
        {
            foreach (ListViewItem item in jobsListView.Items)
                item.Selected = ((NestJobRecord)item.Tag).Id == id;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            pollTimer.Start();
            await RefreshJobsAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            pollTimer.Stop();
            ServerNestDecision.JobsChanged -= Decision_JobsChanged;
            base.OnFormClosed(e);
            boldFont?.Dispose();
        }

        /// <summary>Reads the list now; <paramref name="force"/> skips the unchanged-list shortcut.</summary>
        internal async Task RefreshJobsAsync(bool force = false)
        {
            if (force)
                etag = null;
            if (refreshing)
            {
                refreshAgain = true;
                return;
            }
            refreshing = true;
            try
            {
                do
                {
                    refreshAgain = false;
                    await ReadOnceAsync();
                }
                while (refreshAgain && !IsDisposed);
            }
            finally
            {
                refreshing = false;
            }
        }

        private async Task ReadOnceAsync()
        {
            NestJobList list;
            try
            {
                using var client = connect();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                list = await client.ListAsync(Filter, mineCheckBox.Checked ? user : null, 200, etag, timeout.Token);
            }
            catch (ServerTooOldException)
            {
                pollTimer.Stop();
                ShowStatus("This nest server cannot list jobs; it needs an update.");
                return;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                ShowStatus($"Waiting for the nest server: {ex.Message}");
                return;
            }
            if (IsDisposed)
                return;
            if (list != null)
            {
                etag = list.ETag;
                ShowJobs(list.Jobs);
            }
            ShowStatus(jobs.Count == 0 ? "No jobs." : $"Updated {DateTime.Now:T}");
        }

        private void ShowStatus(string text)
        {
            if (!IsDisposed)
                statusLabel.Text = text;
        }

        private void ShowJobs(IReadOnlyList<NestJobRecord> value)
        {
            var selected = SelectedJob?.Id;
            jobs = value;
            jobsListView.BeginUpdate();
            try
            {
                jobsListView.Items.Clear();
                foreach (var job in jobs)
                {
                    var item = new ListViewItem(new[]
                    {
                        ServerJobText.Title(job),
                        ServerJobText.State(job),
                        ServerJobText.Result(job),
                        job.EngineName,
                        job.SubmittedBy,
                        ServerJobText.Time(job.SubmittedAt),
                        ServerJobText.Time(job.FinishedAt),
                    })
                    {
                        Tag = job,
                        ToolTipText = ServerJobText.Detail(job),
                        Selected = job.Id == selected,
                    };
                    if (job.IsUnreviewed)
                        item.Font = boldFont ??= new System.Drawing.Font(jobsListView.Font, System.Drawing.FontStyle.Bold);
                    jobsListView.Items.Add(item);
                }
            }
            finally
            {
                jobsListView.EndUpdate();
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var job = SelectedJob;
            openButton.Enabled = !busy && job is { IsUnreviewed: true };
            cancelJobButton.Enabled = !busy && job is { IsFinished: false, CancelRequested: false };
            removeButton.Enabled = !busy && job is { IsFinished: true };
        }

        private async void PollTimer_Tick(object sender, EventArgs e)
        {
            if (!busy)
                await RefreshJobsAsync();
        }

        private async void Filter_Changed(object sender, EventArgs e)
        {
            if (IsHandleCreated)
                await RefreshJobsAsync(force: true);
        }

        private async void Decision_JobsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated)
                await RefreshJobsAsync(force: true);
        }

        private void JobsListView_SelectedIndexChanged(object sender, EventArgs e) => UpdateButtons();

        private void JobsListView_DoubleClick(object sender, EventArgs e)
        {
            if (openButton.Enabled)
                OpenButton_Click(sender, e);
        }

        private async void OpenButton_Click(object sender, EventArgs e)
        {
            if (SelectedJob is not { IsUnreviewed: true } job)
                return;
            await RunAsync(() => open(job));
        }

        private async void CancelJobButton_Click(object sender, EventArgs e)
        {
            if (SelectedJob is not { IsFinished: false } job)
                return;
            await RunAsync(async () =>
            {
                using var client = connect();
                if (await client.CancelAsync(job.Id) == null)
                    ShowStatus("The nest server no longer has that job.");
            });
        }

        private async void RemoveButton_Click(object sender, EventArgs e)
        {
            if (SelectedJob is not { IsFinished: true } job)
                return;
            if (job.IsUnreviewed)
            {
                var question = $"{ServerJobText.Title(job)} has a result nobody has kept yet. Remove it from the nest server anyway?";
                var confirmed = Confirm?.Invoke(question)
                    ?? MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                if (!confirmed)
                    return;
            }
            await RunAsync(async () =>
            {
                using var client = connect();
                await client.DeleteAsync(job);
                ServerNestDecision.RaiseJobsChanged();
            });
        }

        private async Task RunAsync(Func<Task> action)
        {
            busy = true;
            UpdateButtons();
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false;
                if (!IsDisposed)
                {
                    UpdateButtons();
                    await RefreshJobsAsync(force: true);
                }
            }
        }

        private void CloseButton_Click(object sender, EventArgs e) => Close();
    }

    /// <summary>How My Server Jobs and its status-bar item describe a job.</summary>
    internal static class ServerJobText
    {
        public static string Title(NestJobRecord job) =>
            string.IsNullOrWhiteSpace(job.Title) ? "(unnamed nest)" : job.Title;

        public static string State(NestJobRecord job) => job.State switch
        {
            NestJobState.Queued when job.QueuePosition == 0 => "Queued (next)",
            NestJobState.Queued => $"Queued ({job.QueuePosition} ahead)",
            NestJobState.Running when job.CancelRequested => "Stopping",
            NestJobState.Running => "Running",
            NestJobState.Succeeded => job.Review switch
            {
                NestJobReview.Kept => "Kept",
                NestJobReview.Discarded => "Discarded",
                _ => "Ready to review",
            },
            NestJobState.Failed => "Failed",
            _ => "Stopped",
        };

        public static string Result(NestJobRecord job)
        {
            if (job.State == NestJobState.Running)
                return ServerAutoNest.Describe(job);
            if (job.State == NestJobState.Failed)
                return job.Error;
            if (job.Summary is not { } summary)
                return "";
            var text = string.Format(CultureInfo.CurrentCulture, "{0} {1} on {2} {3}, {4:P1}",
                summary.PartsPlaced, summary.PartsPlaced == 1 ? "part" : "parts",
                summary.Sheets, summary.Sheets == 1 ? "sheet" : "sheets", summary.Utilization);
            return summary.PartsUnplaced > 0 ? $"{text}, {summary.PartsUnplaced} not placed" : text;
        }

        public static string Detail(NestJobRecord job)
        {
            var lines = new List<string> { $"{Title(job)}: {State(job)}" };
            if (job.Review != NestJobReview.None)
                lines.Add($"{job.Review}{(string.IsNullOrWhiteSpace(job.ReviewedBy) ? "" : " by " + job.ReviewedBy)}"
                    + (job.ReviewedAt is { } at ? $" on {Time(at)}" : ""));
            if (job.State == NestJobState.Failed && job.Error.Length > 0)
                lines.Add(job.Error);
            return string.Join(Environment.NewLine, lines);
        }

        public static string Time(DateTime? time) =>
            time is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "";

        /// <summary>The status-bar text for results waiting for review, or null when there are none.</summary>
        public static string Unreviewed(int count) => count switch
        {
            <= 0 => null,
            1 => "1 server nest to review",
            _ => $"{count} server nests to review",
        };
    }
}
