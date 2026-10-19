using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>A prepared placement must answer exactly as the pairwise check, whichever side
/// is prepared, so the cutout searches can prepare the frame and the occupants once.</summary>
public class NestLayoutCheckPreparedTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    public void PreparedFrameMatchesPairwiseCheckAcrossInsertPoses(double spacing)
    {
        var program = TestShapes.Rectangle(20, 20);
        program.MoveTo(5, 5);
        program.LineTo(5, 15);
        program.LineTo(15, 15);
        program.LineTo(15, 5);
        program.LineTo(5, 5);
        var frame = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(program));
        var insert = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(TestShapes.Rectangle(4, 3)));
        var framePose = new NestJobPlacement("frame", 0, 0, 0, 0);
        var prepared = NestLayoutCheck.Prepare(frame, framePose, spacing);

        var agreements = 0;
        var clear = 0;
        foreach (var rotation in new[] { 0.0, System.Math.PI / 2, 0.3 })
            for (var x = -6.0; x <= 22; x += 1.37)
                for (var y = -6.0; y <= 22; y += 1.13)
                {
                    var pose = new NestJobPlacement("insert", 0, x, y, rotation);
                    var expected = NestLayoutCheck.Clears(frame, framePose, insert, pose, spacing);
                    Assert.Equal(expected, NestLayoutCheck.Clears(prepared, insert, pose));
                    agreements++;
                    if (expected)
                        clear++;
                }
        Assert.True(clear > 0 && clear < agreements, "the sweep should mix clear and blocked poses");
    }

    [Fact]
    public void PreparedSideMayBeLeftOrRightOfTheOtherPart()
    {
        // The pairwise rule inflates whichever raw outline is leftmost; preparing the part on
        // either side must reproduce that, including exact ties on the left edge.
        var disc = new Program();
        disc.MoveTo(2, 0);
        disc.Codes.Add(new ArcMove(2, 0, 0, 0, RotationType.CW));
        var round = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(disc));
        var box = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(TestShapes.Rectangle(4, 3)));
        var preparedRound = NestLayoutCheck.Prepare(round, new NestJobPlacement("disc", 0, 0, 0, 0), 0.25);
        var preparedBox = NestLayoutCheck.Prepare(box, new NestJobPlacement("box", 0, 0, 0, 0), 0.25);

        foreach (var x in new[] { -8.0, -4.3, -2.0, -1.99, 0.0, 2.0, 2.24, 2.26, 2.3, 4.0, 9.0 })
            foreach (var y in new[] { -5.0, -1.0, 0.0, 1.5, 3.3, 6.0 })
            {
                var boxPose = new NestJobPlacement("box", 0, x, y, 0);
                Assert.Equal(NestLayoutCheck.Clears(round, new("disc", 0, 0, 0, 0), box, boxPose, 0.25),
                    NestLayoutCheck.Clears(preparedRound, box, boxPose));
                var discPose = new NestJobPlacement("disc", 0, x, y, 0);
                Assert.Equal(NestLayoutCheck.Clears(box, new("box", 0, 0, 0, 0), round, discPose, 0.25),
                    NestLayoutCheck.Clears(preparedBox, round, discPose));
            }
    }
}
