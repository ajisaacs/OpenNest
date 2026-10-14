using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderIntersectingOpenPathTests
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
                foreach (var expand in new[] { false, true })
                    yield return new object[] { quadrant, transpose, expand };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void IntersectingOpenPathMustRemainAViolation(int quadrant, bool transpose, bool expand)
    {
        var origin = new Vector(quadrant is 2 or 3 ? -100 : 0,
            quadrant is 3 or 4 ? -100 : 0);
        Vector Swap(double x, double y) => transpose ? new Vector(y, x) : new Vector(x, y);
        var plate = new Plate(new Size(100, 100))
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(0, 0),
        };
        var expectedA = origin + Swap(40, 40);
        var expectedB = origin + Swap(43, 40);
        var anchor = new Part(new Drawing("anchor", Square()), expectedA);
        var mover = new Part(new Drawing("mover", Square()), expectedB);
        plate.Parts.Add(anchor);
        plate.Parts.Add(mover);

        var path = new Program();
        var start = Swap(20, 40);
        var across = Swap(70, 42);
        var high = Swap(70, 60);
        var tail = Swap(75, 60);
        path.MoveTo(start.X, start.Y);
        path.LineTo(across.X, across.Y);
        path.LineTo(high.X, high.Y);
        path.LineTo(tail.X, tail.Y);
        var cutoff = new Part(new Drawing("neutral-open-cut", path) { IsCutOff = true }, origin);
        plate.Parts.Add(cutoff);

        // First segment is y=40+0.04*(x-20). At x=41 and x=44,
        // y=40.84 and 40.96 are strictly INSIDE both squares. Signed origins
        // and transposition preserve these independent interior witnesses.
        List<Expander.Violation> violations;
        if (expand)
        {
            var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
                new Expander.Options { MaxSpacing = 0.1 });
            violations = result.Violations;
            Assert.Equal(0, result.AchievedSpacing);
        }
        else
        {
            var result = Expander.Separate(new List<Part> { anchor, mover }, plate, 0);
            violations = result.Violations;
            Assert.False(result.Converged,
                $"Native path intersects both squares but reports converged with {violations.Count} violations");
        }
        Assert.Contains(violations, v => ReferenceEquals(v.B, cutoff) && v.Achieved < 0);
        Assert.Equal(expectedA.X, anchor.Location.X);
        Assert.Equal(expectedA.Y, anchor.Location.Y);
        Assert.Equal(expectedB.X, mover.Location.X);
        Assert.Equal(expectedB.Y, mover.Location.Y);
    }
}
