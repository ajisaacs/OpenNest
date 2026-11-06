using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.Fill;

/// <summary>
/// A part placed exactly one part spacing from its neighbor can always be pushed away
/// from it, whatever rounding the drawing's joints carry.
/// </summary>
public class CompactorPushAwayTests
{
    // A PEP drawing (incremental, six decimals) whose arc ends miss their lines by a few
    // millionths, so its offset contour has hairline kinks where the parts touch.
    private const string PepProgram = """
        G91
        G00X0Y0
        G03X0Y0I-2J0
        G00X-10.173792Y2.75
        G01X16.347584Y0
        G02X0.03125Y-0.03125I0J-0.03125
        G01X0Y-5.742929
        G02X-0.03125Y-0.03125I-0.03125J0
        G01X-5.1294Y0
        G02X-0.058719Y0.041096I0.0000011307J0.0624992087
        G03X-0.15384Y-0.15384I-0.112744J-0.041096
        G02X0.041096Y-0.05872I-0.0214041482J-0.0587207882
        G01X0Y-5.1294
        G02X-0.03125Y-0.031249I-0.0312495J0.0000005
        G01X-5.683358Y0
        G02X-0.03125Y0.03125I0J0.03125
        G01X0Y5.1294
        G02X0.041096Y0.058719I0.0624992087J-0.0000011307
        G03X-0.15384Y0.15384I-0.041096J0.112744
        G02X-0.05872Y-0.041096I-0.0587207882J0.0214041482
        G01X-5.1294Y0
        G02X-0.031249Y0.03125I0.0000005J0.0312495
        G01X0Y5.742929
        G02X0.03125Y0.03125I0.03125J0
        """;

    private static (Plate Plate, Part Bottom, Part Top) StackedPair()
    {
        var program = new ProgramReader(new MemoryStream(Encoding.UTF8.GetBytes(PepProgram))).Read();
        var drawing = new Drawing("PT48", program);
        var plate = new Plate(new Size(48, 96)) { PartSpacing = 0.25 };
        plate.EdgeSpacing = new Spacing(0.25, 0.25);

        // The positions the user's nest saved: the top part one spacing above the bottom.
        var bottom = new Part(drawing, new Vector(3, 10.455042000004001));
        bottom.Rotate(System.Math.PI / 2, bottom.Location);
        var top = new Part(drawing, new Vector(3, 27.115126000007997));
        top.Rotate(System.Math.PI / 2, top.Location);
        plate.Parts.Add(bottom);
        plate.Parts.Add(top);
        return (plate, bottom, top);
    }

    [Fact]
    public void TopPartPushesUpAwayFromTheTouchingPartBelow()
    {
        var (plate, bottom, top) = StackedPair();
        Assert.Equal(0.25, top.BoundingBox.Bottom - bottom.BoundingBox.Top, 6);
        var expected = plate.WorkArea().Top - top.BoundingBox.Top;
        Assert.True(expected > 1);

        var moved = Compactor.Push(new List<Part> { top }, plate, PushDirection.Up);

        Assert.Equal(expected, moved, 6);
    }

    [Fact]
    public void BottomPartPushesDownAwayFromTheTouchingPartAbove()
    {
        var (plate, bottom, top) = StackedPair();
        bottom.Offset(0, 5);
        top.Offset(0, 5);
        var expected = bottom.BoundingBox.Bottom - plate.WorkArea().Bottom;

        var moved = Compactor.Push(new List<Part> { bottom }, plate, PushDirection.Down);

        Assert.Equal(expected, moved, 6);
    }

    [Fact]
    public void BottomPartStillStopsAgainstThePartAbove()
    {
        var (plate, bottom, top) = StackedPair();

        Assert.Equal(0, Compactor.Push(new List<Part> { bottom }, plate, PushDirection.Up));
        Assert.Equal(0, Compactor.PushBoundingBox(new List<Part> { bottom }, plate, PushDirection.Up));
    }

    [Fact]
    public void TopPartStillStopsAgainstThePartBelow()
    {
        var (plate, bottom, top) = StackedPair();

        Assert.Equal(0, Compactor.Push(new List<Part> { top }, plate, PushDirection.Down));
    }

    [Theory]
    [InlineData(PushDirection.Right)]
    [InlineData(PushDirection.Left)]
    public void TopPartSlidesAlongTheTouchingPartBelow(PushDirection direction)
    {
        var (plate, bottom, top) = StackedPair();
        bottom.Offset(5, 0);
        top.Offset(5, 0);
        var work = plate.WorkArea();
        var expected = direction == PushDirection.Right
            ? work.Right - top.BoundingBox.Right
            : top.BoundingBox.Left - work.Left;
        Assert.True(expected > 0.1);

        var moved = Compactor.Push(new List<Part> { top }, plate, direction);

        Assert.Equal(expected, moved, 6);
    }
}
