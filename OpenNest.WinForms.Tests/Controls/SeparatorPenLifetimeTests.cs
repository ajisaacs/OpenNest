using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class SeparatorPenLifetimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisposeReleasesBothPensAndDisposesTheControlOnlyOnce(bool horizontal) => RunSta(() =>
    {
        using var control = CreateSeparator(horizontal);
        using var child = new Control();
        control.Controls.Add(child);
        _ = control.Handle;
        var lightPen = GetPen(control, "lightPen");
        var darkPen = GetPen(control, "darkPen");
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawLine(lightPen, 0, 0, 3, 0);
        graphics.DrawLine(darkPen, 0, 1, 3, 1);
        var disposedEvents = 0;
        control.Disposed += (_, _) => disposedEvents++;

        control.Dispose();

        Assert.True(control.IsDisposed);
        Assert.True(child.IsDisposed);
        Assert.False(control.IsHandleCreated);
        Assert.Equal(1, disposedEvents);
        Assert.Throws<ArgumentException>(() => graphics.DrawLine(lightPen, 0, 0, 3, 0));
        Assert.Throws<ArgumentException>(() => graphics.DrawLine(darkPen, 0, 1, 3, 1));

        control.Dispose();

        // Control.Dispose(bool) re-raises Disposed on every call; only cleanup is idempotent.
        Assert.True(control.IsDisposed);
        Assert.Equal(2, disposedEvents);
        Assert.Throws<ArgumentException>(() => graphics.DrawLine(lightPen, 0, 0, 3, 0));
        Assert.Throws<ArgumentException>(() => graphics.DrawLine(darkPen, 0, 1, 3, 1));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PaintingKeepsOrientationColorsStylesAndResizeBehavior(bool horizontal) => RunSta(() =>
    {
        using var control = CreateSeparator(horizontal);
        Assert.True(GetStyle(control, ControlStyles.OptimizedDoubleBuffer));
        Assert.False(GetStyle(control, ControlStyles.Selectable));
        control.Size = new Size(40, 20);
        _ = control.Handle;
        var paintEvents = 0;
        var resizeEvents = 0;
        var invalidatedEvents = 0;
        control.Paint += (_, _) => paintEvents++;
        control.Resize += (_, _) => resizeEvents++;
        control.Invalidated += (_, _) => invalidatedEvents++;

        AssertPainting(control, horizontal);
        Assert.Equal(1, paintEvents);

        control.Size = new Size(30, 40);

        Assert.Equal(1, resizeEvents);
        Assert.True(invalidatedEvents > 0);
        AssertPainting(control, horizontal);
        Assert.Equal(2, paintEvents);
    });

    private static Control CreateSeparator(bool horizontal) =>
        horizontal ? new HorizontalLine() : new VerticalLine();

    private static Pen GetPen(Control control, string name) =>
        Assert.IsType<Pen>(control.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));

    private static bool GetStyle(Control control, ControlStyles style) =>
        Assert.IsType<bool>(typeof(Control)
            .GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [style]));

    private static void AssertPainting(Control control, bool horizontal)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var background = Color.Magenta;
        graphics.Clear(background);
        using var args = new PaintEventArgs(graphics, control.ClientRectangle);
        control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [args]);

        var midpoint = (horizontal ? control.Height : control.Width) / 2;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var position = horizontal ? y : x;
                var expected = background;
                if (position == midpoint)
                    expected = ProfessionalColors.SeparatorDark;
                else if (position == midpoint + 1)
                    expected = ProfessionalColors.SeparatorLight;
                Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The STA test did not complete.");
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
