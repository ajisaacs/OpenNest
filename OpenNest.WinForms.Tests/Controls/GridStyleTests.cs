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

    private static void Pump(Control control)
    {
        for (var i = 0; i < 5; i++)
        {
            control.Refresh();
            Application.DoEvents();
        }
    }
}
