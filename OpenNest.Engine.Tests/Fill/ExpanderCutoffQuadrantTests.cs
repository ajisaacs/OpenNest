using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderCutoffQuadrantTests
{
    private static Program Square()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(2, 0);
        program.LineTo(2, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return program;
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
            foreach (var verticalMotion in new[] { false, true })
                foreach (var cutoff in new[] { false, true })
                    foreach (var expand in new[] { false, true })
                        yield return new object[] { quadrant, verticalMotion, cutoff, expand };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RemoteCutoffMustNotPinNegativeParallelMotion(
        int quadrant, bool verticalMotion, bool cutoff, bool expand)
    {
        var plate = new Plate(new Size(80, 80))
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(0, 0),
        };
        var origin = new Vector(
            quadrant == 2 || quadrant == 3 ? -80 : 0,
            quadrant == 3 || quadrant == 4 ? -80 : 0);
        var anchorPose = origin + new Vector(30, 30);
        var moverPose = anchorPose + (verticalMotion ? new Vector(0, -3) : new Vector(-3, 0));
        var anchor = new Part(new Drawing("anchor", Square()), anchorPose);
        var mover = new Part(new Drawing("mover", Square()), moverPose);
        plate.Parts.Add(anchor);
        plate.Parts.Add(mover);

        if (cutoff)
        {
            plate.CutOffs.Add(new CutOff(
                origin + (verticalMotion ? new Vector(40, 0) : new Vector(0, 40)),
                verticalMotion ? CutOffAxis.Vertical : CutOffAxis.Horizontal));
            plate.RegenerateCutOffs(new CutOffSettings());
            Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
        }

        Vector end;
        int violations;
        if (expand)
        {
            var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
                new Expander.Options { MaxSpacing = 3 });
            end = mover.Location;
            violations = result.Violations.Count;
            Assert.True(result.AchievedSpacing >= 2.99,
                $"spacing={result.AchievedSpacing:R}; end=({end.X:R},{end.Y:R})");
        }
        else
        {
            var result = Expander.Separate(new List<Part> { anchor, mover }, plate, 3);
            end = result.Positions[1];
            violations = result.Violations.Count;
            Assert.True(result.Converged,
                $"unchanged=({end.X:R},{end.Y:R}), violations={violations}");
            Assert.Equal(moverPose.X, mover.Location.X);
            Assert.Equal(moverPose.Y, mover.Location.Y);
        }

        // Two units of negative-axis motion opens the selected gap 1 -> 3.
        // The parallel open cutoff remains eight units away throughout.
        Assert.True(verticalMotion ? end.Y <= moverPose.Y - 1.999 : end.X <= moverPose.X - 1.999);
        Assert.Equal(0, violations);
        Assert.Equal(anchorPose.X, anchor.Location.X);
        Assert.Equal(anchorPose.Y, anchor.Location.Y);
        if (cutoff)
        {
            plate.RegenerateCutOffs(new CutOffSettings());
            Assert.Single(plate.CutOffs);
            Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
        }
    }
}
