using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using NumericUpDown = OpenNest.Controls.NumericUpDown;

namespace OpenNest.Forms
{
    /// <summary>
    /// Playback controls for a <see cref="CutSimulationOverlay"/> on a nest's plate view. Follows
    /// the current plate and rebuilds the timeline when parts are added, removed or resequenced.
    /// </summary>
    public sealed class CutSimulatorForm : Form
    {
        private const int TrackResolution = 1000;
        private static readonly double[] Speeds = { 1, 2, 5, 10, 25, 50, 100, 250 };

        private readonly EditNestForm nestForm;
        private readonly CutSimulationOverlay overlay;
        private readonly Button restartButton, playButton, endButton;
        private readonly Button previousPartButton, previousContourButton, nextContourButton, nextPartButton;
        private readonly ComboBox speedBox;
        private readonly TrackBar trackBar;
        private readonly Label timeLabel, partLabel, stateLabel;
        private readonly NumericUpDown feedBox, rapidBox, pierceBox;
        private Plate plate;
        private bool updatingTrack;
        private bool rebuildQueued;

        public CutSimulatorForm(EditNestForm nestForm)
        {
            this.nestForm = nestForm;
            overlay = nestForm.PlateView.CutSimulation;

            Text = "Cut Simulator";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(420, 0);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly;

            restartButton = new Button { Text = "⏮", Width = 40, AutoSize = false };
            playButton = new Button { Text = "▶", Width = 56, AutoSize = false };
            endButton = new Button { Text = "⏭", Width = 40, AutoSize = false };
            speedBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            foreach (var speed in Speeds)
                speedBox.Items.Add($"{speed:g}×");
            speedBox.SelectedIndex = Array.IndexOf(Speeds, 10.0);

            previousPartButton = JumpButton("◀◀ Part");
            previousContourButton = JumpButton("◀ Cutout");
            nextContourButton = JumpButton("Cutout ▶");
            nextPartButton = JumpButton("Part ▶▶");
            var jumps = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            jumps.Controls.AddRange(new Control[] { previousPartButton, previousContourButton, nextContourButton, nextPartButton });

            var transport = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            transport.Controls.AddRange(new Control[]
            {
                restartButton,
                playButton,
                endButton,
                new Label { Text = "Speed", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 0, 0) },
                speedBox,
            });

            trackBar = new TrackBar
            {
                Dock = DockStyle.Fill,
                Maximum = TrackResolution,
                TickStyle = TickStyle.None,
                LargeChange = TrackResolution / 20,
                SmallChange = 1,
            };

            timeLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            partLabel = new Label { AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
            stateLabel = new Label { AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
            var status = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            status.Controls.AddRange(new Control[] { timeLabel, partLabel, stateLabel });

            var units = nestForm.Nest.Units;
            var rateSuffix = " " + UnitsHelper.GetShortTimeUnitPair(units);
            // Settings that were never saved (1, 1, 0) would crawl; start from the timing defaults instead.
            var settings = Properties.Settings.Default;
            var unset = settings.LastFeedrate == 1 && settings.LastRapidFeedrate == 1 && settings.LastPierceTime == 0;
            var defaults = CutParameters.Default;
            feedBox = NewNumber(unset ? (decimal)defaults.Feedrate : settings.LastFeedrate, 1, 9999, 0, rateSuffix);
            rapidBox = NewNumber(unset ? (decimal)defaults.RapidTravelRate : settings.LastRapidFeedrate, 1, 9999, 0, rateSuffix);
            pierceBox = NewNumber(unset ? (decimal)defaults.PierceTime.TotalSeconds : settings.LastPierceTime, 0, 999, 1, " s");
            var rates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            rates.Controls.AddRange(new Control[]
            {
                RateLabel("Feed"), feedBox,
                RateLabel("Rapid"), rapidBox,
                RateLabel("Pierce"), pierceBox,
            });

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(6),
            };
            layout.Controls.Add(transport);
            layout.Controls.Add(jumps);
            layout.Controls.Add(trackBar);
            layout.Controls.Add(status);
            layout.Controls.Add(rates);
            Controls.Add(layout);

            restartButton.Click += (_, _) => overlay.Time = TimeSpan.Zero;
            endButton.Click += (_, _) =>
            {
                overlay.Pause();
                overlay.Time = overlay.Simulation?.TotalTime ?? TimeSpan.Zero;
            };
            playButton.Click += (_, _) => TogglePlay();
            previousPartButton.Click += (_, _) => Jump(sim => sim.PreviousPart(overlay.Time));
            previousContourButton.Click += (_, _) => Jump(sim => sim.PreviousContour(overlay.Time));
            nextContourButton.Click += (_, _) => Jump(sim => sim.NextContour(overlay.Time));
            nextPartButton.Click += (_, _) => Jump(sim => sim.NextPart(overlay.Time));
            speedBox.SelectedIndexChanged += (_, _) => overlay.Speed = Speeds[speedBox.SelectedIndex];
            trackBar.Scroll += (_, _) => Seek();
            feedBox.ValueChanged += (_, _) => Rebuild();
            rapidBox.ValueChanged += (_, _) => Rebuild();
            pierceBox.ValueChanged += (_, _) => Rebuild();

            var tips = new ToolTip();
            tips.SetToolTip(restartButton, "Restart");
            tips.SetToolTip(playButton, "Play / pause (Space)");
            tips.SetToolTip(endButton, "Jump to end");
            tips.SetToolTip(previousPartButton, "Back to the start of this part, or the part before (Page Up)");
            tips.SetToolTip(previousContourButton, "Back to the start of this cutout or perimeter, or the one before (Ctrl+Left)");
            tips.SetToolTip(nextContourButton, "Next cutout or perimeter (Ctrl+Right)");
            tips.SetToolTip(nextPartButton, "Next part (Page Down)");
            tips.SetToolTip(trackBar, "Drag to scrub through the cut (Left/Right step one move)");

            overlay.Speed = Speeds[speedBox.SelectedIndex];
            overlay.Changed += Overlay_Changed;
            nestForm.PlateChanged += NestForm_PlateChanged;
            nestForm.FormClosed += NestForm_FormClosed;
            AttachPlate(nestForm.PlateView.Plate);
            Rebuild();
        }

