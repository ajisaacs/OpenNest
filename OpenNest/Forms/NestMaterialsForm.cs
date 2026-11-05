using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Forms;

/// <summary>
/// Open Nest (Database mode) &gt; Get Materials: downloads the chosen nests and lists the
/// sheets they consume by material, grade, thickness and size, with the quantity and the
/// nests that use each. Built by <see cref="NestMaterialsReport"/>; nothing is changed on
/// the server.
/// </summary>
public sealed class NestMaterialsForm : Form
{
    // Archives downloaded and read at once; keeps the server and this machine responsive.
    private const int Parallelism = 4;

    private readonly INestRepository repository;
    private readonly Func<CancellationToken, Task<IReadOnlyList<NestRecord>>> loadRecords;
    private readonly CancellationTokenSource cancellation = new();
    private readonly DataGridView grid;
    private readonly Label statusLabel;
    private readonly ProgressBar progress;
    private readonly Button copyButton;
    private readonly Button saveButton;
    private readonly List<string> failures = new();
    private readonly ToolTip failureTip = new();
    private NestMaterialsReport report;

    public NestMaterialsForm(
        INestRepository repository,
        Func<CancellationToken, Task<IReadOnlyList<NestRecord>>> loadRecords,
        string description)
    {
        this.repository = repository;
        this.loadRecords = loadRecords;

        Text = $"Materials — {description}";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(1100, 600);
        MinimumSize = new Size(600, 300);
        MinimizeBox = false;
        ShowInTaskbar = false;

        grid = GridStyle.Apply(new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        }, sizeColumns: false);
        AddColumn("Material", 160, typeof(string));
        AddColumn("Grade", 90, typeof(string));
        AddColumn("Thickness", 80, typeof(double), "0.####");
        AddColumn("Units", 50, typeof(string));
        AddColumn("Size", 130, typeof(string));
        AddColumn("Quantity", 70, typeof(int), "N0");
        var nests = AddColumn("Nests", 500, typeof(string));
        nests.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        nests.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        statusLabel = new Label { AutoSize = true, Margin = new Padding(3, 8, 12, 0) };
        progress = new ProgressBar { Width = 200, Margin = new Padding(3, 6, 3, 3) };
        copyButton = new Button { Text = "Copy All", AutoSize = true, Enabled = false };
        copyButton.Click += (_, _) => CopyAll();
        saveButton = new Button { Text = "Save CSV...", AutoSize = true, Enabled = false };
        saveButton.Click += (_, _) => SaveCsv();
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = closeButton;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(3) };
        bar.Controls.Add(copyButton);
        bar.Controls.Add(saveButton);
        bar.Controls.Add(closeButton);
        bar.Controls.Add(progress);
        bar.Controls.Add(statusLabel);

        Controls.Add(grid);
        Controls.Add(bar);

        Load += (_, _) => DialogBounds.FitToOwnerScreen(this);
        Shown += async (_, _) => await RunAsync();
        FormClosed += (_, _) => cancellation.Cancel();
    }

    private DataGridViewTextBoxColumn AddColumn(string header, int width, Type valueType, string format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = header,
            HeaderText = header,
            Width = width,
            ValueType = valueType,
        };
        if (valueType != typeof(string))
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (format != null)
            column.DefaultCellStyle.Format = format;
        grid.Columns.Add(column);
        return column;
    }

    private async Task RunAsync()
    {
        var token = cancellation.Token;
        try
        {
            statusLabel.Text = "Finding nests...";
            progress.Style = ProgressBarStyle.Marquee;
            var records = await loadRecords(token);
            if (IsDisposed)
                return;

            progress.Style = ProgressBarStyle.Continuous;
            progress.Maximum = System.Math.Max(1, records.Count);
            progress.Value = 0;
            var built = new NestMaterialsReport();
            var gate = new object();
            var done = 0;
            void Report() => statusLabel.Text = $"Reading nests: {done:N0} of {records.Count:N0}...";
            Report();

            using var throttle = new SemaphoreSlim(Parallelism);
            var tasks = records.Select(async record =>
            {
                await throttle.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var file = await repository.GetFileAsync(record.Id, token);
                    if (file == null)
                        throw new InvalidOperationException("no longer on the server");
                    await Task.Run(() =>
                    {
                        using var stream = new MemoryStream(file);
                        var nest = new NestReader(stream).Read();
                        lock (gate)
                            built.Add(record, nest);
                    }, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (gate)
                        failures.Add($"{record.Name}: {ex.Message}");
                }
                finally
                {
                    throttle.Release();
                    if (!IsDisposed)
                    {
                        // Continuations of the UI-thread loop resume on the UI thread.
                        done++;
                        progress.Value = System.Math.Min(progress.Maximum, done);
                        Report();
                    }
                }
            }).ToList();
            await Task.WhenAll(tasks);
            if (IsDisposed)
                return;

            report = built;
            Populate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                progress.Visible = false;
                statusLabel.Text = $"Could not get materials: {ex.Message}";
            }
        }
    }

    private void Populate()
    {
        grid.Rows.Clear();
        var rows = report.Rows;
        foreach (var row in rows)
        {
            grid.Rows.Add(
                row.Material,
                row.Grade,
                row.Thickness,
                NestMaterialsReport.UnitName(row.Units),
                $"{row.Width.ToString("0.###", CultureInfo.CurrentCulture)} x {row.Length.ToString("0.###", CultureInfo.CurrentCulture)}",
                row.Quantity,
                row.NestList);
        }

        progress.Visible = false;
        var sheets = rows.Sum(row => row.Quantity);
        var text = $"{report.NestNames.Count:N0} nest(s), {rows.Count:N0} material/size row(s), {sheets:N0} sheet(s)";
        if (failures.Count > 0)
        {
            text += $" — {failures.Count:N0} nest(s) could not be read";
            failureTip.SetToolTip(statusLabel, string.Join(Environment.NewLine, failures.Take(30)));
            statusLabel.ForeColor = Color.Firebrick;
        }

        statusLabel.Text = text;
        copyButton.Enabled = saveButton.Enabled = rows.Count > 0;
    }

    private void CopyAll()
    {
        // Tab-separated so it pastes into a spreadsheet as columns.
        var builder = new StringBuilder();
        builder.AppendLine("Material\tGrade\tThickness\tUnits\tWidth\tLength\tQuantity\tNests");
        foreach (var row in report.Rows)
        {
            builder.Append(row.Material).Append('\t')
                .Append(row.Grade).Append('\t')
                .Append(row.Thickness.ToString("0.####", CultureInfo.CurrentCulture)).Append('\t')
                .Append(NestMaterialsReport.UnitName(row.Units)).Append('\t')
                .Append(row.Width.ToString("0.###", CultureInfo.CurrentCulture)).Append('\t')
                .Append(row.Length.ToString("0.###", CultureInfo.CurrentCulture)).Append('\t')
                .Append(row.Quantity.ToString(CultureInfo.CurrentCulture)).Append('\t')
                .AppendLine(row.NestList);
        }

        Clipboard.SetText(builder.ToString());
        statusLabel.Text = $"Copied {report.Rows.Count:N0} row(s).";
    }

    private void SaveCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "Materials.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, report.ToCsv(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save: {ex.Message}", "Save CSV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            failureTip.Dispose();
        // The cancellation source is left undisposed: downloads still finishing after the
        // window closes observe its token.
        base.Dispose(disposing);
    }
}
