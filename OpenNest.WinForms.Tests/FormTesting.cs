using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests;

/// <summary>Drives forms and controls from tests. Import with <c>using static</c>.</summary>
internal static class FormTesting
{
    /// <summary>Pumps messages until the condition holds, failing after 15 seconds.</summary>
    public static void PumpUntil(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(condition(), $"Timed out waiting for {what}.");
    }

    /// <summary>Shows the form at the screen origin at 1200 x 800.</summary>
    public static void Show(Form form)
    {
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Size = new System.Drawing.Size(1200, 800);
        form.Show();
    }

    /// <summary>Calls a protected mouse handler, such as OnMouseDown, with a left click.</summary>
    public static void Mouse(Control control, string handler, Point at) =>
        typeof(Control).GetMethod(
                handler, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(MouseEventArgs)])!
            .Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0)]);

    /// <summary>Every descendant of <paramref name="root"/> of type T, depth first.</summary>
    public static IEnumerable<T> All<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var nested in All<T>(child))
                yield return nested;
        }
    }

    /// <summary>Sends a key to the view through its protected OnKeyDown.</summary>
    public static void PressKey(PlateView view, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new KeyEventArgs(key) });

    /// <summary>Calls one of the view's protected event methods, such as OnMouseDown.</summary>
    public static void Raise(PlateView view, string method, EventArgs args) =>
        typeof(PlateView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { args });
}
