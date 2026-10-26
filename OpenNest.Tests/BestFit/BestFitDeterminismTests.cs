using System.Globalization;
using OpenNest.CNC;
using OpenNest.Engine.BestFit;

namespace OpenNest.Tests.BestFit;

/// <summary>
/// The best-fit pair search evaluates strategies and candidates in parallel; equal-area
/// candidates must still come out in the same order on every run, since fills try them in
/// that order and keep the first of equal layouts.
/// </summary>
public class BestFitDeterminismTests
{
    private const int Runs = 6;

    /// <summary>6 x 3 parallelogram leaning 2 to the right.</summary>
    private static Drawing Parallelogram()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(8, 3);
        program.LineTo(2, 3);
        program.LineTo(0, 0);
        return new Drawing("parallelogram", program);
    }

    /// <summary>6 x 4 chevron.</summary>
    private static Drawing Chevron()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 2);
        program.LineTo(6, 0);
        program.LineTo(6, 2);
        program.LineTo(3, 4);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return new Drawing("chevron", program);
    }

    /// <summary>6 x 5 tee: a 6 x 1.5 bar on a 2-wide stem.</summary>
    private static Drawing Tee()
    {
        var program = new Program();
        program.MoveTo(2, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 3.5);
        program.LineTo(6, 3.5);
        program.LineTo(6, 5);
        program.LineTo(0, 5);
        program.LineTo(0, 3.5);
        program.LineTo(2, 3.5);
        program.LineTo(2, 0);
        return new Drawing("tee", program);
    }

    public static TheoryData<string> Shapes => new() { "parallelogram", "chevron", "tee" };

    private static Drawing Shape(string name) =>
        name switch
        {
            "parallelogram" => Parallelogram(),
            "chevron" => Chevron(),
            _ => Tee(),
        };

    private static string Describe(BestFitResult result)
    {
        var c = result.Candidate;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:R},{1:R},{2:R}|{3:R},{4:R},{5}",
            c.Part2Rotation,
            c.Part2Offset.X,
            c.Part2Offset.Y,
            result.RotatedArea,
            result.OptimalRotation,
            result.Keep
        );
    }

    private static void AssertSameEveryRun(Func<List<BestFitResult>> search)
    {
        var first = search().Select(Describe).ToList();

        for (var run = 1; run < Runs; run++)
        {
            var next = search().Select(Describe).ToList();
            Assert.Equal(first.Count, next.Count);

            var diverged = first.Zip(next).Select((p, i) => (p, i)).FirstOrDefault(x => x.p.First != x.p.Second);
            Assert.True(
                diverged.p.First == null,
                $"run {run} diverged at index {diverged.i}: {diverged.p.First} vs {diverged.p.Second}"
            );
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void FindCandidates_SameOrderEveryRun(string shape)
    {
        var drawing = Shape(shape);
        AssertSameEveryRun(() => new BestFitFinder(48, 28).FindCandidates(drawing, 0.5));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void FindBestFits_SameOrderEveryRun(string shape)
    {
        var drawing = Shape(shape);
        AssertSameEveryRun(() => new BestFitFinder(48, 28).FindBestFits(drawing, 0.5));
    }
}
