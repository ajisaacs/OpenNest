using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls;

/// <summary>
/// The shared look of every <see cref="DataGridView"/> in the desktop app, taken from the
/// database nest browser: flat light headers, faint grid lines, a soft blue selection and
/// fixed row heights. Behaviour (editing, selection mode, column sizing) stays with each grid.
/// </summary>
public static class GridStyle
{
    public const int RowHeight = 22;

    private static readonly Color HeaderBackColor = Color.FromArgb(245, 245, 245);
    private static readonly Color HeaderForeColor = Color.FromArgb(90, 90, 90);
    private static readonly Color SelectionBackColor = Color.FromArgb(204, 228, 247);
    private static readonly Color LineColor = Color.FromArgb(230, 230, 230);

    public static T Apply<T>(T grid) where T : DataGridView
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
        return grid;
    }
}
