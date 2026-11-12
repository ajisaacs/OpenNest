using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Rendering;

public class ViewTransformTests
{
    private static ViewTransform Sized(double width = 800, double height = 600)
    {
        var view = new ViewTransform();
        view.Resize(width, height);
        return view;
    }

    [Fact]
    public void World_and_screen_round_trip_with_y_flipped()
    {
        var view = Sized();
        view.ZoomToArea(new Box(0, 0, 100, 50));

        var world = new Vector(12.5, -3.25);
        var screen = view.WorldToScreen(world);
        var back = view.ScreenToWorld(screen);

        Assert.Equal(world.X, back.X, 9);
        Assert.Equal(world.Y, back.Y, 9);
        var above = view.WorldToScreen(new Vector(12.5, 10));
        Assert.True(above.Y < screen.Y, "world up is screen up");
        Assert.Equal(view.OriginX, view.WorldToScreen(Vector.Zero).X, 9);
        Assert.Equal(view.OriginY, view.WorldToScreen(Vector.Zero).Y, 9);
        Assert.Equal(25 * view.Scale, view.LengthToScreen(25), 9);
        Assert.Equal(25, view.LengthToWorld(view.LengthToScreen(25)), 9);
    }

    [Fact]
    public void Zoom_to_area_fits_and_centres_with_the_border()
    {
        var view = Sized(800, 600);

        Assert.True(view.ZoomToArea(new Box(10, 20, 100, 50)));

        // Width-limited: (800 - 50) / 100 against (600 - 50) / 50.
        Assert.Equal(7.5, view.Scale, 9);
        var centre = view.WorldToScreen(new Vector(60, 45));
        Assert.Equal(400, centre.X, 9);
        Assert.Equal(300, centre.Y, 9);
    }

    [Fact]
    public void Zoom_to_an_empty_area_or_viewport_changes_nothing()
    {
        var view = new ViewTransform();
        var before = view.Capture();

        Assert.False(view.ZoomToArea(new Box(0, 0, 10, 10)));
        view.Resize(400, 300);
        Assert.False(view.ZoomToArea(new Box(0, 0, 0, 10)));
        Assert.False(view.ZoomToArea(null));
        Assert.Equal(before.Scale, view.Scale);
    }

    [Fact]
    public void Zooming_about_a_point_keeps_it_still()
    {
        var view = Sized();
        view.ZoomToArea(new Box(0, 0, 100, 100));
        var at = new ScreenPoint(123, 456);
        var world = view.ScreenToWorld(at);

        view.ZoomAt(at, 1.7);

        var after = view.WorldToScreen(world);
        Assert.Equal(at.X, after.X, 6);
        Assert.Equal(at.Y, after.Y, 6);
    }

    [Fact]
    public void Zoom_stays_within_its_limits()
    {
        var view = Sized();
        view.ZoomToArea(new Box(0, 0, 100, 100));

        view.ZoomAt(new ScreenPoint(10, 10), 1e9);
        Assert.Equal(view.MaxScale, view.Scale, 9);
        view.ZoomAt(new ScreenPoint(10, 10), 1e-12);
        Assert.Equal(view.MinScale, view.Scale, 9);

        Assert.True(view.ZoomToArea(new Box(0, 0, 1e7, 1e7)));
        Assert.Equal(view.MinScale, view.Scale, 9);
    }

    [Fact]
    public void Zoom_steps_are_the_desktop_factor_about_the_centre()
    {
        var view = Sized(800, 600);
        view.ZoomToArea(new Box(0, 0, 100, 100));
        var scale = view.Scale;
        var centre = view.ScreenToWorld(400, 300);

        view.ZoomStep(zoomIn: true);
        Assert.Equal(scale * 1.15, view.Scale, 9);
        view.ZoomStep(zoomIn: false);
        Assert.Equal(scale, view.Scale, 9);
        var after = view.WorldToScreen(centre);
        Assert.Equal(400, after.X, 6);
        Assert.Equal(300, after.Y, 6);
    }

    [Fact]
    public void Resizing_keeps_the_view_centre()
    {
        var view = Sized(800, 600);
        view.ZoomToArea(new Box(0, 0, 100, 100));
        var centre = view.ScreenToWorld(400, 300);

        view.Resize(1000, 500);

        var moved = view.WorldToScreen(centre);
        Assert.Equal(500, moved.X, 9);
        Assert.Equal(250, moved.Y, 9);
    }

    [Fact]
    public void Panning_moves_the_origin_and_the_visible_area_follows()
    {
        var view = Sized(800, 600);
        view.ZoomToArea(new Box(0, 0, 100, 100));
        var visible = view.VisibleWorld();

        view.Pan(view.Scale * 10, 0);

        var after = view.VisibleWorld();
        Assert.Equal(visible.Left - 10, after.Left, 9);
        Assert.Equal(visible.Bottom, after.Bottom, 9);
        Assert.Equal(800 / view.Scale, after.Length, 9);
        Assert.Equal(600 / view.Scale, after.Width, 9);
    }

    [Fact]
    public void Capture_and_restore_round_trip_and_raise_changed()
    {
        var view = Sized();
        view.ZoomToArea(new Box(0, 0, 100, 100));
        var state = view.Capture();
        view.ZoomAt(new ScreenPoint(0, 0), 3);
        var changes = 0;
        view.Changed += (_, _) => changes++;

        view.Restore(state);

        Assert.Equal(state, view.Capture());
        Assert.Equal(1, changes);
    }
}
