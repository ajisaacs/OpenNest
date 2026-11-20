using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Engine;

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
        private bool completed;

        /// <summary>Keep the owner disabled until a whole-job worker finishes after Stop/close.</summary>
        private bool holdOpenUntilCompleted;
        public bool HoldOpenUntilCompleted
        {
            get => holdOpenUntilCompleted;
            set
            {
                holdOpenUntilCompleted = value;
                if (value)
                    stopButton.Enabled = true; // Plug-ins need not report progress before cancellation.
            }
        }

        public bool Accepted { get; private set; }

        /// <summary>
        /// False hides "Accept" for solvers that cannot return a partial result on cancellation
        /// (whole-job engines throw instead).
        /// </summary>
        public bool AllowAccept
        {
            get => acceptButton.Visible;
            set => acceptButton.Visible = value;
        }

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

            // In plan mode the results rows describe the whole-job plan, not a part preview.
            if (progress.IsOverallBest && layoutList == null)
            {
                SetValueWithFlash(partsValue, progress.BestPartCount.ToString());

                var densityText = progress.BestDensity.ToString("P1");
                var densityFlashColor = GetDensityColor(progress.BestDensity);
                SetValueWithFlash(densityValue, densityText, densityFlashColor);
                densityBar.Value = progress.BestDensity;

                SetValueWithFlash(
                    nestedAreaValue,
                    $"{progress.NestedWidth:F1} x {progress.NestedLength:F1} ({progress.NestedArea:F1} sq in)"
                );
            }

            descriptionValue.Text = !string.IsNullOrEmpty(progress.Description)
                ? progress.Description
                : progress.Phase.DisplayName();
        }

        private Controls.LayoutChoiceList layoutList;
        private NestLayoutChoices layouts;
        private Button useLayoutButton;
        private Action stopSearch;
        private TaskCompletionSource<bool> choice;

        /// <summary>
        /// Switches the window to whole-job layouts: the run's top layouts as cards, the best
        /// first, each with its sheets and the free strip its last sheet leaves, over a status
        /// line. A whole-job result is a set of sheets rather than parts on the current plate.
        /// With <paramref name="stopSearch"/> the user may pick a card and use that layout: during
        /// the run it stops the search (<paramref name="stopSearch"/>), and afterwards
        /// <see cref="ChooseLayoutAsync"/> waits for the pick. Without it the cards only show.
        /// </summary>
        public void ShowPlanStrip(ColorScheme colorScheme, Action stopSearch = null)
        {
            if (layoutList != null)
                return;
            this.stopSearch = stopSearch;
            layouts = new NestLayoutChoices();
            var choosing = stopSearch != null;

            SuspendLayout();
            Controls.Remove(statsPanel);
            var header = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = resultsHeader.Font,
                ForeColor = resultsHeader.ForeColor,
                Padding = new Padding(10, 8, 0, 0),
                Text = choosing ? "TOP LAYOUTS" : "BEST PLAN SO FAR",
            };
            layoutList = new Controls.LayoutChoiceList
            {
                Dock = DockStyle.Fill,
                ColorScheme = colorScheme,
                AllowSelection = choosing,
                EmptyText = "Waiting for the first complete plan…",
            };

            // Status and buttons share one line: the detail text gets the whole width.
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                BackColor = Color.White,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(10, 2, 0, 2),
            };
            footer.ColumnStyles.Add(new ColumnStyle());
            footer.ColumnStyles.Add(new ColumnStyle());
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle());
            elapsedLabel.Anchor = elapsedValue.Anchor = AnchorStyles.Left;
            elapsedValue.Margin = new Padding(0, 3, 12, 3);
            descriptionValue.AutoSize = false;
            descriptionValue.AutoEllipsis = true;
            descriptionValue.Dock = DockStyle.Fill;
            descriptionValue.TextAlign = ContentAlignment.MiddleLeft;
            buttonPanel.Dock = DockStyle.None;
            buttonPanel.Anchor = AnchorStyles.Right;
            buttonPanel.WrapContents = false;
            if (choosing)
            {
                useLayoutButton = new Button
                {
                    Name = "useLayoutButton",
                    Enabled = false,
                    Font = stopButton.Font,
                    Margin = new Padding(0, 3, 6, 3),
                    Size = new System.Drawing.Size(132, 27),
                    Text = "Use This Layout",
                    UseVisualStyleBackColor = true,
                };
                useLayoutButton.Click += (s, e) => UseSelectedLayout();
                buttonPanel.Controls.Add(useLayoutButton);
                layoutList.SelectionChanged += (s, e) => UpdateUseLayout();
                layoutList.ChoiceActivated += (s, e) => UseSelectedLayout();
            }
            footer.Controls.Add(elapsedLabel, 0, 0);
            footer.Controls.Add(elapsedValue, 1, 0);
            footer.Controls.Add(descriptionValue, 2, 0);
            footer.Controls.Add(buttonPanel, 3, 0);

            Controls.Add(layoutList);
            Controls.Add(header);
            Controls.Add(footer);
            MinimumSize = new System.Drawing.Size(640, 420);
            ClientSize = new System.Drawing.Size(920, 600);
            ResumeLayout(true);
        }

        /// <summary>The layouts offered now, best first; empty outside plan mode.</summary>
        internal IReadOnlyList<NestLayoutChoice> Layouts => layouts?.Top ?? Array.Empty<NestLayoutChoice>();

        /// <summary>The layout the user chose to use; null when none was chosen.</summary>
        internal NestLayoutChoice ChosenLayout { get; private set; }

        /// <summary>
        /// Adds a layout from the running solve to the cards added by <see cref="ShowPlanStrip"/>;
        /// <paramref name="best"/> marks the engine's best so far.
        /// </summary>
        internal void AddLayout(NestLayoutChoice layout, bool best)
        {
            if (layoutList == null || IsDisposed || !IsHandleCreated)
                return;
            if (layouts.Add(layout, best))
                layoutList.SetChoices(layouts.Top, ReferenceEquals(layouts.Top[0], layouts.Best));
        }

        /// <summary>Shows a whole-job plan as the best layout so far.</summary>
        public void UpdatePlan(IReadOnlyList<Engine.Jobs.ProposedPlate> sheets) =>
            AddLayout(new NestLayoutChoice(sheets), best: true);

        /// <summary>
        /// The search is over and <paramref name="final"/> is its result: with other layouts to
        /// pick from, waits until the user uses one (true, in <see cref="ChosenLayout"/>) or
        /// discards them all (false). With nothing to pick from, or a layout already chosen
        /// during the run, returns at once. With <paramref name="timeout"/>, <paramref name="final"/>
        /// is used once it passes, counting down in the status line, unless the user picks a card
        /// first, which stops the countdown.
        /// </summary>
        internal Task<bool> ChooseLayoutAsync(NestLayoutChoice final, TimeSpan? timeout = null)
        {
            if (ChosenLayout != null)
                return Task.FromResult(true);
            if (layoutList == null || useLayoutButton == null || IsDisposed || !Visible)
            {
                ChosenLayout = final;
                return Task.FromResult(true);
            }
            AddLayout(final, best: true);
            if (layouts.Top.Count < 2)
            {
                ChosenLayout = final;
                return Task.FromResult(true);
            }

            choice = new TaskCompletionSource<bool>();
            stopwatch.Stop();
            elapsedTimer.Stop();
            UpdateElapsed();
            Text = "Nesting Finished";
            layoutList.BestLabel = "Best";
            descriptionValue.Text = "Search finished. Pick the layout to use.";
            stopButton.Text = "Discard";
            stopButton.Enabled = true;
            UpdateUseLayout();
            if (timeout is { } limit)
                StartChoiceCountdown(final, limit);
            return choice.Task;
        }

        private System.Windows.Forms.Timer choiceTimer;

        // Uses the search's result when the time is up, unless a card is picked first.
        private void StartChoiceCountdown(NestLayoutChoice final, TimeSpan limit)
        {
            var waited = Stopwatch.StartNew();
            void ShowLeft(TimeSpan left) => descriptionValue.Text =
                $"Search finished. Using the best layout in {System.Math.Max(1, (int)System.Math.Ceiling(left.TotalSeconds))} s; pick a layout to choose.";
            ShowLeft(limit);
            choiceTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)System.Math.Clamp(limit.TotalMilliseconds, 1, 250),
            };
            choiceTimer.Tick += (s, e) =>
            {
                var left = limit - waited.Elapsed;
                if (left > TimeSpan.Zero)
                {
                    ShowLeft(left);
                    return;
                }
                StopChoiceCountdown();
                if (ChosenLayout != null || choice == null || choice.Task.IsCompleted)
                    return;
                ChosenLayout = final;
                layoutList.AllowSelection = false;
                useLayoutButton.Enabled = false;
                stopButton.Enabled = false;
                choice.TrySetResult(true);
            };
            layoutList.SelectionChanged += PickStopsCountdown;
            choiceTimer.Start();
        }

        private void PickStopsCountdown(object sender, EventArgs e)
        {
            if (choiceTimer == null)
                return;
            StopChoiceCountdown();
            descriptionValue.Text = "Search finished. Pick the layout to use.";
        }

        private void StopChoiceCountdown()
        {
            if (choiceTimer == null)
                return;
            layoutList.SelectionChanged -= PickStopsCountdown;
            choiceTimer.Stop();
            choiceTimer.Dispose();
            choiceTimer = null;
        }

        private void UpdateUseLayout()
        {
            if (useLayoutButton != null && ChosenLayout == null)
                useLayoutButton.Enabled = layoutList.Selected?.Accept != null;
        }

        private void UseSelectedLayout()
        {
            if (useLayoutButton == null || ChosenLayout != null || layoutList.Selected?.Accept == null)
                return;
            StopChoiceCountdown();
            ChosenLayout = layoutList.Selected;
            layoutList.AllowSelection = false;
            useLayoutButton.Enabled = false;
            stopButton.Enabled = false;
            if (choice != null)
            {
                choice.TrySetResult(true);
                return;
            }
            // Still searching: stop, then the caller validates the chosen layout.
            useLayoutButton.Text = "Stopping...";
            stopSearch();
        }

        public void ShowCompleted()
        {
            completed = true;
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
            elapsedValue.Text =
                elapsed.TotalHours >= 1
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
            if (choice != null)
            {
                // The search is over: this is Discard.
                StopChoiceCountdown();
                stopButton.Enabled = false;
                choice.TrySetResult(false);
                return;
            }
            if (useLayoutButton != null)
                useLayoutButton.Enabled = false;
            cts.Cancel();
            acceptButton.Enabled = false;
            stopButton.Enabled = false;
            stopButton.Text = "Stopping...";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (HoldOpenUntilCompleted && !completed)
            {
                // Closing while picking a layout discards them all, as Discard does.
                StopChoiceCountdown();
                if (choice != null)
                    choice.TrySetResult(false);
                else
                    cts.Cancel();
                e.Cancel = true;
                return;
            }
            StopChoiceCountdown();
            fadeTimer.Stop();
            fadeTimer.Dispose();
            elapsedTimer.Stop();
            elapsedTimer.Dispose();
            stopwatch.Stop();

            if (!completed && !cts.IsCancellationRequested)
                cts.Cancel();

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
