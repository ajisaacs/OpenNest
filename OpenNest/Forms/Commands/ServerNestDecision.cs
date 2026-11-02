using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Records the review of a server nest job's result on the server: Keep before anything is
    /// committed here, so of two desktops reviewing the same result only the first keeps it, and
    /// Discard so the job stops waiting for review. See docs/server-nesting.md.
    /// </summary>
    internal sealed class ServerNestDecision
    {
        private const string Caption = "Auto Nest";

        private readonly NestJobRecord job;
        private readonly Func<RemoteNestJobs> connect;

        public ServerNestDecision(NestJobRecord job, Func<RemoteNestJobs> connect = null)
        {
            this.job = job ?? throw new ArgumentNullException(nameof(job));
            this.connect = connect ?? (() => new RemoteNestJobs(NestStorage.Settings.ServerUrl));
        }

        /// <summary>Who submits and reviews server jobs from this desktop: <c>user@machine</c>.</summary>
        internal static string Identity => $"{Environment.UserName}@{Environment.MachineName}";

        /// <summary>A server job was submitted, finished, kept, discarded or removed from this desktop.</summary>
        internal static event EventHandler JobsChanged;

        internal static void RaiseJobsChanged() => JobsChanged?.Invoke(null, EventArgs.Empty);

        /// <summary>Tells the user why a Keep was refused; a message box unless tests replace it.</summary>
        internal Action<string> Warn { get; init; }

        /// <summary>
        /// Records the Keep. True when the result may be committed here; otherwise tells the user
        /// why not (kept or discarded already, removed, or the server unreachable) and nothing is
        /// kept. A server too old to record it keeps as before.
        /// </summary>
        public async Task<bool> KeepAsync(IWin32Window owner)
        {
            string problem;
            try
            {
                using var jobs = connect();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (await jobs.KeepAsync(job, Identity, timeout.Token) != null)
                    return true;
                problem = "The nest server no longer has this job.";
            }
            catch (ServerTooOldException)
            {
                return true;
            }
            catch (NestJobConflictException ex)
            {
                problem = ex.Message;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                problem = $"The nest server could not record the Keep: {ex.Message}\n\n"
                    + "The result is still on the server; open it again from My Server Jobs.";
            }
            finally
            {
                RaiseJobsChanged();
            }
            var message = $"{problem}\n\nNothing was added to the nest.";
            if (Warn != null)
                Warn(message);
            else
                MessageBox.Show(owner, message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        /// <summary>Records the Discard, best effort: a result left unreviewed only stays in My Server Jobs.</summary>
        public async Task DiscardAsync()
        {
            try
            {
                using var jobs = connect();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await jobs.DiscardAsync(job, Identity, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
            {
            }
            finally
            {
                RaiseJobsChanged();
            }
        }
    }
}
