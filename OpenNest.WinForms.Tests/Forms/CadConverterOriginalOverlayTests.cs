using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;
using static OpenNest.WinForms.Tests.FormTesting;
using CadArc = ACadSharp.Entities.Arc;
using CadLayer = ACadSharp.Tables.Layer;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// The Original overlay shows the source file, while Simplify keeps working from the
/// cleaned geometry.
/// </summary>
public class CadConverterOriginalOverlayTests
{
    [Fact]
    public void Overlay_ShowsTheFileFromLoad_AndSimplifyStartsFromTheCleanedGeometry() => ArchUnitsTests.RunSta(() =>
    {
        var directory = Directory.CreateTempSubdirectory("opennest-overlay-").FullName;
        try
        {
            using var form = new CadConverterForm();
            Show(form);
            form.AddFile(WriteHole(directory));
            var view = Field<EntityView>(form, "entityView1");
            var item = Field<FileListControl>(form, "fileList").SelectedItem!;

            Assert.Null(view.OriginalEntities);
            Field<CheckBox>(form, "chkShowOriginal").Checked = true;
            Assert.Same(item.RawEntities, view.OriginalEntities);
            Assert.Equal(2, view.OriginalEntities!.OfType<Arc>().Count());
            Assert.Empty(view.OriginalEntities.OfType<Circle>());
            Assert.Single(item.Entities.OfType<Circle>());
            Assert.Empty(item.Entities.OfType<Arc>());

            // Simplify must not bring the joined hole's arcs back.
            Assert.Null(item.OriginalEntities);
            Invoke(form, "OnSimplifyClick", null, EventArgs.Empty);
            Assert.Single(item.OriginalEntities!.OfType<Circle>());
            Assert.Empty(item.OriginalEntities!.OfType<Arc>());
            Assert.Same(item.RawEntities, view.OriginalEntities);
            form.Close();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    });

    [Fact]
    public void Overlay_FallsBackToThePreSimplifyEntities_WhenNoFileWasRead() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new CadConverterForm();
        Show(form);
        form.LoadDrawings([Reopened()]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Field<CheckBox>(form, "chkShowOriginal").Checked = true;
        Assert.Null(view.OriginalEntities);

        Invoke(form, "OnSimplifyClick", null, EventArgs.Empty);
        Invoke(form, "OnShowOriginalChanged", null, EventArgs.Empty);

        Assert.Null(item.RawEntities);
        Assert.Same(item.OriginalEntities, view.OriginalEntities);
        form.Close();
    });

    private static string WriteHole(string directory)
    {
        var layer = new CadLayer("0");
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Inches;

        void Add(double x1, double y1, double x2, double y2) =>
            document.Entities.Add(new CadLine(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0)) { Layer = layer });

        Add(0, 0, 4, 0);
        Add(4, 0, 4, 6);
        Add(4, 6, 0, 6);
        Add(0, 6, 0, 0);

        const double radius = 0.3125;
        document.Entities.Add(HoleArc(210, 30, radius, layer));
        document.Entities.Add(HoleArc(30, 215.5, radius, layer));
        var overshoot = OnHole(215.5, radius);
        var seam = OnHole(210, radius);
        Add(overshoot.X, overshoot.Y, seam.X, seam.Y);

        var path = Path.Combine(directory, "hole.dxf");
        DxfWriter.Write(path, document, false);
        return path;
    }

    private static CadArc HoleArc(double start, double end, double radius, CadLayer layer) =>
        new()
        {
            Center = new XYZ(2, 3, 0),
            Radius = radius,
            StartAngle = Angle.ToRadians(start),
            EndAngle = Angle.ToRadians(end),
            Layer = layer,
        };

    private static Vector OnHole(double degrees, double radius)
    {
        var angle = Angle.ToRadians(degrees);
        return new Vector(2 + radius * System.Math.Cos(angle), 3 + radius * System.Math.Sin(angle));
    }

    private static Drawing Reopened()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
        };
        var layer = new Layer("0") { IsVisible = true };
        foreach (var e in entities)
            e.Layer = layer;
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "reopened" }, entities,
            new List<Bend>(), 1, null, null);
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(CadConverterForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(CadConverterForm form, string name, params object?[] args) =>
        typeof(CadConverterForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
}
