using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class NestProgressForm : Form
    {
        private static readonly Color DefaultFlashColor = Color.FromArgb(0, 160, 0);
        private static readonly Color DensityLowColor = Color.FromArgb(200, 40, 40);
        private static readonly Color DensityMidColor = Color.FromArgb(200, 160, 0);
        private static readonly Color DensityHighColor = Color.FromArgb(0, 160, 0);

        private const int FadeSteps = 20;
        private const int FadeIntervalMs = 50;

        private readonly CancellationTokenSource cts;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer elapsedTimer;
        private readonly System.Windows.Forms.Timer fadeTimer;
        private readonly Dictionary<Label, (int remaining, Color flashColor)> fadeCounters = new();
        private bool hasReceivedProgress;

        public bool Accepted { get; private set; }

        public Plate PreviewPlate
        {
            get => previewPlateView.Plate;
            set
            {
                previewPlateView.Plate = value;
                previewPlateView.ZoomToFit();
            }
        }

        public NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)
        {
            this.cts = cts;
            InitializeComponent();

            previewPlateView.AllowSelect = false;

            if (!showPlateRow)
            {
                plateLabel.Visible = false;
                plateValue.Visible = false;
            }

            elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            elapsedTimer.Tick += (s, e) => UpdateElapsed();
            elapsedTimer.Start();

            fadeTimer = new System.Windows.Forms.Timer { Interval = FadeIntervalMs };
            fadeTimer.Tick += FadeTimer_Tick;
        }

        public void UpdateProgress(NestProgress progress)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (!hasReceivedProgress)
            {
                hasReceivedProgress = true;
                acceptButton.Enabled = true;
                stopButton.Enabled = true;
            }

            phaseStepper.ActivePhase = progress.Phase;
            SetValueWithFlash(plateValue, progress.PlateNumber.ToString());

            if (progress.IsOverallBest)
            {
                SetValueWithFlash(partsValue, progress.BestPartCount.ToString());

                var densityText = progress.BestDensity.ToString("P1");
                var densityFlashColor = GetDensityColor(progress.BestDensity);
                SetValueWithFlash(densityValue, densityText, densityFlashColor);
                densityBar.Value = progress.BestDensity;

                SetValueWithFlash(nestedAreaValue,
                    $"{progress.NestedWidth:F1} x {progress.NestedLength:F1} ({progress.NestedArea:F1} sq in)");
            }

            descriptionValue.Text = !string.IsNullOrEmpty(progress.Description)
                ? progress.Description
                : progress.Phase.DisplayName();
        }

        public void UpdatePreview(List<Part> bestParts)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            var plate = previewPlateView.Plate;
            plate.Parts.Clear();

            foreach (var part in bestParts)
                plate.Parts.Add((Part)part.Clone());

            previewPlateView.ZoomToFit();
        }

        public void ShowCompleted()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            stopwatch.Stop();
            elapsedTimer.Stop();
            UpdateElapsed();

            phaseStepper.IsComplete = true;
            descriptionValue.Text = "\u2014";

            acceptButton.Visible = false;
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

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            Accepted = true;
            cts.Cancel();
            acceptButton.Enabled = false;
            stopButton.Enabled = false;
            acceptButton.Text = "Accepted";
            stopButton.Text = "Stopping...";
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            acceptButton.Enabled = false;
            stopButton.Enabled = false;
            stopButton.Text = "Stopping...";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            fadeTimer.Stop();
            fadeTimer.Dispose();
            elapsedTimer.Stop();
            elapsedTimer.Dispose();
            stopwatch.Stop();

            if (!cts.IsCancellationRequested)
                cts.Cancel();

            previewPlateView.Dispose();

            base.OnFormClosing(e);
        }

        private void SetValueWithFlash(Label label, string text, Color? flashColor = null)
        {
            if (label.Text == text)
                return;

            var color = flashColor ?? DefaultFlashColor;
            label.Text = text;
            label.ForeColor = color;
            fadeCounters[label] = (FadeSteps, color);

            if (!fadeTimer.Enabled)
                fadeTimer.Start();
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                fadeTimer.Stop();
                return;
            }

            var defaultColor = SystemColors.ControlText;
            var labels = fadeCounters.Keys.ToList();

            foreach (var label in labels)
            {
                var (remaining, flashColor) = fadeCounters[label];
                remaining--;

                if (remaining <= 0)
                {
                    label.ForeColor = defaultColor;
                    fadeCounters.Remove(label);
                }
                else
                {
                    var ratio = (float)remaining / FadeSteps;
                    var r = (int)(defaultColor.R + (flashColor.R - defaultColor.R) * ratio);
                    var g = (int)(defaultColor.G + (flashColor.G - defaultColor.G) * ratio);
                    var b = (int)(defaultColor.B + (flashColor.B - defaultColor.B) * ratio);
                    label.ForeColor = Color.FromArgb(r, g, b);
                    fadeCounters[label] = (remaining, flashColor);
                }
            }

            if (fadeCounters.Count == 0)
                fadeTimer.Stop();
        }

        private static Color GetDensityColor(double density)
        {
            if (density < 0.5)
                return DensityLowColor;
            if (density < 0.7)
                return DensityMidColor;
            return DensityHighColor;
        }
    }
}
