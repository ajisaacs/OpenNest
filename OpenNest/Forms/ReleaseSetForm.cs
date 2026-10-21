using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing Sets &gt; Release: previews how a set splits into one nest per material group
    /// (<see cref="SetReleasePlan"/>), with a kit multiplier, an archived-drawing toggle and a
    /// tick per group, then makes those nests through <see cref="SetReleaseSession"/>: each is
    /// uploaded as a Draft and opened in its own tab, and the release is recorded once every
    /// chosen nest exists. The dialog shows every group's outcome; a release runs once per
    /// dialog.
    /// </summary>
    public partial class ReleaseSetForm : Form
    {
        private readonly IDrawingSetStore sets;
        private readonly IDrawingLibrary library;
        private readonly INestRepository nests;
        private readonly string serverUrl;
        private readonly DrawingSetRecord set;
        private readonly Action<Nest, NestRecord> openNest;
        private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
        private SetReleasePlan plan;
        private SetReleaseSession session;
        private CancellationTokenSource cancellation;
        private bool populating;
        private bool running;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Makes the empty nest for a group, as File &gt; New would in its units; replaced by tests.</summary>
        internal Func<SetReleaseGroup, CancellationToken, Task<Nest>> CreateNest { get; set; }

        /// <summary>The finished release, once one has run; otherwise null.</summary>
        public SetReleaseResult Result { get; private set; }

        /// <param name="set">The set read with <see cref="IDrawingSetStore.GetAsync"/>, members joined.</param>
        /// <param name="openNest">Opens a created nest in its own tab, bound to its server record.</param>
        public ReleaseSetForm(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
            DrawingSetRecord set, Action<Nest, NestRecord> openNest)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.nests = nests ?? throw new ArgumentNullException(nameof(nests));
            this.serverUrl = serverUrl ?? "";
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            this.openNest = openNest ?? throw new ArgumentNullException(nameof(openNest));
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
            CreateNest = (group, _) => NewNestFactory.CreateAsync(this, group.Units);

            GridStyle.Apply(groupGrid);
            Text = $"Release '{set.Name}'";
            setLabel.Text = string.IsNullOrWhiteSpace(set.Customer)
                ? $"{set.Name} — revision {set.Revision}"
                : $"{set.Name} ({set.Customer}) — revision {set.Revision}";
            BuildPlan();
        }

        private void Options_Changed(object sender, EventArgs e)
        {
            if (!running && Result == null)
                BuildPlan();
        }

        // Commit a checkbox click at once so the summary follows the tick.
        private void GroupGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (groupGrid.IsCurrentCellDirty && groupGrid.CurrentCell?.OwningColumn == includeColumn)
                groupGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void GroupGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (populating || e.RowIndex < 0 || e.ColumnIndex != includeColumn.Index)
                return;
            var row = groupGrid.Rows[e.RowIndex];
            if (row.Tag is SetReleaseGroup group)
            {
                if (row.Cells[includeColumn.Index].Value is true)
                    excluded.Remove(group.Key);
                else
                    excluded.Add(group.Key);
            }
            UpdateCommands();
        }

        private async void ReleaseButton_Click(object sender, EventArgs e)
        {
            if (Result is { Release: null, ReleaseError.Length: > 0 })
                await RetryRecordAsync();
            else
                await ReleaseAsync();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            if (running)
            {
                cancellation?.Cancel();
                statusLabel.Text = "Stopping after the nest being uploaded...";
                closeButton.Enabled = false;
                return;
            }

            Close();
        }

        private void ReleaseSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Nests may be on their way to the server; stop between uploads, never mid-way.
            if (!running)
                return;
            e.Cancel = true;
            cancellation?.Cancel();
        }

        /// <summary>The groups ticked for release, in plan order.</summary>
        internal IReadOnlyList<SetReleaseGroup> ChosenGroups() =>
            plan.Groups.Where(g => g.CanRelease && !excluded.Contains(g.Key)).ToList();

        /// <summary>
        /// Confirms, then makes one Draft nest per ticked group, opens each in its own tab and
        /// records the release. Every group's outcome is shown in the grid.
        /// </summary>
        internal async Task ReleaseAsync()
        {
            if (running || Result != null || !groupGrid.EndEdit())
                return;
            var chosen = ChosenGroups();
            if (chosen.Count == 0)
                return;
            var multiplier = plan.Lines[0].Quantity;
            if (ShowMessage($"Create {chosen.Count} Draft nest{(chosen.Count == 1 ? "" : "s")} from set '{set.Name}'"
                    + (multiplier == 1 ? "" : $" ×{multiplier}") + "? Each opens in its own tab.",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            running = true;
            cancellation = new CancellationTokenSource();
            closeButton.Text = "Cancel";
            UpdateCommands();
            session = new SetReleaseSession(sets, library, nests, serverUrl, ReadPackage, WriteNest, CreateNest);
            var progress = new Progress<SetReleaseProgress>(ShowProgress);
            try
            {
                Result = await session.RunAsync(plan, chosen, Environment.UserName, notesBox.Text, progress, cancellation.Token);
            }
            finally
            {
                running = false;
                if (!IsDisposed)
                {
                    closeButton.Text = "Close";
                    closeButton.Enabled = true;
                }
            }

            foreach (var created in Result.Created)
                openNest(created.Nest, created.Record);
            if (IsDisposed)
                return;
            ShowOutcome();
        }

        /// <summary>Posts the release again after every nest was created but recording it failed.</summary>
        internal async Task RetryRecordAsync()
        {
            if (running || session == null || !session.HasPendingRelease)
                return;
            running = true;
            UpdateCommands();
            statusLabel.Text = "Recording the release...";
            try
            {
                var release = await session.RetryReleaseAsync();
                Result = Result.WithRelease(release);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Net.Http.HttpRequestException
                || ex is TaskCanceledException || ex is System.Text.Json.JsonException)
            {
                if (!IsDisposed)
                    ShowMessage($"The release is still not recorded: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                running = false;
            }

            if (!IsDisposed)
                ShowOutcome();
        }

        private void BuildPlan()
        {
            plan = SetReleasePlan.Build(set, (int)multiplierBox.Value, includeArchivedCheckBox.Checked);
            populating = true;
            groupGrid.Rows.Clear();
            foreach (var group in plan.Groups)
            {
                var row = new DataGridViewRow { Tag = group };
                row.CreateCells(groupGrid,
                    group.CanRelease && !excluded.Contains(group.Key),
                    group.IsUnassigned ? "(none)" : group.MaterialName,
                    group.MaterialGrade,
                    group.IsUnassigned ? "" : group.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                    group.IsUnassigned ? "" : UnitsHelper.GetShortString(group.Units),
                    group.IsUnassigned ? "" : group.CutType.ToString(),
                    group.Items.Count,
                    group.TotalQuantity,
                    group.CanRelease ? string.Join(" ", group.Warnings) : group.Refusal);
                // A refused group cannot be ticked.
                row.Cells[includeColumn.Index].ReadOnly = !group.CanRelease;
                groupGrid.Rows.Add(row);
            }
            populating = false;

            var skipped = plan.SkippedArchived;
            skippedLabel.Visible = skipped.Count > 0;
            skippedLabel.Text = skipped.Count == 0 ? "" :
                $"{skipped.Count} archived drawing{(skipped.Count == 1 ? " is" : "s are")} left out: "
                + string.Join(", ", skipped.Take(5).Select(m => $"'{m.Drawing!.Name}'"))
                + (skipped.Count > 5 ? $" and {skipped.Count - 5} more" : "")
                + ". Tick Include archived drawings to release them.";
            UpdateCommands();
        }

        private void ShowProgress(SetReleaseProgress progress)
        {
            // Progress is posted, so a report can arrive after the run ended; the outcome wins.
            if (IsDisposed || !running)
                return;
            statusLabel.Text = progress.Stage switch
            {
                SetReleaseStage.Preparing => $"Preparing nest {progress.Index + 1} of {progress.Count}: {Describe(progress.Group)}...",
                SetReleaseStage.Uploading => $"Uploading nest {progress.Index + 1} of {progress.Count}: {Describe(progress.Group)}...",
                _ => "Recording the release...",
            };
        }

        private void ShowOutcome()
        {
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                var outcome = Result.Groups.FirstOrDefault(g => ReferenceEquals(g.Group, row.Tag));
                if (outcome == null)
                    continue;
                row.Cells[statusColumn.Index].Value = outcome.Outcome switch
                {
                    SetReleaseGroupOutcome.Created => $"Created {outcome.Record!.Name}"
                        + (outcome.Warnings.Count == 0 ? "" : " — " + string.Join(" ", outcome.Warnings)),
                    SetReleaseGroupOutcome.Refused => "Refused: " + outcome.Error,
                    SetReleaseGroupOutcome.Failed => "Failed: " + outcome.Error,
                    _ => "Not started",
                };
            }

            var created = Result.Created.Count;
            var nestsText = $"{created} nest{(created == 1 ? "" : "s")}";
            if (Result.Release != null)
                statusLabel.Text = $"Released: {nestsText} created and opened, and the release recorded.";
            else if (Result.ReleaseError.Length > 0)
                statusLabel.Text = Result.ReleaseError + " Choose Record Release to try again.";
            else if (Result.Canceled)
                statusLabel.Text = $"Canceled: {nestsText} created; no release recorded.";
            else
                statusLabel.Text = created == 0
                    ? "Nothing was created. See the Status column."
                    : $"Stopped: {nestsText} created; no release recorded. See the Status column.";
            UpdateCommands();
        }

        private void UpdateCommands()
        {
            var chosen = plan == null ? Array.Empty<SetReleaseGroup>() : ChosenGroups();
            var editable = !running && Result == null;
            multiplierBox.Enabled = editable;
            includeArchivedCheckBox.Enabled = editable;
            notesBox.ReadOnly = !editable;
            includeColumn.ReadOnly = !editable;
            if (editable)
            {
                foreach (DataGridViewRow row in groupGrid.Rows)
                    row.Cells[includeColumn.Index].ReadOnly = row.Tag is SetReleaseGroup { CanRelease: false };
            }

            var retry = Result is { Release: null, ReleaseError.Length: > 0 };
            releaseButton.Text = retry ? "Record Release" : "Release";
            releaseButton.Enabled = !running && (retry || (editable && chosen.Count > 0));
            var parts = chosen.Sum(g => (long)g.TotalQuantity);
            summaryLabel.Text = plan == null || plan.Groups.Count == 0
                ? "This set has nothing to release."
                : $"{chosen.Count} of {plan.Groups.Count} group{(plan.Groups.Count == 1 ? "" : "s")} chosen: "
                    + $"{chosen.Count} Draft nest{(chosen.Count == 1 ? "" : "s")}, {parts:N0} part{(parts == 1 ? "" : "s")}";
        }

        private static string Describe(SetReleaseGroup group) =>
            group == null ? "" : string.Join(" ", new[]
            {
                group.MaterialName,
                group.MaterialGrade,
                $"{group.Thickness.ToString("0.####", CultureInfo.CurrentCulture)} {UnitsHelper.GetShortString(group.Units)}",
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        private static LibraryPackage ReadPackage(byte[] bytes)
        {
            var contents = DrawingPackage.Read(bytes);
            return new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings);
        }

        private static byte[] WriteNest(Nest nest)
        {
            using var stream = new MemoryStream();
            new NestWriter(nest) { BestFitStore = BestFitCacheStore.Shared }.Write(stream);
            return stream.ToArray();
        }
    }
}
