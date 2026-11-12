using System.Drawing;
using System.Windows.Forms;
using OpenNest.Desktop;

namespace OpenNest.Forms;

/// <summary>
/// Sizing shared by the large browse dialogs (File &gt; Open, Drawing Library, Shape Library)
/// so they open at the same size.
/// </summary>
internal static class DialogBounds
{
    /// <summary>
    /// Opens the dialog as a large normal window centered in the working area of the owner's
    /// screen, so its bottom row stays clear of the taskbar. It is deliberately not maximized
    /// with fixed <see cref="Form.MaximizedBounds"/>: Windows reads those relative to the
    /// monitor, so bounds taken from a secondary screen moved the modal dialog off-screen there
    /// and left the main window unusable. Call from Load with
    /// <see cref="FormStartPosition.Manual"/>.
    /// </summary>
    public static void FitToOwnerScreen(Form form)
    {
        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        var bounds = LargeDialogBounds.Centered(
            new PixelRect(area.X, area.Y, area.Width, area.Height),
            form.MinimumSize.Width,
            form.MinimumSize.Height);
        form.Bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
