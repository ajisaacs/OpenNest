using System.Drawing;
using System.Windows.Forms;

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
        var width = System.Math.Max(form.MinimumSize.Width, area.Width * 9 / 10);
        var height = System.Math.Max(form.MinimumSize.Height, area.Height * 9 / 10);
        form.Bounds = new Rectangle(
            System.Math.Max(area.Left, area.Left + (area.Width - width) / 2),
            System.Math.Max(area.Top, area.Top + (area.Height - height) / 2),
            width,
            height);
    }
}
