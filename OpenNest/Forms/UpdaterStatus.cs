using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Background and on-demand (Help > Check for Updates) update checks (GitHub Releases,
    /// or the per-machine feed URL from the Options dialog), the status-bar version label
    /// and the status-bar item that announces a downloaded update. Never
    /// restarts on its own: it restarts only when the user clicks the item and confirms.
    /// A download left unapplied installs at the next startup (Velopack's default
    /// auto-apply in <see cref="MainApp"/>).
    /// </summary>
    internal sealed class UpdaterStatus : IDisposable
    {
        private readonly Form owner;
        private readonly UpdaterService updater = new();
        private const string ReleasesUrl = "https://github.com/ajisaacs/OpenNest/releases";

        private readonly ToolStripStatusLabel label;
        private bool checking;

        public UpdaterStatus(Form owner, StatusStrip statusStrip, Font baseFont)
        {
            this.owner = owner;
            label = new ToolStripStatusLabel
            {
                Visible = false,
                ForeColor = Color.DarkGreen,
                Font = new Font(baseFont, FontStyle.Bold),
            };
            label.Click += Label_Click;
            statusStrip.Items.Add(label);

            statusStrip.Items.Add(new ToolStripStatusLabel
            {
                Text = "v" + CurrentVersion,
                ToolTipText = "OpenNest version",
                ForeColor = SystemColors.GrayText,
            });

            updater.UpdateReady += version =>
            {
                if (owner.IsHandleCreated)
                    owner.BeginInvoke(new Action(() => ShowUpdateReady(version)));
            };
        }

        public string CurrentVersion => updater.CurrentVersion;

        public void Start() => updater.Start();

        /// <summary>Checks for an update now and reports the outcome; offers a restart when one is ready.</summary>
        public async void CheckForUpdates()
        {
            if (checking)
                return;

            checking = true;
            var previousCursor = owner.Cursor;
            owner.Cursor = Cursors.WaitCursor;
            UpdaterService.CheckResult result;
            try
            {
                result = await updater.CheckNowAsync();
            }
            finally
            {
                owner.Cursor = previousCursor;
                checking = false;
            }

            switch (result)
            {
                case UpdaterService.CheckResult.UpdateReady:
                    ShowUpdateReady(updater.PendingVersion);
                    PromptRestart($"OpenNest {updater.PendingVersion} has been downloaded. Restart now to apply it? Any unsaved nest will be lost.");
                    break;

                case UpdaterService.CheckResult.UpToDate:
                    MessageBox.Show(owner, $"OpenNest {CurrentVersion} is the latest version.",
                        "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;

                case UpdaterService.CheckResult.Failed:
                    MessageBox.Show(owner, "Could not reach the update source. Check the network connection and try again.",
                        "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;

                case UpdaterService.CheckResult.NotInstalled:
                    var open = MessageBox.Show(owner,
                        $"This copy of OpenNest ({CurrentVersion}) was not installed with the OpenNest installer, so it cannot update itself.\n\nOpen the releases page to download the latest version?",
                        "Check for Updates", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                        OpenReleasesPage();
                    break;
            }
        }

        public void ShowAbout()
        {
            MessageBox.Show(owner, $"OpenNest {CurrentVersion}\n\n{ReleasesUrl}",
                "About OpenNest", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void Dispose() => updater.Dispose();

        private void Label_Click(object sender, EventArgs e)
        {
            if (!updater.HasPendingUpdate)
                return;

            PromptRestart("An OpenNest update has been downloaded. Restart now to apply it? Any unsaved nest will be lost.");
        }

        private void PromptRestart(string message)
        {
            var result = MessageBox.Show(owner, message, "Update ready", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
                updater.ApplyAndRestart();
        }

        private void OpenReleasesPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Could not open {ReleasesUrl}: {ex.Message}",
                    "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowUpdateReady(string version)
        {
            label.Text = $"Update {version} ready - click to restart";
            label.Visible = true;
        }
    }
}
