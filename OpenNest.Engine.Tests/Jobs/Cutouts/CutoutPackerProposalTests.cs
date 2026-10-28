using System.Globalization;
using System.Text.RegularExpressions;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>The frontier packer as a cutout proposal: inserts settle against the cutout wall
/// and each other instead of landing at the first sampled point that clears.</summary>
[Collection("Cutout pipeline registry")]
public class CutoutPackerProposalTests
{
    private const double Spacing = 0.3;

    /// <summary>A 13.148 x 1.25 bar with a shallow notch in one long edge and eight bolt holes.</summary>
    private const string BarCode = """
        G91
        G00X0Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X1.875Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X4.625Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X1.875Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X-11.8865Y-1.0625
        G01X0Y0.75
        G02X0.5Y0.5I0.5J0
        G01X3.824Y0
        G02X0.25Y-0.25I0J-0.25
        G01X0Y-0.125
        G03X0.25Y-0.25I0.25J-0
        G01X3.5Y0
        G03X0.25Y0.25I0J0.25
        G01X0Y0.125
        G02X0.25Y0.25I0.25J0
        G01X3.824Y0
        G02X0.5Y-0.5I0J-0.5
        G01X0Y-0.75
        G01X-13.148Y0
        """;

    /// <summary>One 12.25-wide window whose near end carries a notched tab, open to the given
    /// wall height above the tab; the frame below stacks four such windows.</summary>
    private static string Window(double wallHeight) => $"""
        G01X0.125Y0
        G01X0Y0.841458
        G02X0.5Y0.5I0.5J0
        G01X0Y-0.3125
        G01X1.125Y0
        G01X0Y0.3125
        G01X1.25Y0
        G01X0Y-0.3125
        G01X1.125Y0
        G01X0Y0.3125
        G01X4Y0
        G01X0Y-0.3125
        G01X1.125Y0
        G01X0Y0.3125
        G01X1.25Y0
        G01X0Y-0.3125
        G01X1.125Y0
        G01X0Y0.3125
        G02X0.5Y-0.5I0J-0.5
        G01X0Y-0.841458
        G01X0.125Y0
        G01X0Y{wallHeight.ToString(CultureInfo.InvariantCulture)}
        G03X-0.125Y0.125I-0.125J0
        G01X-12Y0
        G03X-0.125Y-0.125I0J-0.125
        G01X0Y-{wallHeight.ToString(CultureInfo.InvariantCulture)}
        """;

    /// <summary>A 14.82 x 69.72 frame with a plain 12.25 x 13.4 window and three tabbed windows
    /// of rising height; only the last is tall enough to stand the bar in.</summary>
    private static readonly string FrameCode = $"""
        G91
        G00X0Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X1.875Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X-3.9375Y2.033896
        G01X12.25Y0
        G01X0Y13.297
        G03X-0.125Y0.125I-0.125J0
        G01X-12Y0
        G03X-0.125Y-0.125I0J-0.125
        G01X0Y-13.297
        G00X8.5625Y-2.033896
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X1.875Y0
        G03X0Y0I-0.125J0
        G00X0.5Y0
        G03X0Y0I-0.125J0
        G00X-11.4375Y16.955896
        {Window(14.375)}
        G00X0Y16
        {Window(14.375)}
        G00X0Y16
        {Window(19.453)}
        G00X-1.283896Y-47.797
        G01X0Y68.25
        G02X0.125Y0.125I0.125J0
        G01X14.567791Y0
        G02X0.125Y-0.125I0J-0.125
        G01X0Y-68.25
        G02X-0.125Y-0.125I-0.125J0
        G01X-0.2615Y0
        G02X-0.125Y0.125I0J0.125
        G01X0Y0.051
        G01X-0.323396Y0
        G01X0Y-0.897396
        G02X-0.5Y-0.5I-0.5J-0
        G01X-12.148Y0
        G02X-0.5Y0.5I0J0.5
        G01X0Y0.897396
        G01X-0.323396Y0
        G01X0Y-0.051
        G02X-0.125Y-0.125I-0.125J0
        G01X-0.2615Y0
        G02X-0.125Y0.125I0J0.125
        """;

    /// <summary>Height of the tab on a tabbed window's near end.</summary>
    private const double TabHeight = 0.841458 + 0.5;

    private static Program Bar() => Incremental(BarCode);
    private static Program Frame() => Incremental(FrameCode);

