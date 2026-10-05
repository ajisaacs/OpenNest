using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.IO;
using Timer = System.Windows.Forms.Timer;

namespace OpenNest.Forms;

/// <summary>
/// File &gt; Open (Database mode). The upper list browses saved nests on the shared nest
/// server one bounded page at a time; filtering, sorting and paging run on the server
/// through <see cref="NestBrowseSession"/>. The lower tabs show the plates and drawings of
/// the highlighted nest, read from its archive through <see cref="NestDetailsSession"/>.
/// This form only renders their state.
/// </summary>
public sealed class SavedNestsForm : Form
{
    private const string IdColumn = "Id";
    private const string PlateSizeColumn = "PlateSize";
    private const string AreaColumn = "Area";
    private const string LoadingText = "Loading...";
    private const string LoadingDetailsText = "Loading details...";
    private const string WindowTitle = "Open Nest — Database";

    private static readonly Font SectionFont = new(SystemFonts.MessageBoxFont.FontFamily, 14f);

    private readonly INestRepository repository;
    private readonly string serverUrl;
    private readonly NestBrowseSession session;
    private readonly NestDetailsSession details;
    private readonly SplitContainer split;
    private readonly DataGridView nestGrid;
    private readonly DataGridView platesGrid;
    private readonly DataGridView drawingsGrid;
    private readonly TabControl detailsTabs;
    private readonly Label detailsStatus;
    private readonly TextBox searchBox;
    private readonly Timer searchTimer;
    private readonly Timer detailsTimer;
    private readonly Button previousButton;
    private readonly Button nextButton;
    private readonly Label summaryLabel;
    private readonly ToolStripMenuItem openMenuItem;
    private readonly ToolStripMenuItem deleteMenuItem;
    private bool deleting;

    /// <summary>Set to the chosen record's id when the dialog closes with OK.</summary>
    public Guid SelectedId { get; private set; }

