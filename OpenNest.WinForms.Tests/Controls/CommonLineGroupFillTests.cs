using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CommonLines;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.WinForms.Tests.Forms;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>Ctrl+F while pasting a copied common-line group fills with whole, joined groups.</summary>
[Collection("Fill operation lifetime")]
public class CommonLineGroupFillTests
{
    private const double Kerf = 0.06;

    [Fact]
    public void FillingWithACopiedJoinedPairPlacesJoinedPairs() => StaTestThread.Run(() =>
    {
        var plate = new Plate(40, 20) { PartSpacing = 0.25 };
        var wide = new Part(new Drawing("wide", Rectangle(6, 4)), new Vector(2, 2));
        var small = new Part(new Drawing("small", Rectangle(3, 2)), new Vector(2, 6 + Kerf));
        plate.Parts.Add(wide);
        plate.Parts.Add(small);
        plate.JoinCommonLine(new[] { wide, small }, Kerf);
        using var view = new PlateView { Plate = plate };

        // Edit > Cut starts the clone and removes the sources, so the fill takes the whole plate.
        view.SetAction(typeof(ActionClone), new List<Part> { wide, small });
        plate.Parts.Remove(wide);
        plate.Parts.Remove(small);
        UndoTestEnv.Invoke(view.Actions.CurrentAction, "Fill");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (view.IsFillInProgress)
        {
            Assert.True(DateTime.UtcNow < deadline, "The fill did not finish.");
            Application.DoEvents();
            Thread.Sleep(5);
        }

        Assert.True(plate.CommonLines.Count >= 4, $"placed {plate.CommonLines.Count} groups");
        Assert.Equal(2 * plate.CommonLines.Count, plate.Parts.Count);
        Assert.All(plate.CommonLines, group =>
        {
            Assert.Equal(Kerf, group.Kerf);
            Assert.Equal(["wide", "small"], group.Members.Select(m => m.BaseDrawing.Name));
            Assert.All(group.Members, member => Assert.Contains(member, plate.Parts));
            Assert.True(CommonLineGeometry.Check(group).IsIntact);
        });
    }, TimeSpan.FromSeconds(90), "Common-line group fill test timed out.");

    private static CNC.Program Rectangle(double length, double width)
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(length, 0);
        program.LineTo(length, width);
        program.LineTo(0, width);
        program.LineTo(0, 0);
        return program;
    }
}
