using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderRemoteObliquePathTests
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
            foreach (var transpose in new[] { false, true })
                foreach (var cutoff in new[] { false, true })
                    foreach (var expand in new[] { false, true })
                        yield return new object[] { quadrant, transpose, cutoff, expand };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RemoteObliqueOpenPathMustNotPinSafeParallelGrowth(
        int quadrant, bool transpose, bool cutoff, bool expand)
    {
        var origin = new Vector(quadrant is 2 or 3 ? -100 : 0,
            quadrant is 3 or 4 ? -100 : 0);
        Vector Swap(double x, double y) => transpose ? new Vector(y, x) : new Vector(x, y);
        var plate = new Plate(new Size(100, 100))
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(0, 0),
        };
        var anchorPose = origin + Swap(40, 40);
        var moverPose = origin + Swap(43, 40);
        var anchor = new Part(new Drawing("anchor", Square()), anchorPose);
        var mover = new Part(new Drawing("mover", Square()), moverPose);
        plate.Parts.Add(anchor);
        plate.Parts.Add(mover);
        if (cutoff)
        {
            var path = new Program();
            var start = Swap(18, 32);
            var middle = Swap(68, 64);
            var high = Swap(68, 80);
            var tail = Swap(75, 80);
            path.MoveTo(start.X, start.Y);
            path.LineTo(middle.X, middle.Y);
            path.LineTo(high.X, high.Y);
            path.LineTo(tail.X, tail.Y);
            plate.Parts.Add(new Part(new Drawing("neutral-path", path) { IsCutOff = true }, origin));
        }
        // First line is y=.64*x+20.48. The anchor's nearest corner (40,42)
        // has exact gap 4.08/sqrt(1+.64^2)>3; mover (43,42) >5.
        Vector end;
        if (expand)
        {
            var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
                new Expander.Options { MaxSpacing = 2 });
            end = mover.Location;
            Assert.True(result.AchievedSpacing >= 1.99,
                $"Remote cutoff pinned mover: spacing={result.AchievedSpacing:R}, violations={result.Violations.Count}");
        }
        else
        {
            var result = Expander.Separate(new List<Part> { anchor, mover }, plate, 2);
            end = result.Positions[1];
            Assert.True(result.Converged,
                $"Remote cutoff pinned mover: end=({end.X:R},{end.Y:R}), violations={result.Violations.Count}");
            Assert.Equal(moverPose.X, mover.Location.X);
            Assert.Equal(moverPose.Y, mover.Location.Y);
        }
        Assert.True(transpose ? end.Y >= moverPose.Y + 0.999 : end.X >= moverPose.X + 0.999);
        Assert.Equal(anchorPose.X, anchor.Location.X);
        Assert.Equal(anchorPose.Y, anchor.Location.Y);
    }
}
