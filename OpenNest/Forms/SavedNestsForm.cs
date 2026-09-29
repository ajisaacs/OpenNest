using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms;

/// <summary>
/// File &gt; Open (Database mode): lists nests from the shared nest server with
/// filterable/sortable metadata columns, and lets the operator open or delete one.
/// </summary>
public sealed class SavedNestsForm : Form
{
    private readonly INestRepository repository;
    private readonly DataGridView grid;
    private readonly TextBox searchBox;
    private readonly Button openButton;
    private readonly Button deleteButton;
    private readonly Button refreshButton;
    private NestRecord[] records = Array.Empty<NestRecord>();

    /// <summary>Set to the chosen record's id when the dialog closes with OK.</summary>
    public Guid SelectedId { get; private set; }

    public SavedNestsForm(INestRepository repository)
    {
        this.repository = repository;

        Text = "Open Nest — Database";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 480);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6) };
        var searchLabel = new Label { Text = "Filter:", AutoSize = true, Location = new Point(6, 10) };
        searchBox = new TextBox { Location = new Point(50, 6), Width = 300 };
        searchBox.TextChanged += (_, _) => ApplyFilter();
        refreshButton = new Button { Text = "Refresh", AutoSize = true, Location = new Point(360, 5) };
        refreshButton.Click += async (_, _) => await LoadAsync();
        topPanel.Controls.Add(searchLabel);
        topPanel.Controls.Add(searchBox);
        topPanel.Controls.Add(refreshButton);

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

        grid.SelectionChanged += (_, _) =>
        {
            var hasSelection = grid.SelectedRows.Count > 0;
            openButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
        };

        Controls.Add(grid);
        Controls.Add(topPanel);
        Controls.Add(buttons);
        CancelButton = cancel;

        Shown += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var list = await repository.ListAsync();
            if (IsDisposed)
                return;
            records = list.ToArray();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(
                    this,
                    $"Could not load nests from the server: {ex.Message}",
                    "Open Nest — Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
        }
        finally
        {
            if (!IsDisposed)
                Cursor = Cursors.Default;
        }
    }

    private void ApplyFilter()
    {
        var filter = searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? records
            : records
                .Where(r =>
                    Contains(r.Name, filter)
                    || Contains(r.Customer, filter)
                    || Contains(r.Material, filter)
                    || Contains(r.MadeBy, filter)
                    || Contains(r.Comments, filter)
                    || Contains(r.Status.ToString(), filter)
                    || Contains(FormatStatus(r.Status), filter)
                    || Contains(r.DateCreated.ToString("g"), filter)
                    || Contains(r.DateModified.ToString("g"), filter)
                    || Contains(r.SavedAt.ToString("g"), filter)
                    || Contains(r.Thickness.ToString(), filter)
                    || Contains(r.PlateCount.ToString(), filter)
                    || Contains(r.PartCount.ToString(), filter)
                    || Contains(r.FileSize.ToString(), filter)
                )
                .ToArray();

        Populate(filtered);
    }

    private static bool Contains(string value, string filter) =>
        !string.IsNullOrEmpty(value) && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void Populate(NestRecord[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Customer", typeof(string));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("Material", typeof(string));
        table.Columns.Add("Date Created", typeof(DateTime));
        table.Columns.Add("Date Modified", typeof(DateTime));
        table.Columns.Add("Thickness", typeof(double));
        table.Columns.Add("Plates", typeof(int));
        table.Columns.Add("Parts", typeof(int));
        table.Columns.Add("Made By", typeof(string));
        table.Columns.Add("Comments", typeof(string));
        table.Columns.Add("File Size", typeof(long));
        table.Columns.Add("Saved", typeof(DateTime));
        table.Columns.Add("Id", typeof(Guid));

        foreach (var record in rows.OrderByDescending(r => r.SavedAt))
        {
            table.Rows.Add(
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

        grid.DataSource = table;
        if (grid.Columns.Contains("Id"))
            grid.Columns["Id"].Visible = false;
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
        SelectedId = (Guid)grid.SelectedRows[0].Cells["Id"].Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void Delete_Click(object sender, EventArgs e)
    {
        if (grid.SelectedRows.Count == 0)
            return;

        var id = (Guid)grid.SelectedRows[0].Cells["Id"].Value;
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
                await LoadAsync();
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
}
