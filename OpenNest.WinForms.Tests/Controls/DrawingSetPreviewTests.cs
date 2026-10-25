using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The set preview packs every drawing into one view at one scale, without overlaps, each in
/// its own orientation, and stays inside the view.
/// </summary>
public class DrawingSetPreviewTests
{
    private static Drawing Rectangle(string name, double width, double height, double x = 0, double y = 0)
    {
        var program = new CNC.Program();
        program.Codes.Add(new CNC.RapidMove(new Vector(x, y)));
        program.Codes.Add(new CNC.LinearMove(new Vector(x + width, y)));
        program.Codes.Add(new CNC.LinearMove(new Vector(x + width, y + height)));
        program.Codes.Add(new CNC.LinearMove(new Vector(x, y + height)));
        program.Codes.Add(new CNC.LinearMove(new Vector(x, y)));
        return new Drawing(name, program);
    }

    [Fact]
    public void Arrange_PlacesEveryCellApartAndInsideTheLayout()
    {
        var sizes = new List<(double, double)> { (40, 10), (3, 3), (12, 30), (1, 0.5), (25, 25), (60, 2), (5, 8) };

        var result = DrawingSetPreviewLayout.Arrange(sizes, 800, 600, 18, 90);

        Assert.Equal(sizes.Count, result.Placements.Count);
        Assert.True(result.Scale > 0);
        Assert.True(result.Width * result.Scale <= 800 + 1e-6);
        Assert.True(result.Height * result.Scale <= 600 + 1e-6);
        // The strip is solved together with the scale, so it lands within a pixel of the request.
        Assert.InRange(result.LabelHeight * result.Scale, 17, 19);

        // Each cell covers its part's width (at least the minimum) and its label strip below.
        var cells = result.Placements.Select(p =>
        {
            var (width, height) = sizes[p.Index];
            Assert.InRange(p.PartX, p.CellX - 1e-9, p.CellX + p.CellWidth - width + 1e-9);
            Assert.True(p.CellWidth * result.Scale >= 89);
            Assert.Equal(p.LabelTop, p.PartY, 9);
            return (Left: p.CellX, Right: p.CellX + p.CellWidth, Bottom: p.PartY - result.LabelHeight, Top: p.PartY + height);
        }).ToList();

        foreach (var cell in cells)
        {
            Assert.InRange(cell.Left, -1e-9, result.Width + 1e-9);
            Assert.InRange(cell.Right, -1e-9, result.Width + 1e-9);
            Assert.InRange(cell.Bottom, -result.Height - 1e-9, 1e-9);
            Assert.InRange(cell.Top, -result.Height - 1e-9, 1e-9);
        }

        for (var i = 0; i < cells.Count; i++)
            for (var j = i + 1; j < cells.Count; j++)
            {
                var a = cells[i];
                var b = cells[j];
                var apart = a.Right <= b.Left + 1e-9 || b.Right <= a.Left + 1e-9 || a.Top <= b.Bottom + 1e-9 || b.Top <= a.Bottom + 1e-9;
                Assert.True(apart, $"cells {i} and {j} overlap");
            }
    }

    [Fact]
    public void Arrange_UsesTheViewShape()
    {
        var sizes = Enumerable.Repeat((10.0, 10.0), 12).ToList();

        var wide = DrawingSetPreviewLayout.Arrange(sizes, 1200, 300, 0, 0);
        var tall = DrawingSetPreviewLayout.Arrange(sizes, 300, 1200, 0, 0);

        Assert.True(wide.Width > wide.Height);
        Assert.True(tall.Height > tall.Width);
    }

    [Fact]
    public void Control_ShowsEveryDrawingAtItsOwnSize() => StaTestThread.Run(() =>
    {
        using var view = new DrawingSetPreviewControl { Size = new System.Drawing.Size(900, 700) };
        var drawings = new[]
        {
            (Rectangle("large", 48, 20, x: 100, y: -50), "large ×1"),
            (Rectangle("small", 2, 1), "small ×10"),
            (new Drawing("empty"), "empty ×1"),
        };

        view.ShowDrawings(drawings);

        Assert.Equal(2, view.Parts.Count);
        Assert.Equal(2, view.Plate.Parts.Count);
        var large = view.Parts[0].BoundingBox;
        var small = view.Parts[1].BoundingBox;
        Assert.Equal((48, 20), (System.Math.Round(large.Length, 6), System.Math.Round(large.Width, 6)));
        Assert.Equal((2, 1), (System.Math.Round(small.Length, 6), System.Math.Round(small.Width, 6)));
        Assert.True(large.Right <= small.Left || small.Right <= large.Left || large.Top <= small.Bottom || small.Top <= large.Bottom);
        Assert.Equal((float)view.Layout.Scale, view.ViewScale, 3);

        // Resizing repacks to the new view; sizes never change.
        view.Size = new System.Drawing.Size(400, 900);
        Assert.Equal(48, System.Math.Round(view.Parts[0].BoundingBox.Length, 6));
        Assert.Equal((float)view.Layout.Scale, view.ViewScale, 3);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void Form_LoadsEachMemberAndCountsTheMissingOnes() => StaTestThread.Run(() =>
    {
        var plate = new DrawingLibraryRecord { Id = Guid.NewGuid(), Name = "SYN-PLATE", Revision = 2 };
        var gone = new DrawingLibraryRecord { Id = Guid.NewGuid(), Name = "SYN-GONE", Revision = 1 };
        var set = new DrawingSetRecord
        {
            Name = "Synthetic kit",
            Revision = 3,
            Members = new[]
            {
                new DrawingSetMember { DrawingId = plate.Id, Quantity = 4, Drawing = plate },
                new DrawingSetMember { DrawingId = gone.Id, Quantity = 1, Drawing = gone },
            },
        };
        var requested = new List<(Guid, int)>();

        using var form = new DrawingSetPreviewForm(set, (record, _) =>
        {
            requested.Add((record.Id, record.Revision));
            return Task.FromResult(record == plate ? Rectangle(record.Name, 10, 5) : null);
        });
        form.Preview.Size = new System.Drawing.Size(800, 600);
        var load = form.LoadAsync();
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!load.IsCompleted && DateTime.UtcNow < until)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(load.IsCompletedSuccessfully);

        Assert.Equal(new[] { (plate.Id, 2), (gone.Id, 1) }, requested.OrderBy(r => r.Item1 == gone.Id));
        Assert.Single(form.Preview.Parts);
        Assert.Equal("SYN-PLATE", form.Preview.Parts[0].BaseDrawing.Name);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
