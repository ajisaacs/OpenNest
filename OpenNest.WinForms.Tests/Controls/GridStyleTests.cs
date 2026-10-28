using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class GridStyleTests
{
    [Fact]
    public void ColumnsSizeToFirstContentThenKeepTheUsersWidths() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new Form { Width = 1200, Height = 400, ShowInTaskbar = false };
        var grid = GridStyle.Apply(new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false });
        var shortColumn = new DataGridViewTextBoxColumn { HeaderText = "Qty", Width = 300 };
        var longColumn = new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 40 };
        var hugeColumn = new DataGridViewTextBoxColumn { HeaderText = "Notes", Width = 40 };
        grid.Columns.AddRange(shortColumn, longColumn, hugeColumn);
        form.Controls.Add(grid);
        form.Show();
        Pump(grid);
        Assert.Equal(300, shortColumn.Width); // no rows yet: nothing is measured

        grid.Rows.Add("1", "A fairly long drawing name", new string('W', 200));
        Pump(grid);

        Assert.True(shortColumn.Width < 300);
        Assert.True(longColumn.Width > 40);
        Assert.Equal(400, hugeColumn.Width);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>(), column =>
        {
            Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.InheritedAutoSizeMode);
            Assert.True(column.Resizable == DataGridViewTriState.True || column.Resizable == DataGridViewTriState.NotSet);
        });

        longColumn.Width = 77;
        grid.Rows.Add("2", new string('X', 80), "");
        Pump(grid);
        Assert.Equal(77, longColumn.Width);
    });

    [Fact]
    public void RowsFilledBeforeTheFormOpensArePaintedAtTheirFinalWidths() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new Form { Width = 1200, Height = 400, ShowInTaskbar = false };
        var grid = GridStyle.Apply(new DataGridView { Dock = DockStyle.Fill });
        var column = new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 40 };
        grid.Columns.Add(column);
        for (var i = 0; i < 20; i++)
            grid.Rows.Add(i == 10 ? "A fairly long drawing name" : "A");
        form.Controls.Add(grid);
        var firstPaintWidth = WidthAtFirstPaint(grid, column);

        form.Show();
        Application.DoEvents();
        Pump(grid);

        Assert.True(column.Width > 40);
        Assert.Equal(column.Width, firstPaintWidth());
    });

    [Fact]
    public void RowsAddedAfterTheFormOpensArePaintedAtTheirFinalWidths() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new Form { Width = 1200, Height = 400, ShowInTaskbar = false };
        var grid = GridStyle.Apply(new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false });
        var column = new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 40 };
        grid.Columns.Add(column);
        form.Controls.Add(grid);
        form.Show();
        Pump(grid);

        // One row at a time, as most grids fill: the long name is not the first row.
        grid.Rows.Add("A");
        grid.Rows.Add("A fairly long drawing name");
        var firstPaintWidth = WidthAtFirstPaint(grid, column);
        Application.DoEvents();
        Pump(grid);

        Assert.True(column.Width > 40);
        Assert.Equal(column.Width, firstPaintWidth());
    });

    [Fact]
    public void DisposingAGridStopsItsShowingKeyboardToolTip() => ArchUnitsTests.RunSta(() =>
    {
        // WinForms leaves the keyboard tooltip's auto-pop timer running after the grid is
        // disposed; when it fires it hides the tooltip through the disposed grid and throws.
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new Form { Width = 600, Height = 300, ShowInTaskbar = false };
        var grid = GridStyle.Apply(new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false });
        grid.Columns.Add("Version", "Version");
        grid.Rows.Add("2");
        form.Controls.Add(grid);
        form.Show();
        Pump(grid);

        // As when the keyboard moves to a cell in the embedded version history.
        var toolTip = (ToolTip)typeof(DataGridView).GetProperty("KeyboardToolTip", members).GetValue(grid);
        typeof(ToolTip).GetMethod("ShowKeyboardToolTip", members)
            .Invoke(toolTip, new object[] { "2", grid.Rows[0].Cells[0], 3000 });
        var timer = typeof(ToolTip).GetField("_timer", members);
        Assert.NotNull(timer.GetValue(toolTip));

        form.Controls.Remove(grid);
        grid.Dispose();

        Assert.Null(timer.GetValue(toolTip));
    });

    private static Func<int> WidthAtFirstPaint(DataGridView grid, DataGridViewColumn column)
    {
        var width = -1;
        void OnPaint(object sender, PaintEventArgs e)
        {
            grid.Paint -= OnPaint;
            width = column.Width;
        }

        grid.Paint += OnPaint;
        return () => width;
    }

    private static void Pump(Control control)
    {
        for (var i = 0; i < 5; i++)
        {
            control.Refresh();
            Application.DoEvents();
        }
    }
}
