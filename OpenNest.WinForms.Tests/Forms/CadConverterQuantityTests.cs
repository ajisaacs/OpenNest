using System.Drawing;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// A nest drawing may require no parts (quantity 0); editing it in the CAD Converter must keep that.
/// </summary>
public class CadConverterQuantityTests
{
    [Fact]
    public void LoadingADrawingThatRequiresNoParts_KeepsItsQuantityAtZero() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new CadConverterForm();
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Size = new System.Drawing.Size(1200, 800);
        form.Show();

        form.LoadDrawings([Square(0)]);

        var drawing = Assert.Single(form.GetDrawings());
        Assert.Equal(0, drawing.Quantity.Required);
        form.Close();
    });

    private static Drawing Square(int quantity)
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 10), new Line(10, 10, 0, 10), new Line(0, 10, 0, 0),
        };
        foreach (var e in entities)
            e.Layer = new Layer("0") { IsVisible = true };
        var drawing = CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "square" }, entities,
            new List<Bend>(), 1, null, null);
        drawing.Quantity.Required = quantity;
        return drawing;
    }
}
