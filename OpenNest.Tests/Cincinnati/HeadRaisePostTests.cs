using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Both Cincinnati posts raise the head before a rapid Plan Cutting marked to pass over parts
/// already cut, whatever the operator's head-raise settings; the CI Fiber post refuses the nest
/// while its head-raise block is not set.
/// </summary>
public class HeadRaisePostTests
{
    private const string RaiseCode = "M210 (RAISE HEAD)";

    [Theory]
    [InlineData(M47Mode.None)]
    [InlineData(M47Mode.BlockDelete)]
    [InlineData(M47Mode.Auto)]
    public void ClPost_RaisesBeforeAMarkedRapid_WhateverTheM47Mode(M47Mode mode)
    {
        var lines = ClLines(Nest(markSecond: true), mode);

        var second = lines.FindIndex(l => l.Contains("G0 X10.75 Y1"));
        Assert.True(second > 0, string.Join("\n", lines));
        Assert.Equal("M47", lines[second - 1]);
    }

    [Fact]
    public void ClPost_WithoutAMarker_FollowsTheM47Mode()
    {
        var lines = ClLines(Nest(markSecond: false), M47Mode.None);

        var second = lines.FindIndex(l => l.Contains("G0 X10.75 Y1"));
        Assert.True(second > 0, string.Join("\n", lines));
        Assert.DoesNotContain("M47", lines[second - 1]);
    }

    [Fact]
    public void CiFiberPost_WritesTheRaiseBeforeOnlyTheMarkedRapid()
    {
        var post = new CIFiberPostProcessor(new CIFiberPostConfig { HeadRaiseCode = RaiseCode });
        using var stream = new MemoryStream();
        post.Post(Nest(markSecond: true), stream);
        var lines = Lines(stream);

        var raise = Assert.Single(lines, l => l == RaiseCode);
        Assert.Equal("G0X10.75Y1", lines[lines.IndexOf(raise) + 1]);
    }

    [Fact]
    public void CiFiberPost_WithoutARaiseCode_RefusesAMarkedNest()
    {
        var post = new CIFiberPostProcessor(new CIFiberPostConfig());

        var refusal = Assert.Throws<InvalidOperationException>(() => post.Post(Nest(markSecond: true), new MemoryStream()));

        Assert.Contains("head-raise code", refusal.Message);
        post.Post(Nest(markSecond: false), new MemoryStream()); // Nothing marked: nothing to refuse.
    }

    // Two squares side by side, each with a line lead-in on its left; the second one's rapid marked.
    private static Nest Nest(bool markSecond)
    {
        var nest = new Nest("RAISE");
        var plate = new Plate(60, 120);
        nest.Plates.Add(plate);
        foreach (var x in new[] { 1.0, 11.0 })
        {
            var part = new Part(new Drawing("square", Square()), new Vector(x, 0));
            var program = LeadIn();
            if (markSecond && x > 1)
                program = HeadRaise.Mark(program, [0]);
            Assert.True(part.RestoreLeadInProgram(program, true));
            plate.Parts.Add(part);
        }
        return nest;
    }

    private static Program Square()
    {
        var p = new Program();
        p.MoveTo(0, 0); p.LineTo(0, 2); p.LineTo(2, 2); p.LineTo(2, 0); p.LineTo(0, 0);
        return p;
    }

    private static Program LeadIn()
    {
        var p = new Program();
        p.MoveTo(-0.25, 1);
        p.Codes.Add(new LinearMove(0, 1) { Layer = LayerType.Leadin });
        p.LineTo(0, 2); p.LineTo(2, 2); p.LineTo(2, 0); p.LineTo(0, 0); p.LineTo(0, 1);
        return p;
    }

    private static List<string> ClLines(Nest nest, M47Mode mode)
    {
        var post = new CincinnatiPostProcessor(new CincinnatiPostConfig { ExteriorM47 = mode, InteriorM47 = mode });
        using var stream = new MemoryStream();
        post.Post(nest, stream);
        return Lines(stream);
    }

    private static List<string> Lines(MemoryStream stream) =>
        Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
}
