using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls;

/// <summary>
/// The shared look of every <see cref="DataGridView"/> in the desktop app, taken from the
/// database nest browser: flat light headers, faint grid lines, a soft blue selection and
/// fixed row heights. Columns size once to their first content and are then the user's to
/// resize. Other behaviour (editing, selection mode) stays with each grid.
/// </summary>
public static class GridStyle
{
    public const int RowHeight = 22;
    private const int MaxInitialColumnWidth = 400;

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
    /// Widens each column to fit its header and the rows first shown, once, then leaves the
    /// widths to the user. Columns never stretch to fill the grid.
    /// </summary>
    private static void SizeColumnsOnceToContent(DataGridView grid)
    {
        // The first paint with rows is the first time they are on screen to be measured. The
        // empty new-row placeholder does not count: a grid that starts empty keeps its widths
        // until it has content.
        void OnPaint(object sender, PaintEventArgs e)
        {
            var newRowShown = grid.NewRowIndex >= 0 && grid.Rows[grid.NewRowIndex].Displayed;
            if (grid.DisplayedRowCount(false) - (newRowShown ? 1 : 0) <= 0)
                return;
            grid.Paint -= OnPaint;
            grid.BeginInvoke(() =>
            {
                if (grid.IsDisposed)
                    return;
                grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                foreach (DataGridViewColumn column in grid.Columns)
                    column.Width = System.Math.Min(column.Width, MaxInitialColumnWidth);
            });
        }

        grid.Paint += OnPaint;
    }
}
