using OpenNest.Engine.NestingEngines.Rectangles;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;
using OpenNest.Engine.Jobs;
namespace OpenNest.Engine.Tests.NestingEngines;
/// <summary>
/// Curved extremes (discs, rings, obrounds) and sloped ones (triangles) must clear the layout check
/// at every spacing: the check circumscribes arcs, so box-touching copies are only valid when the
/// catalog reads their boxes the way the check does. Failed at 16-50 of these 80 jobs before that.
/// </summary>
public class CurvedExtremeSweepTests
{
    [Fact]
    public void CurvedAndSlopedPartsPassTheLayoutCheckAtEverySpacing()
    {
        var bad = new List<string>(); var n = 0;
        foreach (var r in new[] { 0.37, 0.5, 0.731, 1.0, 1.23, 2.0, 3.3, 5.0, 7.77, 12.0 })
        foreach (var sp in new[] { 0.0, 0.1, 0.25, 0.3125 })
        foreach (var rot in new[] { false, true })
        foreach (var ring in new[] { false, true })
        {
            var pol = rot ? RotationPolicy.Automatic : RotationPolicy.Fixed(0);
            var job = Job(new[] { Part("d", ring ? Ring(2 * r, r) : Disc(r), 12, pol), Part("o", Obround(4 * r, 1.3 * r), 6, pol), Part("t", Triangle(3 * r, 2 * r), 4, pol) },
                new[] { Stock("s", 12 * r + 5, 14 * r + 5, spacing: sp) });
            var res = new RectanglesNestingEngine().Solve(job); n++;
            var v = NestLayoutCheck.Violations(job, res);
            if (v.Count > 0) bad.Add($"r={r} sp={sp} rot={rot} ring={ring}: {v[0]}");
        }
        Assert.True(bad.Count == 0, $"{bad.Count}/{n}\n" + string.Join("\n", bad));
    }
}
