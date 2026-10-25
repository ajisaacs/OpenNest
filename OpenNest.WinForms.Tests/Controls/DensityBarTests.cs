using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The Nesting Progress utilization bar paints at every value and size: a sliver of fill
/// (about 1% on the default bar) once threw from a zero-size corner arc.
/// </summary>
public class DensityBarTests
{
    [Theory]
    [InlineData(60, 8, 0.0)]
    [InlineData(60, 8, 0.01)]
    [InlineData(60, 8, 0.02)]
    [InlineData(60, 8, 0.05)]
    [InlineData(60, 8, 0.5)]
    [InlineData(60, 8, 1.0)]
    [InlineData(60, 4, 0.5)]
    [InlineData(3, 2, 1.0)]
    [InlineData(1, 1, 1.0)]
    public void Paints_without_throwing(int width, int height, double value)
    {
        using var bar = new DensityBar { Size = new Size(width, height), Value = value };
        using var bitmap = new Bitmap(System.Math.Max(width, 1), System.Math.Max(height, 1));
        using var g = Graphics.FromImage(bitmap);

        typeof(DensityBar).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { new PaintEventArgs(g, bar.ClientRectangle) });
    }
}
