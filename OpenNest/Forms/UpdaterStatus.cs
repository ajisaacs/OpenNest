using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Background update checks (GitHub Releases, or the per-machine feed URL from the
    /// Options dialog) and the status-bar item that announces a downloaded update. Never
    /// restarts on its own: it restarts only when the user clicks the item and confirms.
    /// A download left unapplied installs at the next startup (Velopack's default
    /// auto-apply in <see cref="MainApp"/>).
    /// </summary>
    internal sealed class UpdaterStatus : IDisposable
    {
        private readonly Form owner;
        private readonly UpdaterService updater = new();
        private readonly ToolStripStatusLabel label;

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

            updater.UpdateReady += version =>
            {
                if (owner.IsHandleCreated)
                    owner.BeginInvoke(new Action(() => ShowUpdateReady(version)));
            };
        }

        public void Start() => updater.Start();

        public void Dispose() => updater.Dispose();

        private void Label_Click(object sender, EventArgs e)
        {
            if (!updater.HasPendingUpdate)
                return;

            var result = MessageBox.Show(
                owner,
                "An OpenNest update has been downloaded. Restart now to apply it? Any unsaved nest will be lost.",
                "Update ready",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (result == DialogResult.Yes)
                updater.ApplyAndRestart();
        }

        private void ShowUpdateReady(string version)
        {
            label.Text = $"Update {version} ready - click to restart";
            label.Visible = true;
        }
    }
}
