using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;

namespace OpenNest.Forms;

/// <summary>Read-only revision metadata/downloads with separate, confirmed promotion and recovery.</summary>
public sealed class NestHistoryForm : Form
{
    private readonly INestRepository repository;
    private readonly string serverUrl;
    private readonly NestHistorySession session;
    private readonly bool recovery;
    private readonly DataGridView records = Grid();
    private readonly DataGridView revisions = Grid();
    private readonly Label status = new() { AutoSize = true };
    private readonly Button previous = new() { Text = "Previous versions", AutoSize = true };
    private readonly Button next = new() { Text = "Next versions", AutoSize = true };
    private readonly Button download = new() { Text = "Download selected version...", AutoSize = true };
    private readonly Button inspect = new() { Text = "Open read-only...", AutoSize = true };
    private readonly Button restore = new() { Text = "Restore selected version...", AutoSize = true };
    private readonly Button recover = new() { Text = "Recover selected nest...", AutoSize = true };
    private readonly Button deletedPrevious = new() { Text = "Previous nests", AutoSize = true };
    private readonly Button deletedNext = new() { Text = "Next nests", AutoSize = true };
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox search = new() { Width = 220, MaxLength = NestQuery.MaxSearchLength, PlaceholderText = "Find deleted nests" };
    private Guid selectedId;
    private int deletedOffset;
    private int readGeneration;
    private bool populating;
    private bool busy;
    public bool Changed { get; private set; }
    public event EventHandler HistoryChanged;

    /// <summary>Raised when the highlighted version changes on a loaded history page.</summary>
    public event EventHandler RevisionSelected;

    /// <summary>The highlighted version when it is not the current one; otherwise null.</summary>
    public NestRecord SelectedHistoricalRevision =>
        session.Current != null && SelectedRevision is NestRecord record && record.Revision != session.Current.Revision
            ? record
            : null;

