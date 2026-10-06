using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.IO.Bom;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class BomImportFormTests
{
    [Fact]
    public void FileNameWithDxfExtension_RealRowBuilderShowsReady()
    {
        RunSta(() =>
        {
            using var files = new WindowsAcceptanceFiles();
            var path = files.WriteSquare("acceptance-square");
            var row = Assert.Single(BomImportRows.Build(
                new List<BomItem> { Item("acceptance-square.dxf", qty: 2) }, files.Folder));
            using var form = Show(out var parts);

            form.LoadRows(new[] { row });

            Assert.True(form.Visible);
            Assert.Equal(path, row.DxfPath);
            Assert.Same(row, parts.Rows[0].DataBoundItem);
            Assert.Equal("acceptance-square.dxf", parts.Rows[0].Cells["colFileName"].Value);
            Assert.Equal("Ready", parts.Rows[0].Cells["colStatus"].Value);
            Assert.Equal(BomRowStatus.Ready, row.Status);
            Assert.Single(GroupsTable(form).Rows);
            Assert.Equal(2, GroupTotal(form, 0));
            Assert.True(Field<Button>(form, "btnCreateNests").Enabled);
        });
    }

    [Fact]
    public void BlankThickness_RealRowBuilderResolvesDrawingAndGridEditMakesReadyGroup()
    {
        RunSta(() =>
        {
            using var files = new WindowsAcceptanceFiles();
            var path = files.WriteSquare("acceptance-square");
            var item = Item("acceptance-square", qty: 3);
            item.Thickness = null;
            var row = Assert.Single(BomImportRows.Build(new List<BomItem> { item }, files.Folder));
            using var form = Show(out var parts);
            form.LoadRows(new[] { row });

            Assert.True(form.Visible);
            Assert.Equal(path, row.DxfPath);
            Assert.Null(row.Thickness);
            Assert.True(row.IsEditable);
            Assert.Equal("Needs thickness", parts.Rows[0].Cells["colStatus"].Value);
            Assert.Empty(GroupsTable(form).Rows);
            Assert.False(Field<Button>(form, "btnCreateNests").Enabled);

            Assert.True(Edit(parts, 0, "colThickness", 0.25.ToString()));

            Assert.Equal(0.25, row.Thickness);
            Assert.Equal(BomRowStatus.Ready, row.Status);
            Assert.Equal("Ready", parts.Rows[0].Cells["colStatus"].Value);
            var group = Assert.Single(GroupsTable(form).Rows.Cast<DataRow>());
            Assert.Equal("Stainless", group["Material"]);
            Assert.Equal(0.25, group["Thickness"]);
            Assert.Equal(1, group["Parts"]);
            Assert.Equal(3, group["Total Qty"]);
            Assert.Equal("1 ready", Field<Label>(form, "lblSummary").Text);
            Assert.True(Field<Button>(form, "btnCreateNests").Enabled);
        });
    }

    [Fact]
    public void DuplicateRows_CreateNestsButtonImportsOneDrawingWithCombinedQuantity()
    {
        RunSta(() =>
        {
            using var files = new WindowsAcceptanceFiles();
            var path = files.WriteSquare("acceptance-square");
            var rows = BomImportRows.Build(
                new List<BomItem>
                {
                    Item("acceptance-square", qty: 2),
                    Item("acceptance-square.dxf", qty: 3),
                }, files.Folder);
            using var host = new Form { IsMdiContainer = true };
            host.Show();
            using var form = Show(out var parts);
            form.MdiParentForm = host;
            Field<TextBox>(form, "txtJobName").Text = "Acceptance job";
            Field<TextBox>(form, "txtPlateWidth").Text = "10";
            Field<TextBox>(form, "txtPlateLength").Text = "20";
            form.LoadRows(rows);

            Assert.Equal(2, parts.Rows.Count);
            Assert.All(rows, row => Assert.Equal(path, row.DxfPath));
            Assert.Equal(5, GroupTotal(form, 0));
            var create = Field<Button>(form, "btnCreateNests");
            Assert.True(create.Enabled);

            // The production event ends in a native MessageBox. A timer in
            // its modal loop clicks that thread's real OK button, retaining
            // the actual result text; no builder or success result is stubbed.
            var dialogTitle = "";
            var dialogText = "";
            var okPosted = false;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            using var dismiss = new System.Windows.Forms.Timer { Interval = 20 };
            dismiss.Tick += (_, _) =>
            {
                EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
                {
                    var className = new StringBuilder(256);
                    GetClassName(window, className, className.Capacity);
                    if (className.ToString() != "#32770")
                        return true;

                    // An OK-only native MessageBox can assign its button the
                    // IDCANCEL id (2), not IDOK (1). Find its sole real button
                    // instead of assuming an id, and keep polling until ready.
                    var ok = FindWindowEx(window, IntPtr.Zero, "Button", null);
                    if (ok == IntPtr.Zero || FindWindowEx(window, ok, "Button", null) != IntPtr.Zero)
                        return true;

                    dialogTitle = WindowText(window);
                    dialogText = "";
                    var text = FindWindowEx(window, IntPtr.Zero, "Static", null);
                    while (text != IntPtr.Zero)
                    {
                        var value = WindowText(text);
                        if (!string.IsNullOrEmpty(value))
                            dialogText += value;
                        text = FindWindowEx(window, text, "Static", null);
                    }
                    okPosted = PostMessage(ok, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                    if (okPosted)
                        dismiss.Stop();
                    return !okPosted;
                }, IntPtr.Zero);
                if (DateTime.UtcNow >= deadline && !okPosted)
                    throw new TimeoutException("The Create Nests result dialog could not be acknowledged.");
            };

            try
            {
                dismiss.Start();
                create.PerformClick();
                dismiss.Stop();

                Assert.True(okPosted, "Create Nests did not show its result dialog.");
                Assert.Equal("Import Complete", dialogTitle);
                Assert.Equal("1 nest created.", dialogText);
                Assert.False(form.Visible);
                var editor = Assert.IsType<EditNestForm>(Assert.Single(host.MdiChildren));
                Assert.True(editor.Visible);
                var nest = editor.Nest;
                var drawing = Assert.Single(nest.Drawings);
                Assert.Equal("acceptance-square", drawing.Name);
                Assert.Equal(5, drawing.Quantity.Required);
                Assert.NotEmpty(drawing.Program.Codes);
                Assert.Equal(4, drawing.Area, precision: 6);
                Assert.Equal($"Acceptance job - {0.25:0.####} Stainless", nest.Name);
                Assert.Equal("Stainless", nest.Material.Name);
                Assert.Equal(0.25, nest.Thickness);
            }
            finally
            {
                dismiss.Stop();
                foreach (var child in host.MdiChildren)
                    child.Dispose();
            }
        });
    }

    private static BomItem Item(string fileName, int qty) => new()
    {
        FileName = fileName,
        Material = "Stainless",
        Thickness = 0.25,
        Qty = qty,
    };

    private static string WindowText(IntPtr window)
    {
        var text = new StringBuilder(1024);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumThreadWindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

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

    [Theory]
    [InlineData("colQty", "0")]
    [InlineData("colQty", "abc")]
    [InlineData("colThickness", "-1")]
    [InlineData("colThickness", "abc")]
    public void ProgrammaticCommitOfInvalidText_KeepsTheRowsValue(string column, string text)
    {
        RunSta(() =>
        {
            using var form = Show(out var parts);
            var part = Ready("PT01", qty: 2);
            form.LoadRows(new[] { part });

            parts.CurrentCell = parts.Rows[0].Cells[column];
            Assert.True(parts.BeginEdit(selectAll: true));
            parts.EditingControl!.Text = text;
            Assert.True(parts.EndEdit());

            Assert.Equal(2, part.Qty);
            Assert.Equal(0.25, part.Thickness);
            Assert.Equal(BomRowStatus.Ready, part.Status);
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
            Assert.Equal("", first.Material);
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

    /// <summary>
    /// Types <paramref name="text"/> into the cell and presses Enter, which
    /// validates the cell the way the operator's commit does. Returns true
    /// when the edit was accepted (the cell left edit mode).
    /// </summary>
    private static bool Edit(DataGridView grid, int row, string column, string text)
    {
        grid.CurrentCell = grid.Rows[row].Cells[column];
        Assert.True(grid.BeginEdit(selectAll: true));
        grid.EditingControl!.Text = text;
        var processEnterKey = typeof(DataGridView).GetMethod(
            "ProcessEnterKey",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        processEnterKey.Invoke(grid, new object[] { Keys.Enter });
        return !grid.IsCurrentCellInEditMode;
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
