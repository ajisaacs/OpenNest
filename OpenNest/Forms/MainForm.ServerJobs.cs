using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms.Commands;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        // My Server Jobs while it is open, and the status-bar count of results to review.
        private ServerJobsForm serverJobsForm;
        private ServerJobsStatus serverJobsStatus;

        private void InitializeServerJobs()
        {
            serverJobsStatus = new ServerJobsStatus(statusStrip1, statusStrip1.Items.IndexOf(machineStatusLabel),
                ShowServerJobs, statusLabel1.Font);
            Shown += (_, _) => serverJobsStatus.Start();
            FormClosed += (_, _) => serverJobsStatus.Dispose();
        }

        private void ServerJobs_Click(object sender, EventArgs e) => ShowServerJobs();

        /// <summary>Shows My Server Jobs (Database mode), bringing it forward when it is already open.</summary>
        internal void ShowServerJobs()
        {
            if (!NestStorage.Settings.IsDatabaseMode)
            {
                MessageBox.Show(this, "Server jobs need Database storage mode (File > Storage Mode).",
                    "My Server Jobs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (serverJobsForm is { IsDisposed: false })
            {
                serverJobsForm.Activate();
                return;
            }
            serverJobsForm = new ServerJobsForm(OpenServerJobAsync);
            serverJobsForm.FormClosed += (_, _) => serverJobsForm = null;
            serverJobsForm.Show(this);
        }

        /// <summary>
        /// Reviews a finished server job's result in the nest it was sent from (by its server id,
        /// then its name), or in the active nest when that one is not open and the user agrees.
        /// </summary>
        private async Task OpenServerJobAsync(NestJobRecord job)
        {
            const string caption = "Open Server Nest";
            var target = FindServerJobTarget(job);
            if (target == null)
            {
                var active = activeForm;
                if (active == null || active.IsDisposed)
                {
                    MessageBox.Show(this, $"Open the nest '{ServerJobText.Title(job)}' first, then open its result.",
                        caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this,
                        $"The nest '{ServerJobText.Title(job)}' this job was sent from is not open.\n\n"
                        + $"Review the result in '{active.Nest.Name}' instead? Its drawings must be the ones the job was sent with.",
                        caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                target = active;
            }
            if (nestingInProgress)
            {
                MessageBox.Show(this, "Finish or stop the current Auto Nest first.", caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (target != activeForm)
                target.Activate();
            if (!TryBeginPlateCommand(target.Nest,
                    "Finish or cancel the current plate operation before opening a server result.", caption))
                return;

            using var cts = new CancellationTokenSource();
            nestingCts = cts;
            SetNestingLockout(true);
            try
            {
                await ServerJobOpen.RunAsync(this, target, job, cts);
            }
            finally
            {
                SetNestingLockout(false);
                nestingCts = null;
            }
        }

        private EditNestForm FindServerJobTarget(NestJobRecord job)
        {
            var forms = MdiChildren.OfType<EditNestForm>().Where(f => !f.IsDisposed).ToList();
            if (job.NestId is { } id && forms.FirstOrDefault(f => f.Document.RemoteId == id) is { } saved)
                return saved;
            var named = forms.Where(f => string.Equals(f.Nest.Name, job.Title, StringComparison.OrdinalIgnoreCase)).ToList();
            return named.Contains(activeForm) ? activeForm : named.FirstOrDefault();
        }
    }
}
