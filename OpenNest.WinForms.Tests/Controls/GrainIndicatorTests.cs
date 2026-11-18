using System.Drawing;
using OpenNest.Controls;
using OpenNest.Math;
using OpenNest.Undo;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The grain arrow off the plate's lower-left corner: shown with bend lines, and a click turns
/// the grain a quarter turn without touching the selection.
/// </summary>
public class GrainIndicatorTests
{
    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 0)]
    [InlineData(30, 120)]
    [InlineData(150, 60)]
    public void Turned_TurnsTheGrainAQuarterTurn(double degrees, double expected)
    {
        Assert.Equal(Angle.ToRadians(expected), GrainIndicator.Turned(Angle.ToRadians(degrees)), 9);
    }

    [Fact]
    public void Bounds_SitDiagonallyOffThePlatesLowerLeftCornerWhenThereIsRoom()
    {
        using var view = NewView(out var plate);
        view.ZoomToControlPoint(new Point(view.Width / 2, view.Height / 2), 0.5f);

        var bounds = GrainIndicator.Bounds(view, plate);
        var corner = view.PointWorldToGraph(0, 0);

        Assert.True(bounds.Right < corner.X, "left of the plate");
        Assert.True(bounds.Top > corner.Y, "below the plate");
    }

    [Fact]
    public void Bounds_StayInsideTheViewWhenZoomedToFit()
    {
        using var view = NewView(out var plate);

        var bounds = GrainIndicator.Bounds(view, plate);
        var topLeft = view.PointGraphToControl(bounds.Location);
        var bottomRight = view.PointGraphToControl(new PointF(bounds.Right, bounds.Bottom));

        Assert.True(topLeft.X >= 0, "inside the left edge");
        Assert.True(bottomRight.Y <= view.ClientSize.Height, "inside the bottom edge");
    }

    [Fact]
    public void ClickingTheIndicator_TurnsTheGrainAndLeavesTheSelection()
    {
        using var view = NewView(out var plate);
        view.ShowBendLines = true;
        view.SelectAll();

        Click(view, plate);

        Assert.Equal(Angle.HalfPI, plate.GrainAngle, 9);
        Assert.Single(view.SelectedParts);

        Click(view, plate);

        Assert.Equal(0, plate.GrainAngle, 9);
    }

    [Fact]
    public void ClickingWhereTheIndicatorWouldBe_DoesNothingWithoutBendLines()
    {
        using var view = NewView(out var plate);

        Click(view, plate);

        Assert.Equal(0, plate.GrainAngle);
    }

    [Fact]
    public void TurnGrain_IsOneUndoableChange()
    {
        using var view = NewView(out var plate);
        var labels = new List<string>();
        view.BeginChangeHandler = (label, _) =>
        {
            labels.Add(label);
            return ChangeScope.Detached();
        };

        Assert.True(view.TurnGrain());

        Assert.Equal(["Turn Grain"], labels);
        Assert.Equal(Angle.HalfPI, plate.GrainAngle, 9);
    }

    private static PlateView NewView(out Plate plate)
    {
        plate = new Plate(new OpenNest.Geometry.Size(60, 120)) { Quadrant = 1 };
        plate.Parts.Add(new Part(new Drawing("square", new CNC.Program()), new OpenNest.Geometry.Vector(10, 10)));
        var view = new PlateView { Plate = plate, Size = new Size(800, 600) };
        view.ZoomToFit();
        return view;
    }

    private static void Click(PlateView view, Plate plate)
    {
        var bounds = GrainIndicator.Bounds(view, plate);
        var center = view.PointGraphToControl(
            new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        Mouse(view, "OnMouseDown", center);
        Mouse(view, "OnMouseUp", center);
    }
}
