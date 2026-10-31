using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// Lines that meet a cut-off follow it when it moves, extended or trimmed to keep meeting it.
/// </summary>
public class CutOffJointsTests
{
    private static readonly CutOffSettings Settings = new() { MinSegmentLength = 0.5 };

    private static (Plate Plate, CutOff Vertical, CutOff Horizontal) Junction()
    {
        var plate = new Plate(60, 120);
        var vertical = new CutOff(new Vector(30, 0), CutOffAxis.Vertical);
        var horizontal = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal) { EndLimit = 30 };
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);
        return (plate, vertical, horizontal);
    }

    [Theory]
    [InlineData(34)]
    [InlineData(26)]
    public void MoveTo_ExtendsOrTrimsTheLineToKeepMeeting(double line)
    {
        var (plate, vertical, horizontal) = Junction();

        CutOffTrim.Joints(plate, vertical, Settings).MoveTo(line);

        Assert.Equal(line, horizontal.EndLimit);
        Assert.Null(horizontal.StartLimit);
    }

    [Fact]
    public void Joints_MoveALineStoppedOnAHorizontalToo()
    {
        var plate = new Plate(60, 120);
        var horizontal = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal);
        var vertical = new CutOff(new Vector(30, 0), CutOffAxis.Vertical) { EndLimit = 20 };
        plate.CutOffs.Add(horizontal);
        plate.CutOffs.Add(vertical);

        CutOffTrim.Joints(plate, horizontal, Settings).MoveTo(25);

        Assert.Equal(25, vertical.EndLimit);
    }

    [Fact]
    public void MoveTo_MovesAGapEdgeOnTheLine()
    {
        var (plate, vertical, horizontal) = Junction();
        horizontal.EndLimit = null;
        horizontal.Gaps = new[] { new CutOffGap(30, 50) };

        CutOffTrim.Joints(plate, vertical, Settings).MoveTo(35);

        Assert.Equal(new CutOffGap(35, 50), Assert.Single(horizontal.Gaps));
    }

    [Fact]
    public void MoveTo_LeavesTheOtherEndAlone()
    {
        var (plate, vertical, horizontal) = Junction();
        horizontal.StartLimit = 10;

        CutOffTrim.Joints(plate, vertical, Settings).MoveTo(40);

        Assert.Equal(10, horizontal.StartLimit);
        Assert.Equal(40, horizontal.EndLimit);
    }

    [Fact]
    public void MoveTo_HoldsAMinimumSegmentShortOfTheNextStop_AndComesBack()
    {
        var (plate, vertical, horizontal) = Junction();
        horizontal.StartLimit = 10;
        var joints = CutOffTrim.Joints(plate, vertical, Settings);

        joints.MoveTo(5);
        Assert.Equal(10.5, horizontal.EndLimit);

        joints.MoveTo(25);
        Assert.Equal(25, horizontal.EndLimit);
    }

    [Fact]
    public void MoveTo_StaysOnTheSheet()
    {
        var (plate, vertical, horizontal) = Junction();
        var right = plate.BoundingBox(includeParts: false).Right;

        CutOffTrim.Joints(plate, vertical, Settings).MoveTo(right + 50);

        Assert.Equal(right - 0.5, horizontal.EndLimit);
    }

    [Fact]
    public void Joints_SkipALineThatStopsShort()
    {
        var (plate, vertical, horizontal) = Junction();
        horizontal.EndLimit = 26;

        var joints = CutOffTrim.Joints(plate, vertical, Settings);
        joints.MoveTo(34);

        Assert.Empty(joints.Lines);
        Assert.Equal(26, horizontal.EndLimit);
    }

    [Fact]
    public void Joints_SkipALineItsOwnPiecesDoNotReach()
    {
        var (plate, vertical, horizontal) = Junction();
        vertical.EndLimit = 10;

        Assert.Empty(CutOffTrim.Joints(plate, vertical, Settings).Lines);

        vertical.EndLimit = null;
        vertical.Gaps = new[] { new CutOffGap(15, 25) };

        Assert.Empty(CutOffTrim.Joints(plate, vertical, Settings).Lines);
    }

    [Fact]
    public void Joints_SkipAParallelLine()
    {
        var (plate, vertical, _) = Junction();
        plate.CutOffs.Add(new CutOff(new Vector(10, 0), CutOffAxis.Vertical) { EndLimit = 30 });

        var joints = CutOffTrim.Joints(plate, vertical, Settings);

        Assert.DoesNotContain(joints.Lines, line => line.Axis == CutOffAxis.Vertical);
    }
}
