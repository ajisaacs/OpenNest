using OpenNest.Geometry;

namespace OpenNest.Desktop.Interaction;

/// <summary>How a dragged selection window picks parts and cut-offs.</summary>
public enum WindowSelectionMode
{
    /// <summary>Anything the window touches: dragged from right to left.</summary>
    Intersect,

    /// <summary>Only what lies wholly inside the window: dragged from left to right.</summary>
    Contains,
}

/// <summary>
/// A selection window between the point a drag started and where it is now, in plate (world)
/// coordinates. As in the WinForms view, dragging to the right selects what the window contains
/// and dragging to the left (or straight up or down) selects what it touches.
/// </summary>
public readonly record struct SelectionWindow(Vector First, Vector Second)
{
    public WindowSelectionMode Mode =>
        First.X < Second.X ? WindowSelectionMode.Contains : WindowSelectionMode.Intersect;

    public double Left => System.Math.Min(First.X, Second.X);

    public double Right => System.Math.Max(First.X, Second.X);

    public double Bottom => System.Math.Min(First.Y, Second.Y);

    public double Top => System.Math.Max(First.Y, Second.Y);

    /// <summary>The window as a box (a fresh copy each time).</summary>
    public Box Box => new(Left, Bottom, Right - Left, Top - Bottom);

    /// <summary>Whether <paramref name="point"/> lies in the window, edges included.</summary>
    public bool Contains(Vector point) =>
        point.X >= Left && point.X <= Right && point.Y >= Bottom && point.Y <= Top;

    /// <summary>Whether <paramref name="box"/> lies in the window, edges included.</summary>
    public bool Contains(Box box) =>
        box.Left >= Left && box.Right <= Right && box.Bottom >= Bottom && box.Top <= Top;

    /// <summary>Whether <paramref name="box"/> and the window overlap, touching edges included.</summary>
    public bool Overlaps(Box box) =>
        box.Left <= Right && box.Right >= Left && box.Bottom <= Top && box.Top >= Bottom;

    /// <summary>Whether the segment from <paramref name="start"/> to <paramref name="end"/> enters the window.</summary>
    public bool Intersects(Vector start, Vector end)
    {
        if (Contains(start) || Contains(end))
            return true;

        // Clip the segment against all four edges (Liang-Barsky): a segment can cross the window
        // without ending inside it.
        var min = 0.0;
        var max = 1.0;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Clip(-dx, start.X - Left, ref min, ref max)
            && Clip(dx, Right - start.X, ref min, ref max)
            && Clip(-dy, start.Y - Bottom, ref min, ref max)
            && Clip(dy, Top - start.Y, ref min, ref max);
    }

    /// <summary>The window in the frame whose origin is at <paramref name="origin"/>, such as a placed part's program frame.</summary>
    public SelectionWindow RelativeTo(Vector origin) => new(First - origin, Second - origin);

    private static bool Clip(double p, double q, ref double min, ref double max)
    {
        if (p == 0)
            return q >= 0;
        var t = q / p;
        if (p < 0)
        {
            if (t > max)
                return false;
            min = System.Math.Max(min, t);
        }
        else
        {
            if (t < min)
                return false;
            max = System.Math.Min(max, t);
        }
        return min <= max;
    }
}
