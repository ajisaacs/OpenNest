using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms.Commands;

namespace OpenNest.Forms
{
    /// <summary>
    /// The status-bar item that counts this user's server nest results waiting for review (in
    /// Database mode), including results that finished while OpenNest was closed. It reads the
    /// server at startup, every minute and whenever a job changes from this desktop; clicking it
    /// opens My Server Jobs. Nothing pops up. Hidden when there is nothing to review or the
    /// server cannot be reached.
    /// </summary>
    internal sealed class ServerJobsStatus : IDisposable
    {
        private readonly Func<bool> enabled;
        private readonly Func<RemoteNestJobs> connect;
        private readonly string user;
        private readonly System.Windows.Forms.Timer timer;
        private bool refreshing;
        private bool disposed;

        public ServerJobsStatus(StatusStrip statusStrip, int index, Action open, Font baseFont,
            Func<bool> enabled = null, Func<RemoteNestJobs> connect = null, string user = null)
        {
            ArgumentNullException.ThrowIfNull(open);
            this.enabled = enabled ?? (() => NestStorage.Settings.IsDatabaseMode);
            this.connect = connect ?? (() => new RemoteNestJobs(NestStorage.Settings.ServerUrl));
            this.user = user ?? Environment.UserName;
            Label = new ToolStripStatusLabel
            {
                Visible = false,
                IsLink = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font(baseFont, FontStyle.Bold),
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                ToolTipText = "Server Auto Nest results nobody has kept or discarded yet. Click to open My Server Jobs.",
            };
            Label.Click += (s, e) => open();
            statusStrip.Items.Insert(System.Math.Clamp(index, 0, statusStrip.Items.Count), Label);
            timer = new System.Windows.Forms.Timer { Interval = 60_000 };
            timer.Tick += async (s, e) => await RefreshAsync();
            ServerNestDecision.JobsChanged += Decision_JobsChanged;
        }

        internal ToolStripStatusLabel Label { get; }

        public void Start()
        {
            timer.Start();
            _ = RefreshAsync();
        }

        /// <summary>Counts the results waiting for review now.</summary>
        public async Task RefreshAsync()
        {
            if (disposed || refreshing)
                return;
            if (!enabled())
            {
                Show(0);
                return;
            }
            refreshing = true;
            try
            {
                using var client = connect();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var list = await client.ListAsync(NestJobListFilter.Unreviewed, user, NestJobRules.MaxListLimit,
                    cancellationToken: timeout.Token);
                Show(list?.Jobs.Count ?? 0);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                or UriFormatException or InvalidOperationException)
            {
                Show(0);
            }
            finally
            {
                refreshing = false;
            }
        }

        private void Show(int count)
        {
            if (disposed)
                return;
            var text = ServerJobText.Unreviewed(count);
            Label.Text = text ?? "";
            Label.Visible = text != null;
        }

        private async void Decision_JobsChanged(object sender, EventArgs e) => await RefreshAsync();

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            ServerNestDecision.JobsChanged -= Decision_JobsChanged;
            timer.Dispose();
            Label.Font.Dispose();
        }
    }
}
