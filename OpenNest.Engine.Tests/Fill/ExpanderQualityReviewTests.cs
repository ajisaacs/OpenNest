using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderQualityReviewTests
{
    private static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, height);
        program.LineTo(0, height);
        program.LineTo(0, 0);
        return program;
    }

    private static Part AddSquare(Plate plate, double x, double y, double size)
    {
        var part = new Part(new Drawing("neutral", Rectangle(size, size)), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Plate MakePlate(double length) =>
        new(new Size(12, length)) { EdgeSpacing = new Spacing(0, 0) };

    [Fact]
    public void TinyZoneContainedInMover_MustNotReturnClearPositiveSpacing()
    {
        var plate = MakePlate(30);
        plate.ClampZones.Add(new ClampZone("tiny", new Box(5, 5, 1e-5, 1e-5)));
        var anchor = AddSquare(plate, 1, 4, 2);
        var mover = AddSquare(plate, 4, 4, 2);
        // Independent oracle: [5,5.00001]^2 lies strictly inside [4,6]^2.
        var result = Expander.Expand(
            new List<Part> { anchor, mover },
            plate,
            new Expander.Options { InitialStep = 3, MaxSpacing = 3 }
        );
        Assert.True(
            result.Violations.Count > 0,
            $"Physical clamp is still inside mover but report is clear: spacing={result.AchievedSpacing:R}, violations={result.Violations.Count}"
        );
        Assert.True(result.Violations[0].Achieved < 0);
    }

    [Fact]
    public void TinyZoneInsideCutoutIsNotReportedAsMaterialOverlap()
    {
        var plate = MakePlate(30);
        plate.ClampZones.Add(new ClampZone("tiny", new Box(5, 5, 1e-5, 1e-5)));
        var frameProgram = Rectangle(10, 10);
        frameProgram.MoveTo(4, 4);
        frameProgram.LineTo(6, 4);
        frameProgram.LineTo(6, 6);
        frameProgram.LineTo(4, 6);
        frameProgram.LineTo(4, 4);
        var frame = new Part(new Drawing("neutral-frame", frameProgram), Vector.Zero);
        plate.Parts.Add(frame);

        var result = Expander.Separate(new List<Part> { frame }, plate, spacing: 0.5);
        Assert.True(result.Converged, $"zone inside cutout reported as material: {result.Violations.Count}");
        Assert.Empty(result.Violations);
        Assert.Equal(0, frame.Location.X);
    }

    [Fact]
    public void EdgeLimitedConvexOverlap_ShouldRetainCertifiedPartialImprovement()
    {
        var plate = MakePlate(11.5);
        var anchor = AddSquare(plate, 5, 4, 4);
        var mover = AddSquare(plate, 7, 4, 4);
        var result = Expander.Expand(
            new List<Part> { anchor, mover }, plate, new Expander.Options { MaxSpacing = 1 }
        );
        // Along x in [7,7.5], penetration is 9-x and strictly decreases.
        Assert.True(
            mover.Location.X >= 7.49,
            $"Lost safe partial-overlap improvement: x={mover.Location.X:R}; expected edge at 7.5"
        );
        Assert.NotEmpty(result.Violations);
        Assert.True(mover.Location.X + 4 <= 11.5 + 1e-9);
    }

    [Fact]
    public void NegativeTarget_ConvexOverlapCanReachLessNegativeClearance()
    {
        var plate = MakePlate(30);
        var anchor = AddSquare(plate, 5, 4, 4);
        var mover = AddSquare(plate, 7, 4, 4);
        var result = Expander.Separate(new List<Part> { anchor, mover }, plate, -1);
        Assert.True(result.Converged, $"Signed target -1 was not reached; x={result.Positions[1].X:R}");
        Assert.True(result.Positions[1].X >= 8);
        Assert.Equal(7, mover.Location.X);
    }
}