    public SavedNestsForm(INestRepository repository, string serverUrl = null)
    {
        this.repository = repository;
        this.serverUrl = serverUrl ?? "";
        session = new NestBrowseSession(repository);
        details = new NestDetailsSession(LoadDetailsAsync);

        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(800, 560);
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Nests: one server page, sorted by the server when a header is clicked.
        nestGrid = CreateGrid();
        nestGrid.RowHeadersWidth = 64;
        AddNestColumn("Nest name", NestSortField.Name, 170);
        AddNestColumn("Status", NestSortField.Status, 100);
        AddNestColumn("Modified", NestSortField.DateModified, 140);
        AddNestColumn("Plates", NestSortField.PlateCount, 60, alignRight: true);
        AddNestColumn("Parts", NestSortField.PartCount, 60, alignRight: true);
        AddNestColumn("Comment", NestSortField.Comments, 260);
        AddNestColumn("Customer", NestSortField.Customer, 160);
        AddNestColumn("Made by", NestSortField.MadeBy, 90);
        AddNestColumn("Material", NestSortField.Material, 150);
        AddNestColumn("Thickness", NestSortField.Thickness, 80, alignRight: true);
        AddNestColumn("Created", NestSortField.DateCreated, 140);
        AddNestColumn("Saved", NestSortField.SavedAt, 140);
        AddNestColumn("File size", NestSortField.FileSize, 80, alignRight: true);
        nestGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = IdColumn,
            ValueType = typeof(Guid),
            Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        // Sorting a page locally would misrepresent the archive; the server sorts all matches.
        nestGrid.ColumnHeaderMouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left && nestGrid.Columns[e.ColumnIndex].Tag is NestSortField field)
                await RunAsync(() => session.SortByAsync(field));
        };
        nestGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                OpenSelected();
        };
        nestGrid.CellMouseDown += (_, e) =>
        {
            // Right-click highlights the row under the pointer before its menu opens.
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                nestGrid.CurrentCell = nestGrid.Rows[e.RowIndex].Cells[0];
        };
        nestGrid.SelectionChanged += (_, _) =>
        {
            UpdateCommands();
            ScheduleDetails();
        };

        var menu = new ContextMenuStrip();
        openMenuItem = new ToolStripMenuItem("Open", null, (_, _) => OpenSelected()) { ShortcutKeyDisplayString = "Enter" };
        deleteMenuItem = new ToolStripMenuItem("Delete...", null, async (_, _) => await DeleteSelectedAsync());
        var refreshMenuItem = new ToolStripMenuItem("Refresh", null, async (_, _) => await RunAsync(session.RefreshAsync));
        menu.Items.AddRange(new ToolStripItem[] { openMenuItem, deleteMenuItem, new ToolStripSeparator(), refreshMenuItem });
        nestGrid.ContextMenuStrip = menu;

        // Details of the highlighted nest.
        platesGrid = CreateGrid();
        platesGrid.RowHeadersVisible = false;
        AddColumn(platesGrid, "Number", "#", 50, typeof(int), alignRight: true);
        AddColumn(platesGrid, "Duplicates", "Duplicates", 90, typeof(int), alignRight: true);
        AddColumn(platesGrid, PlateSizeColumn, "Plate size", 140, typeof(string));
        AddColumn(platesGrid, "Parts", "Parts", 70, typeof(int), alignRight: true);
        AddColumn(platesGrid, "Drawings", "Drawings", 80, typeof(int), alignRight: true);
        AddColumn(platesGrid, "Utilization", "Utilization", 90, typeof(double), alignRight: true, format: "P1");

        drawingsGrid = CreateGrid();
        drawingsGrid.RowHeadersVisible = false;
        AddColumn(drawingsGrid, "Drawing", "Drawing", 220, typeof(string));
        AddColumn(drawingsGrid, "Customer", "Customer", 160, typeof(string));
        AddColumn(drawingsGrid, "Required", "Required", 80, typeof(int), alignRight: true);
        AddColumn(drawingsGrid, "Nested", "Nested", 80, typeof(int), alignRight: true);
        AddColumn(drawingsGrid, "Remaining", "Remaining", 90, typeof(int), alignRight: true);
        AddColumn(drawingsGrid, AreaColumn, "Area", 100, typeof(double), alignRight: true, format: "0.###");

        detailsTabs = new TabControl { Dock = DockStyle.Fill };
        detailsTabs.TabPages.Add(CreateTab("Plates", platesGrid));
        detailsTabs.TabPages.Add(CreateTab("Drawings", drawingsGrid));

        detailsStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
        };
        var detailsHeader = new Panel { Dock = DockStyle.Top, Height = 32 };
        detailsHeader.Controls.Add(detailsStatus);
        detailsHeader.Controls.Add(SectionLabel("Details", DockStyle.Left));

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
        };
        split.Panel1.Controls.Add(nestGrid);
        split.Panel1.Controls.Add(SectionLabel("Nests", DockStyle.Top));
        split.Panel2.Controls.Add(detailsTabs);
        split.Panel2.Controls.Add(detailsHeader);

        // Find bar and paging.
        searchBox = new TextBox
        {
            Width = 320,
            MaxLength = NestQuery.MaxSearchLength,
            PlaceholderText = "Name, customer, material, made by, comment or status",
        };
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
        detailsTimer = new Timer { Interval = 250 };
        detailsTimer.Tick += async (_, _) =>
        {
            detailsTimer.Stop();
            if (SelectedNestId() is Guid id)
                await ShowDetailsAsync(id);
        };

        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        refreshButton.Click += async (_, _) => await RunAsync(session.RefreshAsync);
        previousButton = new Button { Text = "\u25C0", Width = 32, Enabled = false };
        previousButton.Click += async (_, _) => await RunAsync(session.PreviousPageAsync);
        nextButton = new Button { Text = "\u25B6", Width = 32, Enabled = false };
        nextButton.Click += async (_, _) => await RunAsync(session.NextPageAsync);
        summaryLabel = new Label { AutoSize = true, Margin = new Padding(12, 8, 3, 0) };

        var findBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        findBar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        findBar.Controls.Add(searchBox);
        findBar.Controls.Add(refreshButton);
        findBar.Controls.Add(previousButton);
        findBar.Controls.Add(nextButton);
        findBar.Controls.Add(summaryLabel);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(3, 3, 3, 6),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.Controls.Add(findBar);

        Controls.Add(split);
        Controls.Add(bottom);

        Load += (_, _) =>
        {
            // Minimum sizes are applied once the container has its real height; setting them
            // on the default-sized container throws.
            split.Panel1MinSize = 120;
            split.Panel2MinSize = 120;
            split.SplitterDistance = System.Math.Max(split.Panel1MinSize, split.Height * 3 / 5);
        };
        Shown += async (_, _) =>
        {
            nestGrid.Focus();
            await RunAsync(session.RefreshAsync);
        };
        UpdateCommands();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Enter when searchBox.Focused:
                // Run the typed search now instead of opening a row from the previous results.
                searchTimer.Stop();
                _ = RunAsync(() => session.SetSearchAsync(searchBox.Text));
                nestGrid.Focus();
                return true;
            case Keys.Enter:
                OpenSelected();
                return true;
            case Keys.Escape:
                DialogResult = DialogResult.Cancel;
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = Color.Silver,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            StandardTab = true,
        };
        grid.RowTemplate.Height = 22;
        grid.DefaultCellStyle.SelectionBackColor = Color.Black;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.RowHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = Color.Black;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return grid;
    }

    private static DataGridViewTextBoxColumn AddColumn(
        DataGridView grid,
        string name,
        string header,
        int width,
        Type valueType,
        bool alignRight = false,
        string format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ValueType = valueType,
        };
        if (alignRight)
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (format != null)
            column.DefaultCellStyle.Format = format;
        grid.Columns.Add(column);
        return column;
    }

    private void AddNestColumn(string header, NestSortField sortField, int width, bool alignRight = false)
    {
        // Cells hold display text: the server has already sorted the page.
        var column = AddColumn(nestGrid, header, header, width, typeof(string), alignRight);
        column.SortMode = DataGridViewColumnSortMode.Programmatic;
        column.Tag = sortField;
    }

    private static TabPage CreateTab(string text, Control content)
    {
        var page = new TabPage(text) { Padding = new Padding(0) };
        page.Controls.Add(content);
        return page;
    }

    private static Label SectionLabel(string text, DockStyle dock) =>
        new()
        {
            Text = text,
            Font = SectionFont,
            Dock = dock,
            AutoSize = dock == DockStyle.Left,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 12, 0),
        };

    /// <summary>
    /// Sends one browse request and renders its page. A request superseded by a newer one
    /// renders nothing; a failure clears the rows and shows the error in the summary line.
    /// Navigation follows only the latest request (<see cref="NestBrowseSession.IsLoading"/>).
    /// </summary>
    private async Task RunAsync(Func<Task<bool>> request)
    {
        summaryLabel.Text = LoadingText;
        try
        {
            var started = request();
            UpdateNavigation();
            if (await started && !IsDisposed)
                Populate(session.Page);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                nestGrid.Rows.Clear();
                summaryLabel.Text = $"Could not load nests from the server: {ex.Message}";
                UpdateTitle("");
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                UpdateNavigation();
                // A request that was declined or superseded renders nothing of its own.
                if (!session.IsLoading && summaryLabel.Text == LoadingText)
                    summaryLabel.Text = session.Summary;
            }
        }
    }

    private void UpdateNavigation()
    {
        previousButton.Enabled = !session.IsLoading && session.CanGoPrevious;
        nextButton.Enabled = !session.IsLoading && session.CanGoNext;
    }

    private void UpdateCommands()
    {
        var hasSelection = SelectedNestId() != null;
        openMenuItem.Enabled = hasSelection;
        deleteMenuItem.Enabled = hasSelection && !deleting;
    }

    private void UpdateTitle(string summary)
    {
        var parts = new[] { WindowTitle, serverUrl, summary };
        Text = string.Join("    ", Array.FindAll(parts, part => part.Length > 0));
    }

    private void Populate(NestPage page)
    {
        nestGrid.Rows.Clear();
        for (var i = 0; i < page.Items.Count; i++)
        {
            var record = page.Items[i];
            var index = nestGrid.Rows.Add(
                record.Name,
                FormatStatus(record.Status),
                FormatDate(record.DateModified),
                record.PlateCount.ToString("N0", CultureInfo.CurrentCulture),
                record.PartCount.ToString("N0", CultureInfo.CurrentCulture),
                record.Comments,
                record.Customer,
                record.MadeBy,
                record.Material,
                record.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                FormatDate(record.DateCreated),
                FormatDate(record.SavedAt),
                FormatFileSize(record.FileSize),
                record.Id
            );
            // Row numbers count through the whole filtered list, not just this page.
            nestGrid.Rows[index].HeaderCell.Value = (page.Offset + i + 1).ToString("N0", CultureInfo.CurrentCulture);
        }

        foreach (DataGridViewColumn column in nestGrid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.Tag is NestSortField field && field == session.Sort
                ? session.Descending ? SortOrder.Descending : SortOrder.Ascending
                : SortOrder.None;
        }

        if (nestGrid.Rows.Count > 0 && nestGrid.CurrentCell == null)
            nestGrid.CurrentCell = nestGrid.Rows[0].Cells[0];

        summaryLabel.Text = session.Summary;
        UpdateTitle(session.Summary);
    }

    private static string FormatStatus(NestStatus status) => status switch
    {
        NestStatus.Quote => "Quote",
        NestStatus.ToBeCut => "To Be Cut",
        NestStatus.HasBeenCut => "Has Been Cut",
        _ => status.ToString(),
    };

    private static string FormatDate(DateTime value) =>
        value == default ? "" : value.ToString("g", CultureInfo.CurrentCulture);

    private static string FormatFileSize(long bytes) =>
        $"{System.Math.Ceiling(bytes / 1024.0).ToString("N0", CultureInfo.CurrentCulture)} KB";

    private Guid? SelectedNestId() =>
        nestGrid.SelectedRows.Count > 0 && nestGrid.SelectedRows[0].Cells[IdColumn].Value is Guid id ? id : null;

    /// <summary>
    /// Shows the highlighted nest's details after a short pause, so arrowing through the
    /// list downloads only the nest the highlight settles on.
    /// </summary>
    private void ScheduleDetails()
    {
        detailsTimer.Stop();
        var id = SelectedNestId();
        if (id == null)
        {
            details.Clear();
            ShowDetailsMessage("");
            return;
        }

        if (id == details.NestId)
        {
            // Back on the nest already shown or loading (for example after arrowing away and
            // back before the pause ended): keep that result rather than downloading it again.
            if (details.Details != null)
            {
                PopulateDetails(details.Details);
                return;
            }

            if (details.IsLoading)
            {
                ShowDetailsMessage(LoadingDetailsText);
                return;
            }
        }

        ShowDetailsMessage(LoadingDetailsText);
        detailsTimer.Start();
    }

    private async Task ShowDetailsAsync(Guid id)
    {
        try
        {
            if (await details.LoadAsync(id) && !IsDisposed)
                PopulateDetails(details.Details);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                ShowDetailsMessage($"Could not load details: {ex.Message}");
        }
    }

    /// <summary>Downloads one nest's archive and reads its plates and drawings off the UI thread.</summary>
    private async Task<NestDetails> LoadDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await repository.GetFileAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("This nest no longer exists on the server.");
        return await Task.Run(
            () =>
            {
                using var stream = new MemoryStream(file);
                return NestDetails.FromNest(new NestReader(stream).Read());
            },
            cancellationToken);
    }

    private void ShowDetailsMessage(string message)
    {
        platesGrid.Rows.Clear();
        drawingsGrid.Rows.Clear();
        detailsStatus.Text = message;
    }

    private void PopulateDetails(NestDetails nestDetails)
    {
        var unit = nestDetails.Units == Units.Millimeters ? "mm" : "in";
        platesGrid.Columns[PlateSizeColumn].HeaderText = $"Plate size ({unit})";
        drawingsGrid.Columns[AreaColumn].HeaderText = $"Area ({unit}\u00B2)";

        ShowDetailsMessage(
            $"{nestDetails.Plates.Count:N0} plate(s), {nestDetails.Drawings.Count:N0} drawing(s)");
        foreach (var plate in nestDetails.Plates)
        {
            platesGrid.Rows.Add(
                plate.Number,
                plate.Duplicates,
                $"{plate.Width.ToString("0.###", CultureInfo.CurrentCulture)} x {plate.Length.ToString("0.###", CultureInfo.CurrentCulture)}",
                plate.PartCount,
                plate.DrawingCount,
                plate.Utilization);
        }

        foreach (var drawing in nestDetails.Drawings)
        {
            drawingsGrid.Rows.Add(
                drawing.Name,
                drawing.Customer,
                drawing.Required,
                drawing.Nested,
                drawing.Remaining,
                drawing.Area);
        }
    }

    private void OpenSelected()
    {
        if (SelectedNestId() is not Guid id)
            return;
        SelectedId = id;
        DialogResult = DialogResult.OK;
    }

    private async Task DeleteSelectedAsync()
    {
        if (deleting || SelectedNestId() is not Guid id)
            return;

        var name = nestGrid.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
        var confirm = MessageBox.Show(
            this,
            $"Delete '{name}' from the server? This cannot be undone.",
            "Delete Nest",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning
        );
        if (confirm != DialogResult.Yes)
            return;

        deleting = true;
        UpdateCommands();
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
            deleting = false;
            if (!IsDisposed)
                UpdateCommands();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            searchTimer.Dispose();
            detailsTimer.Dispose();
            session.Dispose();
            details.Dispose();
        }

        base.Dispose(disposing);
    }
}
