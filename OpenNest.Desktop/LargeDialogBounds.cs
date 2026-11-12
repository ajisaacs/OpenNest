namespace OpenNest.Desktop;

/// <summary>
/// Sizing shared by the large browse dialogs (File &gt; Open, Drawing Library, Shape Library) so
/// they open at the same size: a normal window nine tenths of the owner screen's working area,
/// centered in it so its bottom row stays clear of the taskbar.
/// </summary>
public static class LargeDialogBounds
{
    /// <summary>
    /// The bounds in <paramref name="workingArea"/> (screen pixels), never smaller than the
    /// dialog's minimum size and never starting left of or above the working area.
    /// </summary>
    public static PixelRect Centered(PixelRect workingArea, int minimumWidth, int minimumHeight)
    {
        var width = System.Math.Max(minimumWidth, workingArea.Width * 9 / 10);
        var height = System.Math.Max(minimumHeight, workingArea.Height * 9 / 10);
        return new PixelRect(
            System.Math.Max(workingArea.X, workingArea.X + (workingArea.Width - width) / 2),
            System.Math.Max(workingArea.Y, workingArea.Y + (workingArea.Height - height) / 2),
            width,
            height);
    }
}
