using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>Resizing the plate view scales the view with it, so a fitted plate stays fitted.</summary>
public class PlateViewResizeTests
{
    private static void Run(Action body) =>
        StaTestThread.Run(body, TimeSpan.FromSeconds(30), "Plate view resize test timed out.");

    private static PlateView FittedView()
    {
        var view = new PlateView { Plate = new Plate(60, 120), Size = new System.Drawing.Size(1000, 600) };
        view.ZoomToFit();
        return view;
    }

    private static void AssertPlateInside(PlateView view)
    {
        var lower = view.PointWorldToControl(new Vector(0, 0));
        var upper = view.PointWorldToControl(new Vector(120, 60));
        Assert.InRange(lower.X, 0, view.Width);
        Assert.InRange(upper.X, 0, view.Width);
        Assert.InRange(lower.Y, 0, view.Height);
        Assert.InRange(upper.Y, 0, view.Height);
    }

    [Fact]
    public void NarrowingTheViewKeepsAFittedPlateInView() => Run(() =>
    {
        using var view = FittedView();
        var center = view.PointControlToWorld(500, 300);

        view.Size = new System.Drawing.Size(520, 600);

        AssertPlateInside(view);
        var moved = view.PointControlToWorld(260, 300);
        Assert.Equal(center.X, moved.X, 1);
        Assert.Equal(center.Y, moved.Y, 1);
    });

    [Fact]
    public void RestoringTheSizeRestoresTheZoom() => Run(() =>
    {
        using var view = FittedView();
        var scale = view.ViewScale;

        view.Size = new System.Drawing.Size(520, 600);
        view.Size = new System.Drawing.Size(700, 600);
        view.Size = new System.Drawing.Size(1000, 600);

        Assert.Equal(scale, view.ViewScale, 4);
    });

    [Fact]
    public void ZoomingWhileNarrowMeasuresLaterResizesFromThatZoom() => Run(() =>
    {
        using var view = FittedView();
        view.Size = new System.Drawing.Size(520, 600);
        view.ZoomToPoint(new Vector(60, 30), 2f);
        var zoomed = view.ViewScale;

        view.Size = new System.Drawing.Size(400, 600);

        Assert.Equal(zoomed * (400 - 50) / (520f - 50), view.ViewScale, 3);
    });

    [Fact]
    public void GrowingOnlyTheUnconstrainedSideKeepsTheZoom() => Run(() =>
    {
        using var view = FittedView();
        var scale = view.ViewScale;

        view.Size = new System.Drawing.Size(1000, 900);

        Assert.Equal(scale, view.ViewScale, 4);
    });
}
