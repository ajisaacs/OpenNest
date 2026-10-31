using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// The CAD Converter's Hole Table on a 10 x 6 plate with two 0.5 holes and one 0.25 hole.
/// </summary>
public class CadConverterHoleTableTests
{
    [Fact]
    public void Holes_ListsEachSizeWithItsQuantity_AndColorsItsHolesOnTheView() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new CadConverterForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        var circles = item.Entities.OfType<Circle>().ToList();
        var large = circles.Where(c => c.Radius > 0.2).Select(c => c.Id).ToList();
        var small = circles.Single(c => c.Radius < 0.2).Id;

        Assert.Null(view.HoleColors);
        Field<Button>(form, "btnHoleTable").PerformClick();

        var table = Assert.Single(form.OwnedForms.OfType<HoleTableForm>());
        Assert.True(table.Visible);
        var grid = table.Controls.OfType<DataGridView>().Single();
        Assert.Equal(
            new[] { ("\u00D80.2500", "1"), ("\u00D80.5000", "2") },
            grid.Rows.Cast<DataGridViewRow>().Select(r => (r.Cells[1].Value!.ToString()!, r.Cells[2].Value!.ToString()!)));

        // Each size's holes take its row's swatch color; the outline keeps its own.
        Assert.Equal(3, view.HoleColors!.Count);
        Assert.Equal(grid.Rows[0].Cells[0].Style.BackColor, view.HoleColors[small]);
        Assert.All(large, id => Assert.Equal(grid.Rows[1].Cells[0].Style.BackColor, view.HoleColors[id]));
        Assert.NotEqual(view.HoleColors[small], view.HoleColors[large[0]]);

        // A hole set to Display is no longer cut, so it leaves the table.
        Invoke(form, "OnEntityActionRequested", null,
            new EntityActionEventArgs(EntityAction.Display, [circles.First(c => c.Id == large[0])]));
        Assert.Equal("1", grid.Rows[1].Cells[2].Value!.ToString());
        Assert.False(view.HoleColors.ContainsKey(large[0]));

        // Closing the table restores the view's normal colors.
        table.Close();
        Application.DoEvents();
        Assert.Null(view.HoleColors);
        form.Close();
    });

    private static Drawing Part()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
            new Circle(2, 3, 0.25), new Circle(5, 3, 0.25), new Circle(8, 3, 0.125),
        };
        var layer = new Layer("0") { IsVisible = true };
        foreach (var e in entities)
            e.Layer = layer;
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "holes" }, entities,
            new List<Bend>(), 1, null, null);
    }

    private static void Show(Form form)
    {
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Size = new System.Drawing.Size(1200, 800);
        form.Show();
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(CadConverterForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(CadConverterForm form, string name, params object?[] args) =>
        typeof(CadConverterForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
}
