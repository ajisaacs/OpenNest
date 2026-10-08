using System.Drawing;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class DrawingMetadataColorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MetadataRefreshKeepsPaletteOwnershipWithoutRebuildingPrograms(
        bool updateGeometry, bool changeName) => StaTestThread.Run(() =>
    {
        var previousScheme = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorScheme.Default));
        var previousPartColors = Drawing.PartColors;
        try
        {
            var program = new CNC.Program();
            program.MoveTo(0, 0);
            program.LineTo(100, 0);
            program.LineTo(100, 100);
            program.LineTo(0, 100);
            program.LineTo(0, 0);
            var drawing = new Drawing("Original", program) { Color = Color.Coral };
            var nest = new Nest("Metadata palette");
            nest.Drawings.Add(drawing);
            var part = new Part(drawing);
            nest.CreatePlate().Parts.Add(part);
            using var editor = new EditNestForm(nest);
            editor.PlateView.SetOverlapAutoCheck(null);
            editor.Show();
            editor.PlateView.Matrix.Reset();
            var layout = Assert.Single(editor.PlateView.LayoutParts);
            layout.Update(editor.PlateView);
            var path = layout.Path;
            var placedProgram = part.Program;
            var before = NestWriter.GetProgramText(placedProgram);

            using var properties = new EditDrawingForm();
            properties.LoadDrawing(drawing);
            if (changeName)
                Assert.IsType<TextBox>(properties.Controls.Find("nameBox", true).Single()).Text = "Renamed";
            properties.SaveDrawing(drawing);
            // The live successful-dialog handler calls this color-only refresh.
            // Modal interaction itself is covered separately by operator acceptance.
            editor.RefreshDrawingColor(drawing);

            Assert.Equal(changeName ? "Renamed" : "Original", drawing.Name);
            Assert.Same(path, layout.Path);
            Assert.Same(placedProgram, part.Program);
            Assert.Equal(before, NestWriter.GetProgramText(part.Program));
            Assert.Equal(Color.Coral, drawing.Color);

            var workshop = ColorSchemeRegistry.Get("Workshop");
            ColorSchemeRegistry.Apply(workshop);
            Assert.Equal(workshop.PartColors[0], drawing.Color);
            if (updateGeometry)
                layout.Update(editor.PlateView);
            layout.IsSelected = false;
            using var image = new Bitmap(120, 120);
            using var g = Graphics.FromImage(image);
            layout.Draw(g);
            Assert.Equal(drawing.Color.ToArgb(), image.GetPixel(20, 20).ToArgb());
            Assert.Equal(before, NestWriter.GetProgramText(part.Program));
        }
        finally
        {
            ColorSchemeRegistry.Apply(previousScheme);
            Drawing.PartColors = previousPartColors;
        }
    }, TimeSpan.FromMinutes(1), "Metadata color test timed out.");
}