    public NestHistoryForm(INestRepository repository, string serverUrl, Guid id = default, bool embedded = false)
    {
        this.repository = repository;
        this.serverUrl = serverUrl;
        selectedId = id;
        recovery = id == Guid.Empty;
        session = new NestHistorySession(repository, serverUrl);
        Text = recovery ? "Deleted Nests — Recovery" : "Nest Version History";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1050, 650);
        MinimumSize = new Size(800, 480);
        ShowInTaskbar = false;
        if (embedded)
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            MinimumSize = Size.Empty;
            CancelButton = null;
        }
        records.Columns.Add("Name", "Deleted nest");
        records.Columns.Add("Version", "Current version");
        records.Columns.Add("Saved", "Saved");
        foreach (var caption in new[] { "Version", "State", "Saved", "Name", "Status", "Comments", "Restored from", "Bytes" })
            revisions.Columns.Add(caption, caption);
        revisions.Columns["Comments"].Width = 220;
        records.SelectionChanged += async (_, _) =>
        {
            if (!populating && !busy && records.SelectedRows.Count > 0
                && records.SelectedRows[0].Tag is NestRecord record)
            {
                selectedId = record.Id;
                await ReadAsync(0);
            }
        };
        revisions.SelectionChanged += (_, _) =>
        {
            UpdateButtons();
            if (!populating && session.Current != null)
                RevisionSelected?.Invoke(this, EventArgs.Empty);
        };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += async (_, _) => { if (recovery) await ReadDeletedAsync(); else await ReadAsync(0); };
        previous.Click += async (_, _) => await ReadAsync(System.Math.Max(0, session.Page.Offset - NestHistorySession.PageSize));
        next.Click += async (_, _) => await ReadAsync(session.Page.Offset + NestHistorySession.PageSize);
        download.Click += async (_, _) => await DownloadAsync();
        inspect.Click += async (_, _) => await InspectAsync();
        restore.Click += async (_, _) => await MutateAsync(false);
        recover.Click += async (_, _) => await MutateAsync(true);
        deletedPrevious.Click += async (_, _) => { deletedOffset = System.Math.Max(0, deletedOffset - 50); await ReadDeletedAsync(); };
        deletedNext.Click += async (_, _) => { deletedOffset += 50; await ReadDeletedAsync(); };
        var find = new Button { Text = "Find", AutoSize = true };
        find.Click += async (_, _) => { deletedOffset = 0; await ReadDeletedAsync(); };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        if (!embedded) CancelButton = close;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bar.Controls.AddRange(new Control[] { refresh, previous, next, inspect, download, restore, recover });
        if (!embedded) bar.Controls.Add(close);
        recover.Visible = recovery;
        restore.Visible = !recovery;
        var heading = new Label
        {
            Text = "Historical versions are read-only. Download opens no editor and changes nothing on the server. Restore adds a new version to the same nest.",
            Dock = DockStyle.Top,
            Height = 42,
        };
        var statusPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        statusPanel.Controls.Add(status);
        Controls.Add(revisions);
        if (recovery)
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 180 };
            var findBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            findBar.Controls.AddRange(new Control[] { search, find, deletedPrevious, deletedNext });
            top.Controls.Add(records);
            top.Controls.Add(findBar);
            Controls.Add(top);
        }
        Controls.Add(heading);
        Controls.Add(statusPanel);
        Controls.Add(bar);
        Shown += async (_, _) => { if (recovery) await ReadDeletedAsync(); else await ReadAsync(0); };
        FormClosing += (_, _) => lifetime.Cancel();
        UpdateButtons();
    }

    private static DataGridView Grid() => GridStyle.Apply(new DataGridView
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
    });

    private NestRecord SelectedRevision => revisions.SelectedRows.Count > 0 ? revisions.SelectedRows[0].Tag as NestRecord : null;

    private void UpdateButtons()
    {
        var ready = !busy && session.Current != null;
        previous.Enabled = ready && session.Page?.Offset > 0;
        next.Enabled = ready && session.Page != null && session.Page.Offset + session.Page.Items.Count < session.Page.Total;
        download.Enabled = ready && SelectedRevision != null;
        inspect.Enabled = download.Enabled;
        restore.Enabled = ready && SelectedRevision != null && SelectedRevision.Revision != session.Current.Revision;
        recover.Enabled = ready && session.Deleted;
        records.Enabled = !busy;
    }

    private async Task ReadDeletedAsync()
    {
        if (busy) return;
        var run = ++readGeneration;
        selectedId = Guid.Empty;
        session.Clear();
        revisions.Rows.Clear();
        UpdateButtons();
        try
        {
            var page = await repository.QueryDeletedAsync(new NestQuery { Search = search.Text, Offset = deletedOffset, Limit = 50 }, lifetime.Token);
            if (IsDisposed || run != readGeneration) return;
            populating = true;
            records.Rows.Clear();
            foreach (var record in page.Items)
            {
                var row = records.Rows.Add(record.Name, record.Revision, record.SavedAt.ToString("g"));
                records.Rows[row].Tag = record;
            }
            populating = false;
            deletedPrevious.Enabled = page.Offset > 0;
            deletedNext.Enabled = page.Offset + page.Items.Count < page.Total;
            status.Text = $"{page.Total} deleted nests; page begins at {page.Offset + 1}.";
            if (records.SelectedRows.Count > 0 && records.SelectedRows[0].Tag is NestRecord first)
            {
                selectedId = first.Id;
                await ReadAsync(0);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && run == readGeneration) status.Text = ex.Message; }
        finally { populating = false; }
    }

    private async Task ReadAsync(int offset)
    {
        if (busy || selectedId == Guid.Empty) return;
        var run = ++readGeneration;
        revisions.Rows.Clear();
        status.Text = "Loading version history...";
        var task = session.LoadAsync(selectedId, recovery, offset, lifetime.Token);
        UpdateButtons();
        try
        {
            if (!await task || IsDisposed || run != readGeneration) return;
            populating = true;
            try
            {
                foreach (var record in session.Page.Items)
                {
                    var row = revisions.Rows.Add(record.Revision,
                        (record.Revision == session.Current.Revision ? "Current" : "Historical") + (record.Deleted ? " (deleted)" : ""),
                        record.SavedAt.ToString("g"), record.Name, record.Status, record.Comments, record.RestoredFromRevision, record.FileSize);
                    revisions.Rows[row].Tag = record;
                }
            }
            finally { populating = false; }
            RevisionSelected?.Invoke(this, EventArgs.Empty);
            status.Text = $"{serverUrl} — Nest {selectedId} — Current version {session.Current.Revision}; {session.Page.Total} versions. Displaying {offset + 1}–{offset + session.Page.Items.Count}.";
        }
        catch (Exception ex) { if (!IsDisposed && run == readGeneration) status.Text = ex.Message; }
        finally { if (!IsDisposed) UpdateButtons(); }
    }

    /// <summary>Downloads one version on the displayed page; null when the page changed meanwhile.</summary>
    public Task<byte[]> DownloadRevisionAsync(int revision) => session.DownloadAsync(revision, serverUrl, lifetime.Token);

    private async Task InspectAsync()
    {
        if (SelectedRevision is not NestRecord record || busy) return;
        busy = true;
        UpdateButtons();
        try
        {
            var bytes = await session.DownloadAsync(record.Revision, serverUrl, lifetime.Token);
            if (bytes == null || IsDisposed) return;
            var nest = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                var reader = new NestReader(stream) { BestFitStore = BestFitCacheStore.Shared };
                var loaded = reader.Read();
                if (reader.Warnings.Count > 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, reader.Warnings));
                return loaded;
            }, lifetime.Token);
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            using var preview = new NestRevisionPreviewForm(nest, record);
            preview.ShowDialog(this);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally { busy = false; if (!IsDisposed) UpdateButtons(); }
    }

    private async Task DownloadAsync()
    {
        if (SelectedRevision is not NestRecord record || busy) return;
        using var dialog = new SaveFileDialog { Filter = "Nest archive (*.nest)|*.nest", FileName = $"nest-{selectedId}-v{record.Revision}.nest" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        busy = true;
        UpdateButtons();
        try
        {
            var bytes = await session.DownloadAsync(record.Revision, serverUrl, lifetime.Token);
            if (bytes != null && !IsDisposed) await File.WriteAllBytesAsync(dialog.FileName, bytes, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally { busy = false; if (!IsDisposed) UpdateButtons(); }
    }

    private async Task MutateAsync(bool recoveryAction)
    {
        if (busy || session.Current == null || (!recoveryAction && SelectedRevision == null)) return;
        var revision = SelectedRevision?.Revision ?? 0;
        var current = session.Current.Revision;
        var text = recoveryAction
            ? $"Recover nest {selectedId} at version {current}? This unhides the same nest and adds a new version; all history is retained."
            : $"Restore version {revision} over current version {current} of nest {selectedId}? This adds a new current version with the selected bytes and metadata. No history is removed.";
        if (MessageBox.Show(this, text, recoveryAction ? "Recover Nest" : "Restore Version", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        busy = true;
        UpdateButtons();
        try
        {
            if (recoveryAction) await session.RecoverAsync(serverUrl, lifetime.Token);
            else await session.RestoreAsync(revision, serverUrl, lifetime.Token);
            Changed = true;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            busy = false;
            if (!IsDisposed) { if (recoveryAction) await ReadDeletedAsync(); else await ReadAsync(0); }
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = $"No automatic retry. Refresh to reconcile before continuing. {ex.Message}"; }
        finally { busy = false; if (!IsDisposed) UpdateButtons(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); session.Dispose(); }
        base.Dispose(disposing);
    }
}
