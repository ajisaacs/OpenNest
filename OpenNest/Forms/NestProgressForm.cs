using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class NestProgressForm : Form
    {
        private readonly CancellationTokenSource cts;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer elapsedTimer;

        public NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)
        {
            this.cts = cts;
            InitializeComponent();

            if (!showPlateRow)
            {
                plateLabel.Visible = false;
                plateValue.Visible = false;
            }

            elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            elapsedTimer.Tick += (s, e) => UpdateElapsed();
            elapsedTimer.Start();
        }

        public void UpdateProgress(NestProgress progress)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            phaseValue.Text = FormatPhase(progress.Phase);
            plateValue.Text = progress.PlateNumber.ToString();
            partsValue.Text = progress.BestPartCount.ToString();
            densityValue.Text = progress.BestDensity.ToString("P1");
            remnantValue.Text = $"{progress.UsableRemnantArea:F1} sq in";

            descriptionValue.Text = !string.IsNullOrEmpty(progress.Description)
                ? progress.Description
                : "\u2014";
        }

        public void ShowCompleted()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            stopwatch.Stop();
            elapsedTimer.Stop();
            UpdateElapsed();

            phaseValue.Text = "Done";
            stopButton.Text = "Close";
            stopButton.Enabled = true;
            stopButton.Click -= StopButton_Click;
            stopButton.Click += (s, e) => Close();
        }

        private void UpdateElapsed()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            var elapsed = stopwatch.Elapsed;
            elapsedValue.Text = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"m\:ss");
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            stopButton.Text = "Stopping...";
            stopButton.Enabled = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            elapsedTimer.Stop();
            elapsedTimer.Dispose();
            stopwatch.Stop();

            if (!cts.IsCancellationRequested)
                cts.Cancel();

            base.OnFormClosing(e);
        }

        private static string FormatPhase(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Linear: return "Trying rotations...";
                case NestPhase.RectBestFit: return "Trying best fit...";
                case NestPhase.Pairs: return "Trying pairs...";
                case NestPhase.Remainder: return "Filling remainder...";
                default: return phase.ToString();
            }
        }
    }
}