        private static NumericUpDown NewNumber(decimal value, decimal min, decimal max, int decimals, string suffix) =>
            new()
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 10 : 0.1m,
                Value = System.Math.Clamp(value, min, max),
                Suffix = suffix,
                Width = 110,
            };

        private static Button JumpButton(string text) =>
            new() { Text = text, AutoSize = true, MinimumSize = new Size(80, 0) };

        // Jumps pause so the jumped-to contour stays on screen.
        private void Jump(Func<CutSimulation, TimeSpan> target)
        {
            if (overlay.Simulation is not { } sim || sim.Steps.Count == 0)
                return;
            overlay.Pause();
            overlay.Time = target(sim);
        }

        private static Label RateLabel(string text) =>
            new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 0, 0) };

        private CutParameters GetCutParameters() =>
            new()
            {
                Feedrate = (double)feedBox.Value,
                RapidTravelRate = (double)rapidBox.Value,
                PierceTime = TimeSpan.FromSeconds((double)pierceBox.Value),
                Units = nestForm.Nest.Units,
            };

        private void Rebuild()
        {
            if (plate == null)
            {
                overlay.Clear();
                return;
            }
            overlay.Load(CutSimulation.Build(plate, GetCutParameters()));
        }

        private void AttachPlate(Plate value)
        {
            if (plate != null)
            {
                plate.PartAdded -= Plate_PartsChanged;
                plate.PartRemoved -= Plate_PartsChanged;
                plate.PartsReordered -= Plate_PartsChanged;
            }

            plate = value;

            if (plate != null)
            {
                plate.PartAdded += Plate_PartsChanged;
                plate.PartRemoved += Plate_PartsChanged;
                plate.PartsReordered += Plate_PartsChanged;
            }
        }

        // A fill adds parts one at a time; rebuild once after the batch.
        private void Plate_PartsChanged(object sender, EventArgs e)
        {
            if (rebuildQueued || !IsHandleCreated)
                return;
            rebuildQueued = true;
            BeginInvoke(() =>
            {
                rebuildQueued = false;
                if (!IsDisposed)
                    Rebuild();
            });
        }

        private void NestForm_PlateChanged(object sender, EventArgs e)
        {
            if (nestForm.PlateView.Plate == plate)
                return;
            AttachPlate(nestForm.PlateView.Plate);
            overlay.Pause();
            overlay.Clear();
            Rebuild();
        }

        private void NestForm_FormClosed(object sender, FormClosedEventArgs e) => Close();

        private void TogglePlay()
        {
            if (overlay.IsPlaying)
            {
                overlay.Pause();
                return;
            }
            // Parts may have moved since the timeline was built; moves raise no plate event.
            if (overlay.Simulation == null || overlay.Time <= TimeSpan.Zero || overlay.Time >= overlay.Simulation.TotalTime)
                Rebuild();
            overlay.Play();
        }

        private void Seek()
        {
            if (updatingTrack || overlay.Simulation == null)
                return;
            overlay.Time = overlay.Simulation.TotalTime * ((double)trackBar.Value / TrackResolution);
        }

        private void StepMove(int direction)
        {
            var sim = overlay.Simulation;
            if (sim == null || sim.Steps.Count == 0)
                return;
            overlay.Pause();
            var index = System.Math.Clamp(sim.IndexAt(overlay.Time), 0, sim.Steps.Count);
            var atStart = index < sim.Steps.Count && overlay.Time == sim.Steps[index].StartTime;
            var target = direction > 0 ? index + 1 : atStart ? index - 1 : index;
            overlay.Time = target <= 0 ? TimeSpan.Zero
                : target >= sim.Steps.Count ? sim.TotalTime
                : sim.Steps[target].StartTime;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ActiveControl is not System.Windows.Forms.NumericUpDown and not ComboBox)
            {
                switch (keyData)
                {
                    case Keys.Escape:
                        Close();
                        return true;
                    case Keys.Space:
                        TogglePlay();
                        return true;
                    case Keys.Control | Keys.Right:
                        Jump(sim => sim.NextContour(overlay.Time));
                        return true;
                    case Keys.Control | Keys.Left:
                        Jump(sim => sim.PreviousContour(overlay.Time));
                        return true;
                    case Keys.PageDown:
                        Jump(sim => sim.NextPart(overlay.Time));
                        return true;
                    case Keys.PageUp:
                        Jump(sim => sim.PreviousPart(overlay.Time));
                        return true;
                    case Keys.Right:
                        StepMove(1);
                        return true;
                    case Keys.Left:
                        StepMove(-1);
                        return true;
                    case Keys.Home:
                        overlay.Time = TimeSpan.Zero;
                        return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Overlay_Changed(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            var sim = overlay.Simulation;
            playButton.Text = overlay.IsPlaying ? "⏸" : "▶";

            if (sim == null || sim.Steps.Count == 0)
            {
                timeLabel.Text = "Nothing to cut";
                partLabel.Text = stateLabel.Text = string.Empty;
                SetTrack(0);
                return;
            }

            var time = overlay.Time;
            timeLabel.Text = $"{TimingForm.GetTimeMsg(time)} / {TimingForm.GetTimeMsg(sim.TotalTime)}";
            SetTrack(sim.TotalTime > TimeSpan.Zero ? (int)System.Math.Round(TrackResolution * (time / sim.TotalTime)) : 0);

            var index = sim.IndexAt(time);
            if (index >= sim.Steps.Count)
            {
                partLabel.Text = $"{sim.PartCount} of {sim.PartCount} parts";
                stateLabel.Text = "Done";
                return;
            }

            var step = sim.Steps[index];
            var drawing = plate != null && step.PartIndex < plate.Parts.Count ? plate.Parts[step.PartIndex].BaseDrawing?.Name : null;
            partLabel.Text = $"Part {step.PartIndex + 1} of {sim.PartCount}" + (string.IsNullOrEmpty(drawing) ? "" : $" ({drawing})");
            stateLabel.Text = step.Kind switch
            {
                CutSimulationStepKind.Rapid => "Rapid",
                CutSimulationStepKind.Pierce => "Piercing",
                CutSimulationStepKind.Scribe => "Scribing",
                _ => "Cutting",
            };
        }

        private void SetTrack(int value)
        {
            updatingTrack = true;
            trackBar.Value = System.Math.Clamp(value, trackBar.Minimum, trackBar.Maximum);
            updatingTrack = false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Properties.Settings.Default.LastFeedrate = feedBox.Value;
            Properties.Settings.Default.LastRapidFeedrate = rapidBox.Value;
            Properties.Settings.Default.LastPierceTime = pierceBox.Value;
            Properties.Settings.Default.Save();

            overlay.Changed -= Overlay_Changed;
            nestForm.PlateChanged -= NestForm_PlateChanged;
            nestForm.FormClosed -= NestForm_FormClosed;
            AttachPlate(null);
            if (!nestForm.PlateView.IsDisposed)
                overlay.Clear();

            base.OnFormClosed(e);
        }
    }
}
