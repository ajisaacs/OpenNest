using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class RotationRangeDialTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void TriedAngles_FollowTheEnginesRotationPolicy()
    {
        Assert.Equal(new[] { 0d, 90d, 180d, 270d }, RotationRangeDial.TriedAngles(10, 20, 0, false));
        Assert.Equal(7, RotationRangeDial.TriedAngles(0, 90, 15, false).Count);
        Assert.Equal(14, RotationRangeDial.TriedAngles(0, 90, 15, true).Count);
        Assert.Equal(new[] { 30d }, RotationRangeDial.TriedAngles(30, 30, 360, false));
        Assert.Empty(RotationRangeDial.TriedAngles(90, 0, 15, false));
    }

    [Fact]
    public void Properties_KeepTheStartAtOrBeforeTheEnd_AndDoNotRaiseTheOperatorEvent()
    {
        using var dial = new RotationRangeDial();
        var raised = 0;
        dial.AnglesChanged += (_, _) => raised++;

        dial.EndAngle = 90;
        dial.StartAngle = 120;
        Assert.Equal((120d, 120d), (dial.StartAngle, dial.EndAngle));
        dial.EndAngle = 400;
        dial.StartAngle = -5;
        Assert.Equal((0d, 360d), (dial.StartAngle, dial.EndAngle));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Keys_MoveTheActiveHandleWithinTheOtherHandle()
    {
        using var dial = new RotationRangeDial { StartAngle = 0, EndAngle = 30 };
        var raised = 0;
        dial.AnglesChanged += (_, _) => raised++;

        dial.ActiveHandle = DialHandle.Start;
        dial.MoveActive(15);
        dial.MoveActive(15);
        dial.MoveActive(15);
        Assert.Equal((30d, 30d), (dial.StartAngle, dial.EndAngle));

        dial.ActiveHandle = DialHandle.End;
        dial.MoveActive(-1);
        Assert.Equal(30d, dial.EndAngle);
        dial.MoveActive(1);
        Assert.Equal(31d, dial.EndAngle);
        Assert.Equal(3, raised);
    }

    [Fact]
    public void Dragging_MovesTheNearerHandle_AndSweepsTheEndUpToAFullTurn()
    {
        StaTestThread.Run(
            () =>
            {
                using var form = new Form { ClientSize = new Size(240, 240) };
                var dial = new DraggableDial { Bounds = new Rectangle(0, 0, 200, 200), StartAngle = 0, EndAngle = 90, StepAngle = 15 };
                form.Controls.Add(dial);
                form.Show();
                var raised = 0;
                dial.AnglesChanged += (_, _) => raised++;

                // Pressing away from the handles at 180° takes the nearer (end) handle there.
                dial.Down(At(dial, 180));
                Assert.Equal((0d, 180d), (dial.StartAngle, dial.EndAngle));

                // The drag follows the pointer past 0°, stopping at a full turn rather than wrapping to 10°.
                foreach (var angle in new[] { 270d, 300d, 340d, 10d, 40d })
                    dial.Drag(At(dial, angle));
                dial.Up(At(dial, 40));
                Assert.Equal((0d, 360d), (dial.StartAngle, dial.EndAngle));

                // Backing off from the full turn moves the end with the pointer again.
                dial.Down(At(dial, 300));
                Assert.Equal(300d, dial.EndAngle);
                dial.Up(At(dial, 300));
                Assert.True(raised > 0);
            },
            TestTimeout,
            "The rotation dial drag test did not complete.");
    }

    // A point inside the dial at a small radius, so it is never on a handle.
    private static Point At(Control dial, double degrees)
    {
        var radians = degrees * System.Math.PI / 180;
        return new Point(
            (int)System.Math.Round(dial.Width / 2.0 + 30 * System.Math.Cos(radians)),
            (int)System.Math.Round(dial.Height / 2.0 - 30 * System.Math.Sin(radians)));
    }

    private sealed class DraggableDial : RotationRangeDial
    {
        public void Down(Point point) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));

        public void Drag(Point point) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0));

        public void Up(Point point) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
    }
}
