using System.Drawing;
using System.Drawing.Drawing2D;
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
