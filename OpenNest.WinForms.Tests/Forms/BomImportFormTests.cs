using System.Data;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.IO.Bom;

namespace OpenNest.WinForms.Tests.Forms;

public class BomImportFormTests
{
    [Fact]
    public void PartColumns_KeepBomOrderAndOnlyMaterialThicknessAndQtyAreEditable()
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);

            Assert.Equal(
                new[] { "Item #", "File Name", "Description", "Material", "Thickness", "Qty", "Status" },
                parts.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).Select(c => c.HeaderText)
            );
            Assert.All(
                parts.Columns.Cast<DataGridViewColumn>(),
                c => Assert.Equal(DataGridViewColumnSortMode.NotSortable, c.SortMode)
            );
            Assert.Equal(
                new[] { "colMaterial", "colThickness", "colQty" },
                parts.Columns.Cast<DataGridViewColumn>().Where(c => !c.ReadOnly).Select(c => c.Name)
            );
        });
    }

    [Fact]
    public void EditingQty_ChangesThatPartAndTheGroupTotal()
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var first = Ready("PT01", qty: 2);
            var second = Ready("PT02", qty: 3);
            form.LoadRows(new[] { first, second });
            Assert.Equal(5, GroupTotal(form, 0));

            Assert.True(Edit(parts, 0, "colQty", "5"));

            Assert.Equal(5, first.Qty);
            Assert.Equal(3, second.Qty);
            Assert.Equal(8, GroupTotal(form, 0));
            Assert.Equal("Ready", parts.Rows[0].Cells["colStatus"].Value);
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("abc")]
    [InlineData("")]
    public void InvalidQty_IsRefusedAndTheOldValueKept(string text)
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var part = Ready("PT01", qty: 2);
            form.LoadRows(new[] { part });

            Assert.False(Edit(parts, 0, "colQty", text));
            Assert.True(parts.IsCurrentCellInEditMode);
            Assert.False(string.IsNullOrEmpty(parts.CurrentCell.ErrorText));

            parts.CancelEdit();
            Assert.True(parts.EndEdit());

            Assert.Equal(2, part.Qty);
            Assert.Equal(2, GroupTotal(form, 0));
        });
    }

    [Fact]
    public void Edits_ChangeThePartShownInThatRow_AfterTheRowsAreReloadedInAnotherOrder()
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var a = Ready("PT01", qty: 1);
            var b = Ready("PT02", qty: 1);
            var c = Ready("PT03", qty: 1);
            form.LoadRows(new[] { a, b, c });

            Assert.True(Edit(parts, 2, "colMaterial", "Aluminum"));
            Assert.Equal("Aluminum", c.Material);
            Assert.Equal("Stainless", a.Material);

            form.LoadRows(new[] { c, a, b });
            Assert.Same(c, parts.Rows[0].DataBoundItem);

            Assert.True(Edit(parts, 0, "colQty", "9"));
            Assert.Equal(9, c.Qty);
            Assert.Equal(1, a.Qty);
        });
    }

    [Fact]
    public void RowsWithoutADrawing_CannotEnterEditMode()
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var noDrawing = Ready("PT01", qty: 1);
            noDrawing.DxfPath = null;
            form.LoadRows(new[] { noDrawing, Ready("PT02", qty: 1) });

            parts.CurrentCell = parts.Rows[0].Cells["colQty"];
            Assert.False(parts.BeginEdit(selectAll: true));
            Assert.False(parts.IsCurrentCellInEditMode);

            parts.CurrentCell = parts.Rows[1].Cells["colQty"];
            Assert.True(parts.BeginEdit(selectAll: true));
            parts.CancelEdit();
        });
    }

    [Fact]
    public void BlankingMaterialOrThickness_MarksTheRowAndDropsItFromTheGroups()
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var first = Ready("PT01", qty: 1);
            var second = Ready("PT02", qty: 1);
            form.LoadRows(new[] { first, second });

            Assert.True(Edit(parts, 0, "colMaterial", " "));
            Assert.Null(first.Material);
            Assert.Equal("Needs material", parts.Rows[0].Cells["colStatus"].Value);

            Assert.False(Edit(parts, 1, "colThickness", "abc"));
            parts.CancelEdit();
            Assert.True(parts.EndEdit());
            Assert.Equal(0.25, second.Thickness);

            Assert.True(Edit(parts, 1, "colThickness", ""));
            Assert.Null(second.Thickness);
            Assert.Equal("Needs thickness", parts.Rows[1].Cells["colStatus"].Value);

            Assert.Empty(GroupsTable(form).Rows);
            Assert.Equal("0 ready, 1 needs a material, 1 needs a thickness", Field<Label>(form, "lblSummary").Text);
        });
    }

    private static BomImportForm Show(out DataGridView parts)
    {
        var form = new BomImportForm();
        form.Show();
        parts = Field<DataGridView>(form, "dgvParts");
        return form;
    }

    private static bool Edit(DataGridView grid, int row, string column, string text)
    {
        grid.CurrentCell = grid.Rows[row].Cells[column];
        Assert.True(grid.BeginEdit(selectAll: true));
        grid.EditingControl!.Text = text;
        return grid.EndEdit();
    }

    private static DataTable GroupsTable(BomImportForm form) =>
        Assert.IsType<DataTable>(Field<DataGridView>(form, "dgvGroups").DataSource);

    private static int GroupTotal(BomImportForm form, int group) =>
        (int)GroupsTable(form).Rows[group]["Total Qty"];

    private static BomPartRow Ready(string fileName, int qty) =>
        new()
        {
            FileName = fileName,
            DxfPath = $@"C:\drawings\{fileName}.dxf",
            Material = "Stainless",
            Thickness = 0.25,
            Qty = qty,
        };

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "The BOM import dialog test did not complete.");
}
