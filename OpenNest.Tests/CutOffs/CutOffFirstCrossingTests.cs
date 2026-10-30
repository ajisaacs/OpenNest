using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A new Sheet Cut-Off line runs from its start edge, set by the cut direction, and stops at the
/// first crossing line it meets, unless that would leave it too short.
/// </summary>
public class CutOffFirstCrossingTests
{
    private static Plate PlateWithHorizontals(int quadrant, params double[] ys)
    {
        var plate = new Plate(48, 96) { Quadrant = quadrant };
        foreach (var y in ys)
            plate.CutOffs.Add(new CutOff(new Vector(0, y), CutOffAxis.Horizontal));
        return plate;
    }

    private static CutOffEdit Stop(Plate plate, CutOff line, CutDirection direction = CutDirection.AwayFromOrigin)
    {
        var edit = CutOffTrim.StopAtFirstCrossing(plate, line, new CutOffSettings { CutDirection = direction });
        Assert.True(edit.Succeeded, edit.Refusal);
        edit.Apply();
        return edit;
    }

    [Fact]
    public void AwayFromOrigin_StopsAtTheCrossingNearestTheOrigin()
    {
        var plate = PlateWithHorizontals(1, 30, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Stop(plate, line);

        Assert.Null(line.StartLimit);
        Assert.Equal(10, line.EndLimit);
    }

    [Fact]
    public void TowardOrigin_StopsAtTheCrossingNearestTheFarEdge()
    {
        var plate = PlateWithHorizontals(1, 30, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Stop(plate, line, CutDirection.TowardOrigin);

        Assert.Equal(30, line.StartLimit);
        Assert.Null(line.EndLimit);
    }

    [Fact]
    public void AwayFromOrigin_StartsAtTheOriginEdgeInAnyQuadrant()
    {
        var plate = PlateWithHorizontals(3, -30, -10);
        var line = new CutOff(new Vector(-40, -25), CutOffAxis.Vertical);

        Stop(plate, line);

        Assert.Equal(-10, line.StartLimit);
        Assert.Null(line.EndLimit);
    }

    [Fact]
    public void HorizontalLineStopsAtTheFirstVertical()
    {
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        var line = new CutOff(new Vector(70, 12), CutOffAxis.Horizontal);

        Stop(plate, line);

        Assert.Null(line.StartLimit);
        Assert.Equal(20, line.EndLimit);
    }

    [Fact]
    public void StaysFullLengthWhenNothingCrossesIt()
    {
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new Vector(10, 0), CutOffAxis.Vertical));
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Assert.Empty(Stop(plate, line).Changes);
        Assert.Null(line.StartLimit);
        Assert.Null(line.EndLimit);
    }

    [Fact]
    public void ALineStoppedShortOfItDoesNotCross()
    {
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new Vector(0, 10), CutOffAxis.Horizontal) { EndLimit = 30 });
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Assert.Empty(Stop(plate, line).Changes);
    }

    [Fact]
    public void StaysFullLengthWhenTheLineLeftWouldBeTooShort()
    {
        var plate = PlateWithHorizontals(1, 0.03, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Assert.Empty(Stop(plate, line).Changes);
        Assert.Null(line.EndLimit);
    }

    [Fact]
    public void StoppingShortOfTheFarEdgeSaysTheOvercutIsOff()
    {
        var plate = PlateWithHorizontals(1, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical) { HeadLockOvercut = 1.5 };

        Assert.Equal(CutOffTrim.OvercutOff, Stop(plate, line).Notice);
    }

    [Fact]
    public void ReachingTheFarEdgeKeepsTheOvercut()
    {
        var plate = PlateWithHorizontals(1, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical) { HeadLockOvercut = 1.5 };

        Assert.Null(Stop(plate, line, CutDirection.TowardOrigin).Notice);
    }

    [Fact]
    public void TheRegeneratedLineEndsOnTheCrossing()
    {
        var plate = PlateWithHorizontals(1, 10);
        var line = new CutOff(new Vector(40, 25), CutOffAxis.Vertical);

        Stop(plate, line);
        plate.CutOffs.Add(line);
        plate.RegenerateCutOffs(new CutOffSettings());

        var moves = line.Drawing.Program.Codes.OfType<LinearMove>().ToList();
        Assert.Equal(10, moves.Max(move => move.EndPoint.Y), 6);
    }
}
