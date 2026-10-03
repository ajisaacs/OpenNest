using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms;

/// <summary>
/// File &gt; Open (Database mode): browses nests on the shared nest server one bounded
/// page at a time. Filtering, sorting and paging run on the server through
/// <see cref="NestBrowseSession"/>; this form only renders its state.
/// </summary>
public sealed class SavedNestsForm : Form
{
    private const string IdColumn = "Id";
    private const string LoadingText = "Loading...";

    private readonly INestRepository repository;
    private readonly NestBrowseSession session;
    private readonly DataGridView grid;
    private readonly TextBox searchBox;
    private readonly Timer searchTimer;
    private readonly Button openButton;
    private readonly Button deleteButton;
    private readonly Button previousButton;
    private readonly Button nextButton;
    private readonly ToolStripStatusLabel statusLabel;
    private int pendingRequests;

    /// <summary>Set to the chosen record's id when the dialog closes with OK.</summary>
    public Guid SelectedId { get; private set; }

    public SavedNestsForm(INestRepository repository)
    {
        this.repository = repository;
        session = new NestBrowseSession(repository);

        Text = "Open Nest — Database";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 480);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(3),
            WrapContents = false,
        };
        var searchLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(3, 7, 0, 0) };
        searchBox = new TextBox { Width = 300, MaxLength = NestQuery.MaxSearchLength };
        searchTimer = new Timer { Interval = 300 };
        searchTimer.Tick += async (_, _) =>
        {
            searchTimer.Stop();
            await RunAsync(() => session.SetSearchAsync(searchBox.Text));
        };
        searchBox.TextChanged += (_, _) =>
        {
            searchTimer.Stop();
            searchTimer.Start();
        };
        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        refreshButton.Click += async (_, _) => await RunAsync(session.RefreshAsync);
        previousButton = new Button { Text = "< Previous", AutoSize = true, Enabled = false };
        previousButton.Click += async (_, _) => await RunAsync(session.PreviousPageAsync);
        nextButton = new Button { Text = "Next >", AutoSize = true, Enabled = false };
        nextButton.Click += async (_, _) => await RunAsync(session.NextPageAsync);
        topPanel.Controls.Add(searchLabel);
        topPanel.Controls.Add(searchBox);
        topPanel.Controls.Add(refreshButton);
        topPanel.Controls.Add(previousButton);
        topPanel.Controls.Add(nextButton);

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        };
        AddColumn("Name", NestSortField.Name, typeof(string));
        AddColumn("Customer", NestSortField.Customer, typeof(string));
        AddColumn("Status", NestSortField.Status, typeof(string));
        AddColumn("Material", NestSortField.Material, typeof(string));
        AddColumn("Date Created", NestSortField.DateCreated, typeof(DateTime));
        AddColumn("Date Modified", NestSortField.DateModified, typeof(DateTime));
        AddColumn("Thickness", NestSortField.Thickness, typeof(double));
        AddColumn("Plates", NestSortField.PlateCount, typeof(int));
        AddColumn("Parts", NestSortField.PartCount, typeof(int));
        AddColumn("Made By", NestSortField.MadeBy, typeof(string));
        AddColumn("Comments", NestSortField.Comments, typeof(string));
        AddColumn("File Size", NestSortField.FileSize, typeof(long));
        AddColumn("Saved", NestSortField.SavedAt, typeof(DateTime));
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = IdColumn,
            ValueType = typeof(Guid),
            Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        // Sorting a page locally would misrepresent the archive; the server sorts all matches.
        grid.ColumnHeaderMouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left && grid.Columns[e.ColumnIndex].Tag is NestSortField field)
                await RunAsync(() => session.SortByAsync(field));
        };
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                Open_Click(this, EventArgs.Empty);
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        deleteButton = new Button { Text = "Delete", AutoSize = true, Enabled = false };
        deleteButton.Click += Delete_Click;
        openButton = new Button { Text = "Open", AutoSize = true, Enabled = false };
        openButton.Click += Open_Click;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(openButton);
        buttons.Controls.Add(deleteButton);

        var statusStrip = new StatusStrip { SizingGrip = false };
        statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(statusLabel);

        grid.SelectionChanged += (_, _) =>
        {
            var hasSelection = grid.SelectedRows.Count > 0;
            openButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
        };

        Controls.Add(grid);
        Controls.Add(topPanel);
        Controls.Add(buttons);
        Controls.Add(statusStrip);
        CancelButton = cancel;

        Shown += async (_, _) => await RunAsync(session.RefreshAsync);
    }

    private void AddColumn(string name, NestSortField sortField, Type valueType)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = name,
            ValueType = valueType,
            SortMode = DataGridViewColumnSortMode.Programmatic,
            Tag = sortField,
        });
    }

    /// <summary>
    /// Sends one browse request and renders its page. A request superseded by a newer one
    /// renders nothing; a failure clears the rows and shows the error in the status line.
    /// </summary>
    private async Task RunAsync(Func<Task<bool>> request)
    {
        pendingRequests++;
        UpdateNavigation();
        statusLabel.Text = LoadingText;
        try
        {
            if (await request() && !IsDisposed)
                Populate(session.Page);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                grid.Rows.Clear();
                statusLabel.Text = $"Could not load nests from the server: {ex.Message}";
            }
        }
        finally
        {
            pendingRequests--;
            if (!IsDisposed)
            {
                UpdateNavigation();
                // A request that was declined or superseded renders nothing of its own.
                if (pendingRequests == 0 && statusLabel.Text == LoadingText)
                    statusLabel.Text = session.Summary;
            }
        }
    }

    private void UpdateNavigation()
    {
        var idle = pendingRequests == 0;
        previousButton.Enabled = idle && session.CanGoPrevious;
        nextButton.Enabled = idle && session.CanGoNext;
    }

    private void Populate(NestPage page)
    {
        grid.Rows.Clear();
        foreach (var record in page.Items)
        {
            grid.Rows.Add(
                record.Name,
                record.Customer,
                FormatStatus(record.Status),
                record.Material,
                record.DateCreated,
                record.DateModified,
                record.Thickness,
                record.PlateCount,
                record.PartCount,
                record.MadeBy,
                record.Comments,
                record.FileSize,
                record.SavedAt,
                record.Id
            );
        }

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.Tag is NestSortField field && field == session.Sort
                ? session.Descending ? SortOrder.Descending : SortOrder.Ascending
                : SortOrder.None;
        }

        statusLabel.Text = session.Summary;
    }

    private static string FormatStatus(NestStatus status) => status switch
    {
        NestStatus.Quote => "Quote",
        NestStatus.ToBeCut => "To Be Cut",
        NestStatus.HasBeenCut => "Has Been Cut",
        _ => status.ToString(),
    };

    private void Open_Click(object sender, EventArgs e)
    {
        if (grid.SelectedRows.Count == 0)
            return;
        SelectedId = (Guid)grid.SelectedRows[0].Cells[IdColumn].Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void Delete_Click(object sender, EventArgs e)
    {
        if (grid.SelectedRows.Count == 0)
            return;

        var id = (Guid)grid.SelectedRows[0].Cells[IdColumn].Value;
        var name = grid.SelectedRows[0].Cells["Name"].Value?.ToString() ?? "";

        var confirm = MessageBox.Show(
            this,
            $"Delete '{name}' from the server? This cannot be undone.",
            "Delete Nest",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning
        );
        if (confirm != DialogResult.Yes)
            return;

        deleteButton.Enabled = false;
        try
        {
            await repository.DeleteAsync(id);
            if (!IsDisposed)
                await RunAsync(session.RefreshAsync);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(
                    this,
                    $"Could not delete the nest: {ex.Message}",
                    "Delete Nest",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
        }
        finally
        {
            if (!IsDisposed)
                deleteButton.Enabled = grid.SelectedRows.Count > 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            searchTimer.Dispose();
            session.Dispose();
        }

        base.Dispose(disposing);
    }
}
