using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests;

public class LayoutPartColorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BestFitColorOverrideSurvivesPaintAndGeometryUpdate(bool updateGeometry)
        => StaTestThread.Run(() =>
        {
            var program = new CNC.Program();
            program.MoveTo(0, 0);
            program.LineTo(100, 0);
            program.LineTo(100, 100);
            program.LineTo(0, 100);
            program.LineTo(0, 0);
            var drawing = new Drawing("Best fit color", program) { Color = Color.Coral };
            using var cell = new BestFitCell(ColorSchemeRegistry.Get("Classic")) { PartColor = Color.Blue };
            cell.Matrix = new Matrix();
            cell.Plate.Parts.Add(new Part(drawing));
            var layout = Assert.Single(cell.LayoutParts);
            Assert.Equal(Color.Blue, layout.Color);
            if (updateGeometry)
                layout.Update(cell);
            using var image = new Bitmap(120, 120);
            using var g = Graphics.FromImage(image);
            layout.Draw(g);
            Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(20, 20).ToArgb());
            Assert.Equal(Color.Blue, layout.Color);
            Assert.Equal(Color.Coral, drawing.Color);

            // The existing explicit refresh returns color ownership to the drawing.
            layout.Update();
            layout.Draw(g);
            Assert.Equal(Color.Coral.ToArgb(), image.GetPixel(20, 20).ToArgb());
        }, TimeSpan.FromMinutes(1), "Best fit color test timed out.");
}
