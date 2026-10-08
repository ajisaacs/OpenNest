using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class EtchVisibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EtchRemainsVisibleWhenNoCutIntersectsViewport(bool includeCut, bool preview)
        => StaTestThread.Run(() =>
        {
            var program = new CNC.Program();
            if (includeCut)
                AddSquare(program, 0, LayerType.Cut);
            AddSquare(program, 100, LayerType.Scribe);
            var part = new Part(new Drawing("Panned etch", program));
            using var view = new PannedView();
            if (preview)
                view.Previews.SetActiveParts(new List<Part> { part });
            else
                view.Plate.Parts.Add(part);
            var layout = preview ? Assert.Single(view.PreviewParts) : Assert.Single(view.LayoutParts);
            Assert.False(layout.Path.GetBounds().IntersectsWith(view.GetViewBounds()));
            using var image = new Bitmap(30, 30);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.White);
            g.TranslateTransform(-95, -95);
            view.Renderer.DrawParts(g);
            Assert.Equal(Color.Lime.ToArgb(), image.GetPixel(5, 15).ToArgb());
            Assert.Equal(Color.White.ToArgb(), image.GetPixel(8, 8).ToArgb());
            if (!includeCut)
                Assert.Equal(0, layout.Path.PointCount);
        }, TimeSpan.FromMinutes(1), "Panned etch test timed out.");

    [Fact]
    public void EtchOnlyPart_RemainsSelectableAndHoverableWithoutCutMaterial()
        => StaTestThread.Run(() =>
        {
            var program = new CNC.Program();
            AddSquare(program, 100, LayerType.Scribe);
            var part = new Part(new Drawing("etch-only", program));
            using var view = new PannedView();
            view.Plate.Parts.Add(part);
            var layout = Assert.Single(view.LayoutParts);
            Assert.Equal(0, layout.Path.PointCount);
            Assert.True(layout.EtchPath.PointCount > 0);

            Assert.Same(layout, view.Selection.GetPartAtGraphPoint(new PointF(110, 110)));
            Assert.Null(view.Selection.GetPartAtGraphPoint(new PointF(50, 50)));
            Assert.Contains(layout, view.Selection.GetPartsFromWindow(
                new RectangleF(105, 105, 10, 10), SelectionType.Intersect));
            Assert.Contains(layout, view.Selection.GetPartsFromWindow(
                new RectangleF(95, 95, 30, 30), SelectionType.Contains));

            typeof(PlateView).GetField("hoverPending", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, true);
            typeof(PlateView).GetField("hoverPoint", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, new Point(15, 15));
            typeof(PlateView).GetMethod("HoverCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, null);
            Assert.Same(layout, typeof(PlateView).GetField("hoveredPart",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
        }, TimeSpan.FromMinutes(1), "Etch selection test timed out.");

    private static void AddSquare(CNC.Program program, double origin, LayerType layer)
    {
        program.MoveTo(origin, origin);
        foreach (var end in new[]
        {
            new Vector(origin + 20, origin), new Vector(origin + 20, origin + 20),
            new Vector(origin, origin + 20), new Vector(origin, origin),
        })
            program.Codes.Add(new LinearMove(end) { Layer = layer });
    }

    private sealed class PannedView : PlateView
    {
        public PannedView() : base(ColorSchemeRegistry.Get("Workshop"))
        {
            Size = new System.Drawing.Size(30, 30);
            DrawBounds = false;
            Matrix = new Matrix();
            origin = new PointF(-95, -95);
        }
    }
}
