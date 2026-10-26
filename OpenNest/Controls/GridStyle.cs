using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls;

/// <summary>
/// The shared look of every <see cref="DataGridView"/> in the desktop app, taken from the
/// database nest browser: flat light headers, faint grid lines, a soft blue selection and
/// fixed row heights. Columns size once to their first content, before it is first painted,
/// and are then the user's to resize. Other behaviour (editing, selection mode) stays with each grid.
/// </summary>
public static class GridStyle
{
    public const int RowHeight = 22;
    private const int MaxInitialColumnWidth = 400;
    private const int MeasuredRowCount = 50;

    private static readonly Color HeaderBackColor = Color.FromArgb(245, 245, 245);
    private static readonly Color HeaderForeColor = Color.FromArgb(90, 90, 90);
    private static readonly Color SelectionBackColor = Color.FromArgb(204, 228, 247);
    private static readonly Color LineColor = Color.FromArgb(230, 230, 230);

    /// <param name="sizeColumns">False leaves column widths to the grid, as for columns that fill it.</param>
    public static T Apply<T>(T grid, bool sizeColumns = true) where T : DataGridView
    {
        grid.AllowUserToResizeRows = false;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = LineColor;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        grid.RowTemplate.Height = RowHeight;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;

        grid.DefaultCellStyle.SelectionBackColor = SelectionBackColor;
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderForeColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBackColor;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.RowHeadersDefaultCellStyle.BackColor = HeaderBackColor;
        grid.RowHeadersDefaultCellStyle.ForeColor = HeaderForeColor;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = SelectionBackColor;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = Color.Black;
        grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (sizeColumns)
            SizeColumnsOnceToContent(grid);
        return grid;
    }

    /// <summary>
    /// Widens each column to fit its header and its first rows, once, then leaves the widths
    /// to the user. Columns never stretch to fill the grid.
    /// </summary>
    private static void SizeColumnsOnceToContent(DataGridView grid)
    {
        // Sizing measures the rows rather than waiting for them on screen, so the grid is first
        // painted at its final widths. Rows already there when the handle is created are sized
        // at once; rows added later are sized in a posted call, which runs after the adding code
        // returns (so a loop of Rows.Add is measured whole) and before the grid repaints. The
        // empty new-row placeholder does not count: a grid that starts empty keeps its widths
        // until it has content.
        var pending = false;

        void Detach()
        {
            grid.HandleCreated -= OnHandleCreated;
            grid.RowsAdded -= OnRowsAdded;
            grid.DataBindingComplete -= OnRowsAdded;
        }

        void SizeIfFilled()
        {
            if (grid.IsDisposed || ContentRowCount(grid) == 0)
                return;
            Detach();
            SizeColumnsToRows(grid);
        }

        void OnHandleCreated(object sender, EventArgs e) => SizeIfFilled();

        void OnRowsAdded(object sender, EventArgs e)
        {
            if (pending || !grid.IsHandleCreated || ContentRowCount(grid) == 0)
                return;
            pending = true;
            grid.BeginInvoke(() =>
            {
                pending = false;
                SizeIfFilled();
            });
        }

        grid.HandleCreated += OnHandleCreated;
        grid.RowsAdded += OnRowsAdded;
        grid.DataBindingComplete += OnRowsAdded;
        if (grid.IsHandleCreated)
            SizeIfFilled();
    }

    private static int ContentRowCount(DataGridView grid) =>
        grid.Rows.Count - (grid.NewRowIndex >= 0 ? 1 : 0);

    private static void SizeColumnsToRows(DataGridView grid)
    {
        var rowCount = System.Math.Min(ContentRowCount(grid), MeasuredRowCount);
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (!column.Visible || column.InheritedAutoSizeMode != DataGridViewAutoSizeColumnMode.None)
                continue;
            var width = column.HeaderCell.PreferredSize.Width;
            for (var row = 0; row < rowCount; row++)
            {
                if (row != grid.NewRowIndex)
                    width = System.Math.Max(width, grid.Rows[row].Cells[column.Index].PreferredSize.Width);
            }
            column.Width = System.Math.Clamp(width, column.MinimumWidth, MaxInitialColumnWidth);
        }
    }
}
