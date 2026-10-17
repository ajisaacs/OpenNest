using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>Right-click on a machine clamp takes it off the current sheet or puts it back.</summary>
public class PlateViewClampMenuTests
{
    // Default view scale is about one pixel per inch: allow for rounding to whole pixels.
    private const double Tolerance = 1.0;

    private static readonly double[] Centers = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private static Plate RailPlate()
    {
        var plate = new Plate(48, 120);
        plate.SetClampRail(new ClampRail(1.5, 0.625, Centers));
        return plate;
    }

    [Fact]
    public void RemovesAClampAndPutsItBack() => StaTestThread.Run(() =>
    {
        var plate = RailPlate();
        using var view = new PlateView { Plate = plate };
        var onClamp3 = view.PointWorldToControl(new Vector(36, 0.3125));

        var menu = view.ClampMenu.BuildMenu(onClamp3, Tolerance);
        Assert.NotNull(menu);
        var remove = Assert.Single(menu.Items.Cast<System.Windows.Forms.ToolStripItem>());
        Assert.Equal("Remove clamp-3 from This Sheet", remove.Text);

        remove.PerformClick();
        Assert.Equal(new[] { "clamp-3" }, plate.RemovedClamps);
        Assert.DoesNotContain(plate.ClampZones, zone => zone.Id == "clamp-3");
        Assert.StartsWith("Removed clamp-3", view.Status);

        // A removed clamp is still hit (it is drawn dashed) and offers to come back.
        var restore = Assert.Single(
            view.ClampMenu.BuildMenu(onClamp3, Tolerance)!.Items.Cast<System.Windows.Forms.ToolStripItem>());
        Assert.Equal("Put clamp-3 Back on This Sheet", restore.Text);

        restore.PerformClick();
        Assert.Empty(plate.RemovedClamps);
        Assert.Equal(8, plate.ClampZones.Count);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void OffersNothingAwayFromAClampOrWhenClampsCannotBeEdited() => StaTestThread.Run(() =>
    {
        var plate = RailPlate();
        using var view = new PlateView { Plate = plate };
        var onClamp3 = view.PointWorldToControl(new Vector(36, 0.3125));

        Assert.Null(view.ClampMenu.BuildMenu(view.PointWorldToControl(new Vector(28, 20)), Tolerance));

        view.InspectionOnly = true;
        Assert.Null(view.ClampMenu.BuildMenu(onClamp3, Tolerance));
        view.InspectionOnly = false;

        view.Actions.SetAction(typeof(ActionZoomWindow));
        Assert.Null(view.ClampMenu.BuildMenu(onClamp3, Tolerance));
        view.Actions.SetAction(typeof(ActionSelect));

        view.Plate = new Plate(48, 120); // no rail
        Assert.Null(view.ClampMenu.BuildMenu(onClamp3, Tolerance));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void IgnoresAClickFromAMenuOpenedOnAnotherSheet() => StaTestThread.Run(() =>
    {
        var plate = RailPlate();
        using var view = new PlateView { Plate = plate };
        var menu = view.ClampMenu.BuildMenu(view.PointWorldToControl(new Vector(36, 0.3125)), Tolerance);
        var remove = menu!.Items[0];

        view.Plate = RailPlate();
        remove.PerformClick();

        Assert.Empty(plate.RemovedClamps);
        Assert.Empty(view.Plate.RemovedClamps);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
