using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class LayoutPartColorTests
{
    [Fact]
    public void ValidationMutesOnlyUnrelatedInstancesAndRestoresColors()
        => StaTestThread.Run(() =>
        {
            var program = new CNC.Program();
            program.MoveTo(0, 0);
            program.LineTo(40, 0);
            program.LineTo(40, 40);
            program.LineTo(0, 40);
            program.LineTo(0, 0);
            var drawing = new Drawing("shared drawing", program) { Color = Color.Coral };
            using var view = new PlateView { Width = 240, Height = 100, DrawBounds = false };
            view.Matrix = new Matrix();
            for (var i = 0; i < 3; i++)
                view.Plate.Parts.Add(new Part(drawing) { Location = new Vector(i * 60, 0) });
            using var image = new Bitmap(240, 100);
            using var graphics = Graphics.FromImage(image);
            view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.Overlap, 1, 1, 2, "overlap"));
            view.LayoutParts[2].IsSelected = true;
            view.Renderer.DrawParts(graphics);
            Assert.Equal(Color.Coral.ToArgb(), image.GetPixel(10, 10).ToArgb());
            Assert.Equal(Color.Coral.ToArgb(), image.GetPixel(70, 10).ToArgb());
            Assert.Equal(Color.Gainsboro.ToArgb(), image.GetPixel(130, 10).ToArgb());
            Assert.True(view.LayoutParts[2].IsSelected);
            view.LayoutParts[2].IsSelected = false;

            view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.MissingLeadIn, 1, 3, null, "lead"));
            view.Renderer.DrawParts(graphics);
            Assert.Equal(Color.Gainsboro.ToArgb(), image.GetPixel(10, 10).ToArgb());
            Assert.Equal(Color.Gainsboro.ToArgb(), image.GetPixel(70, 10).ToArgb());
            Assert.Equal(Color.Coral.ToArgb(), image.GetPixel(130, 10).ToArgb());

            view.ClearValidationFinding();
            view.Renderer.DrawParts(graphics);
            for (var i = 0; i < 3; i++)
                Assert.Equal(Color.Coral.ToArgb(), image.GetPixel(i * 60 + 10, 10).ToArgb());
            Assert.Equal(Color.Coral, drawing.Color);
            Assert.All(view.LayoutParts, part => Assert.Equal(Color.Coral, part.Color));
        }, TimeSpan.FromMinutes(1), "Validation color test timed out.");

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
