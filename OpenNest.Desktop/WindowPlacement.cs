using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenNest.Desktop;

/// <summary>A rectangle in screen pixels, standing in for each front end's own type.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// A window's remembered size, position and maximized state, stored in the shared
/// user-settings file under the names the WinForms app already uses
/// (<c>&lt;name&gt;Location</c> "x, y", <c>&lt;name&gt;Size</c> "width, height" and
/// <c>&lt;name&gt;State</c> "Normal" or "Maximized", all in screen pixels). A window is never
/// restored minimized.
/// </summary>
public sealed record WindowPlacement(
    (int X, int Y)? Location,
    (int Width, int Height)? Size,
    bool Maximized)
{
    /// <summary>
    /// The main window's names: MainWindowLocation, MainWindowSize and MainWindowState.
    /// </summary>
    public const string MainWindow = "MainWindow";

    /// <summary>The placement saved under <paramref name="name"/>, or null if none.</summary>
    public static WindowPlacement Read(IReadOnlyDictionary<string, string> settings, string name)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var location = settings.TryGetValue(name + "Location", out var l) ? ParsePair(l) : null;
        var size = settings.TryGetValue(name + "Size", out var s) ? ParsePair(s) : null;
        if (size is { } pair && (pair.A <= 0 || pair.B <= 0))
            size = null;
        var maximized = settings.TryGetValue(name + "State", out var state)
            && string.Equals(state?.Trim(), "Maximized", StringComparison.OrdinalIgnoreCase);

        if (location is null && size is null && !maximized)
            return null;

        return new WindowPlacement(
            location is { } at ? (at.A, at.B) : null,
            size is { } wh ? (wh.A, wh.B) : null,
            maximized);
    }

    /// <summary>Stores it under <paramref name="name"/> in the WinForms format.</summary>
    public void WriteTo(IDictionary<string, string> settings, string name)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Location is { } at)
            settings[name + "Location"] = Format(at.X, at.Y);
        if (Size is { } size)
            settings[name + "Size"] = Format(size.Width, size.Height);
        settings[name + "State"] = Maximized ? "Maximized" : "Normal";
    }

    /// <summary>
    /// What still fits the current screens: the position is kept only when it lies on one of
    /// <paramref name="workingAreas"/>, and the size only when it fits that screen (the first
    /// screen when the position was dropped), as the WinForms app restores its main window.
    /// </summary>
    public WindowPlacement FitTo(IReadOnlyList<PixelRect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (workingAreas.Count == 0)
            return this with { Location = null, Size = null };

        PixelRect? screen = null;
        if (Location is { } at)
        {
            foreach (var area in workingAreas)
            {
                if (area.Contains(at.X, at.Y))
                {
                    screen = area;
                    break;
                }
            }
        }

        var target = screen ?? workingAreas[0];
        var fits = Size is { } wh && wh.Width <= target.Width && wh.Height <= target.Height;
        var size = fits ? Size : null;
        return this with { Location = screen is null ? null : Location, Size = size };
    }

    private static (int A, int B)? ParsePair(string text)
    {
        var parts = text?.Split(',');
        if (parts is not { Length: 2 })
            return null;

        return TryParseInt(parts[0], out var a) && TryParseInt(parts[1], out var b) ? (a, b) : null;
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string Format(int a, int b) =>
        string.Create(CultureInfo.InvariantCulture, $"{a}, {b}");
}