    [Fact]
    public void FrameHasFourWindowsAndTheBarIsSolid()
    {
        var frame = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(Frame()));
        var bar = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(Bar()));
        Assert.Equal(4, frame.Cutouts.Count(c => System.Math.Abs(c.Area()) > 100));
        Assert.Equal(14.818, frame.Bounds.Length, 3);
        Assert.Equal(69.721, frame.Bounds.Width, 3);
        Assert.Equal(13.148, bar.Bounds.Length, 3);
        Assert.Equal(1.25, bar.Bounds.Width, 3);
    }

    [Fact]
    public void TwoBarsStackAgainstAWindowWallInsteadOfScatteringAcrossIt()
    {
        var frame = JobBuilder.Part("PT10", Frame(), 1);
        var bar = JobBuilder.Part("PT11", Bar(), 2);
        var geometry = JobPartGeometry.Read(frame.Geometry);
        var seated = new List<(int Hole, IReadOnlyList<NestJobPlacement> Poses)>();
        for (var hole = 0; hole < geometry.Cutouts.Count; hole++)
        {
            if (System.Math.Abs(geometry.Cutouts[hole].Area()) < 100)
                continue;
            var poses = CutoutRouter.Fill(frame, hole, new[] { bar }, Spacing);
            if (poses.Count == 2)
                seated.Add((hole, poses));
        }
        var (window, stacked) = Assert.Single(seated);
        AssertClear(frame, bar, stacked, Spacing);
        AssertStacked(geometry.Cutouts[window], JobPartGeometry.Read(bar.Geometry), stacked);
    }

    [Fact]
    public void PackerAloneSeatsBothBarsAgainstTheWall()
    {
        var frame = JobBuilder.Part("PT10", Frame(), 1);
        var bar = JobBuilder.Part("PT11", Bar(), 2);
        var geometry = JobPartGeometry.Read(frame.Geometry);
        var tallest = Enumerable.Range(0, geometry.Cutouts.Count)
            .OrderByDescending(i => System.Math.Abs(geometry.Cutouts[i].Area())).First();
        var poses = CutoutPackerProposal.Fill(frame, tallest, new[] { bar }, Spacing);
        Assert.Equal(2, poses.Count);
        Assert.Equal(new[] { 0, 1 }, poses.Select(p => p.InstanceIndex));
        AssertClear(frame, bar, poses, Spacing);
        AssertStacked(geometry.Cutouts[tallest], JobPartGeometry.Read(bar.Geometry), poses);
    }

    [Fact]
    public void PackerKeepsClearOfInsertsAlreadySeated()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Rectangle("insert", 3, 2, 6);
        var geometry = JobPartGeometry.Read(insert.Geometry);
        var occupant = new NestJobPlacement("occupant", 0, -1.5, -1, 0);
        var poses = CutoutPackerProposal.Fill(frame, 0, new[] { insert }, 0.25,
            new[] { (geometry, occupant) }, null, default);
        Assert.NotEmpty(poses);
        foreach (var pose in poses)
            Assert.True(NestLayoutCheck.Clears(geometry, occupant, geometry, pose, 0.25), $"{pose} meets the occupant");
        AssertClear(frame, insert, poses, 0.25);
    }

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void AutoNestSeatsBothBarsStackedInOneWindow(string engine)
    {
        var frameDrawing = new Drawing("PT10", Frame());
        var barDrawing = new Drawing("PT11", Bar());
        var request = new NestPipelineRequest(engine,
            new[] { new NestItem { Drawing = barDrawing, Quantity = 2 }, new NestItem { Drawing = frameDrawing, Quantity = 2 } },
            new[] { new NestPlateStock("sheet", new Size(60, 120), 1, Spacing) },
            new NestJobOptions(maxPlates: 1));
        var result = NestPipeline.Run(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var plate = Assert.Single(result.Plates);
        var bars = plate.Parts.Where(p => ReferenceEquals(p.BaseDrawing, barDrawing)).ToArray();
        var frames = plate.Parts.Where(p => ReferenceEquals(p.BaseDrawing, frameDrawing)).ToArray();
        Assert.Equal(2, bars.Length);
        Assert.Equal(2, frames.Length);
        var frameGeometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(Frame()));
        var barGeometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(Bar()));
        // Both bars lie in one window of one frame; express them in that frame's coordinates.
        var seated = new List<(Part Frame, Shape Window, IReadOnlyList<NestJobPlacement> Poses)>();
        foreach (var host in frames)
        {
            var local = bars.Select(bar => Local(host, bar)).ToArray();
            foreach (var window in frameGeometry.Cutouts.Where(c => System.Math.Abs(c.Area()) > 100))
                if (local.All(pose => Inside(window, Extent(barGeometry, pose))))
                    seated.Add((host, window, local));
        }
        var (_, hole, poses) = Assert.Single(seated);
        AssertStacked(hole, barGeometry, poses);
    }

    private static NestJobPlacement Local(Part frame, Part part)
    {
        var dx = part.Location.X - frame.Location.X;
        var dy = part.Location.Y - frame.Location.Y;
        var cosine = System.Math.Cos(-frame.Rotation);
        var sine = System.Math.Sin(-frame.Rotation);
        return new NestJobPlacement("PT11", 0, dx * cosine - dy * sine, dx * sine + dy * cosine,
            part.Rotation - frame.Rotation);
    }

    private static bool Inside(Shape window, Box box)
    {
        var bounds = window.BoundingBox;
        return box.Left > bounds.Left && box.Right < bounds.Right && box.Bottom > bounds.Bottom && box.Top < bounds.Top;
    }

    /// <summary>Both bars stand in the window against one wall and against each other, not in
    /// the middle: each touches the wall or the other bar across the window, and the near tab
    /// or the far wall along it, within the spacing plus a small tolerance.</summary>
    private static void AssertStacked(Shape window, JobPartGeometry bar, IReadOnlyList<NestJobPlacement> poses)
    {
        const double slack = 0.05;
        var hole = window.BoundingBox;
        var boxes = poses.Select(p => Extent(bar, p)).ToArray();
        Assert.Equal(2, boxes.Length);
        foreach (var box in boxes)
        {
            Assert.True(Inside(window, box), $"{box} is not inside the window {hole}");
            var other = boxes.Single(b => !ReferenceEquals(b, box));
            var toWall = System.Math.Min(box.Left - hole.Left, hole.Right - box.Right);
            var toOther = System.Math.Max(other.Left - box.Right, box.Left - other.Right);
            Assert.True(toWall <= Spacing + slack || toOther <= Spacing + slack,
                $"{box} floats {toWall:F3} from the walls and {toOther:F3} from the other bar");
            var toEnd = System.Math.Min(box.Bottom - hole.Bottom - TabHeight, hole.Top - box.Top);
            Assert.True(toEnd <= Spacing + slack, $"{box} floats {toEnd:F3} from the window ends");
        }
        var gap = System.Math.Max(boxes[1].Left - boxes[0].Right, boxes[0].Left - boxes[1].Right);
        Assert.InRange(gap, Spacing - 1e-6, Spacing + slack);
        var center = (boxes.Min(b => b.Left) + boxes.Max(b => b.Right)) / 2;
        Assert.True(System.Math.Abs(center - (hole.Left + hole.Right) / 2) > 2,
            "the pair sits in the middle of the window");
    }

    private static Box Extent(JobPartGeometry geometry, NestJobPlacement pose)
    {
        var shape = (Shape)geometry.Perimeter.Clone();
        shape.Rotate(pose.Rotation);
        var box = shape.BoundingBox;
        return new Box(box.Left + pose.X, box.Bottom + pose.Y, box.Length, box.Width);
    }

    private static void AssertClear(NestJobPart frame, NestJobPart insert, IReadOnlyList<NestJobPlacement> poses,
        double spacing)
    {
        var frameGeometry = JobPartGeometry.Read(frame.Geometry);
        var geometry = JobPartGeometry.Read(insert.Geometry);
        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        foreach (var pose in poses)
            Assert.True(NestLayoutCheck.Clears(frameGeometry, framePose, geometry, pose, spacing), $"{pose} meets the frame");
        for (var i = 0; i < poses.Count; i++)
            for (var j = i + 1; j < poses.Count; j++)
                Assert.True(NestLayoutCheck.Clears(geometry, poses[i], geometry, poses[j], spacing),
                    $"{poses[i]} meets {poses[j]}");
    }

    /// <summary>Incremental (G91) G-code: rapids, lines and arcs with centers relative to the start.</summary>
    private static Program Incremental(string code)
    {
        var program = new Program();
        double x = 0, y = 0;
        foreach (var line in code.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = Regex.Matches(line, @"([A-Z])(-?\d*\.?\d+)")
                .ToDictionary(m => m.Groups[1].Value[0], m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            var g = (int)words['G'];
            if (g == 91)
                continue;
            var end = new Vector(x + words.GetValueOrDefault('X'), y + words.GetValueOrDefault('Y'));
            var center = new Vector(x + words.GetValueOrDefault('I'), y + words.GetValueOrDefault('J'));
            program.Codes.Add(g switch
            {
                0 => new RapidMove(end.X, end.Y),
                1 => new LinearMove(end.X, end.Y),
                2 => new ArcMove(end, center, RotationType.CW),
                _ => new ArcMove(end, center, RotationType.CCW),
            });
            (x, y) = (end.X, end.Y);
        }
        return program;
    }
}
