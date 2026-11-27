using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class FormedAfterCuttingFormTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawingProperties_BendLinesCheckTheBoxAndUncheckingOverridesThem(bool hasBends) =>
        StaTestThread.Run(() =>
        {
            var drawing = new Drawing("part", new Program());
            if (hasBends)
            {
                drawing.Bends.Add(new Bend
                {
                    StartPoint = new Vector(0, 5),
                    EndPoint = new Vector(10, 5),
                });
            }
            using var form = new EditDrawingForm();

            form.LoadDrawing(drawing);
            var box = Assert.IsType<CheckBox>(form.Controls.Find("formedBox", true).Single());
            Assert.Equal(hasBends, box.Checked);

            form.SaveDrawing(drawing);
            Assert.Null(drawing.FormedOverride);

            box.Checked = !hasBends;
            form.SaveDrawing(drawing);
            Assert.Equal(!hasBends, drawing.FormedOverride);
            Assert.Equal(!hasBends, drawing.IsFormed);
        }, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
