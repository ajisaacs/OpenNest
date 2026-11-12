using System;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Rendering;

/// <summary>A point in a view's screen (control) pixels, y down.</summary>
public readonly record struct ScreenPoint(double X, double Y);

/// <summary>
/// A plate or CAD view's world-to-screen mapping: world Y up, screen Y down, a uniform
/// <see cref="Scale"/> in pixels per drawing unit and <see cref="OriginX"/>/<see cref="OriginY"/>, the
/// screen position of the world origin. The zoom rules are the WinForms <c>DrawControl</c>'s: the
/// scale stays within <see cref="MinScale"/>..<see cref="MaxScale"/>, a zoom about a point keeps that
/// point still, and fitting an area leaves <see cref="FitBorder"/> pixels of margin in all.
/// </summary>
public sealed class ViewTransform
{
    /// <summary>The DrawControl's zoom step for one Zoom In.</summary>
    public const double ZoomInFactor = 1.15;

    /// <summary>Total pixels left around a fitted area (half on each side).</summary>
    public const double FitBorder = 50;

    public double Scale { get; private set; } = 1;

    public double OriginX { get; private set; } = 100;

    public double OriginY { get; private set; } = 100;

    public double MinScale { get; set; } = 0.3;

    public double MaxScale { get; set; } = 10000;

    public double ViewportWidth { get; private set; }

    public double ViewportHeight { get; private set; }

    /// <summary>Raised after the scale, origin or viewport changes.</summary>
    public event EventHandler Changed;

    public ScreenPoint WorldToScreen(Vector world) => WorldToScreen(world.X, world.Y);

    public ScreenPoint WorldToScreen(double x, double y) => new(OriginX + x * Scale, OriginY - y * Scale);

    public Vector ScreenToWorld(ScreenPoint screen) => ScreenToWorld(screen.X, screen.Y);

    public Vector ScreenToWorld(double x, double y) => new((x - OriginX) / Scale, (OriginY - y) / Scale);

    public double LengthToScreen(double worldLength) => worldLength * Scale;

    public double LengthToWorld(double screenLength) => screenLength / Scale;

    /// <summary>The world area the viewport shows.</summary>
    public Box VisibleWorld()
    {
        var bottomLeft = ScreenToWorld(0, ViewportHeight);
        return new Box(bottomLeft.X, bottomLeft.Y, ViewportWidth / Scale, ViewportHeight / Scale);
    }

    /// <summary>
    /// Sets the viewport size. After the first size, a resize keeps the view centred by moving
    /// the origin half the change, as the WinForms view does.
    /// </summary>
    public void Resize(double width, double height)
    {
        if (width == ViewportWidth && height == ViewportHeight)
            return;
        OriginX += (width - ViewportWidth) * 0.5;
        OriginY += (height - ViewportHeight) * 0.5;
        ViewportWidth = width;
        ViewportHeight = height;
        OnChanged();
    }

    /// <summary>Moves the view by a screen offset.</summary>
    public void Pan(double dx, double dy)
    {
        if (dx == 0 && dy == 0)
            return;
        OriginX += dx;
        OriginY += dy;
        OnChanged();
    }

    /// <summary>Zooms by <paramref name="factor"/> about the world point under <paramref name="screen"/>.</summary>
    public void ZoomAt(ScreenPoint screen, double factor) => ZoomToPoint(ScreenToWorld(screen), factor);

    /// <summary>
    /// Zooms by <paramref name="factor"/> keeping <paramref name="world"/> at the same screen point;
    /// the factor is cut so the scale stays within its limits.
    /// </summary>
    public void ZoomToPoint(Vector world, double factor)
    {
        var newScale = Scale * factor;
        if (newScale < MinScale)
            factor = MinScale / Scale;
        else if (newScale > MaxScale)
            factor = MaxScale / Scale;

        OriginX -= (world.X * factor - world.X) * Scale;
        OriginY += (world.Y * factor - world.Y) * Scale;
        Scale *= factor;
        OnChanged();
    }

    /// <summary>Zooms in or out by one step about the viewport centre.</summary>
    public void ZoomStep(bool zoomIn) =>
        ZoomAt(new ScreenPoint(ViewportWidth / 2, ViewportHeight / 2), zoomIn ? ZoomInFactor : 1 / ZoomInFactor);

    /// <summary>
    /// Fits <paramref name="area"/> in the viewport, centred, with <see cref="FitBorder"/> pixels of
    /// margin in all; an empty area or viewport changes nothing. Returns whether it changed the view.
    /// </summary>
    public bool ZoomToArea(Box area)
    {
        if (area == null || area.Length <= 0 || area.Width <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
            return false;

        var a = (ViewportHeight - FitBorder) / area.Width;
        var b = (ViewportWidth - FitBorder) / area.Length;
        Scale = System.Math.Clamp(a < b ? a : b, MinScale, MaxScale);

        var px = area.X * Scale;
        var py = area.Y * Scale;
        var pw = area.Length * Scale;
        var ph = area.Width * Scale;
        OriginX = (ViewportWidth - pw) * 0.5 - px;
        OriginY = (ViewportHeight + ph) * 0.5 + py;
        OnChanged();
        return true;
    }

    /// <summary>The current scale and origin, to restore later.</summary>
    public (double Scale, double OriginX, double OriginY) Capture() => (Scale, OriginX, OriginY);

    public void Restore((double Scale, double OriginX, double OriginY) state)
    {
        Scale = System.Math.Clamp(state.Scale, MinScale, MaxScale);
        OriginX = state.OriginX;
        OriginY = state.OriginY;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
