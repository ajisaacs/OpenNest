using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// C16 (PM 6859db53): a clamp zone the job validator accepts may be far smaller than the
/// validation outline tolerance (here 1e-5 square). Every keep-out consumer must check it
/// without throwing, flag a placement whose material overlaps it (plus the D6 clearance),
/// and keep a placement that only touches or clears it clear. Tolerances are unchanged (C5):
/// the expected verdicts below are exact rectangle arithmetic, not the checker's output.
/// </summary>
public class TinyClampZoneTests
{
    private const double Tiny = 1e-5;

    private static readonly ClampZone TinyZone = new("tiny", new Box(8, 0, Tiny, Tiny));

    private static Program SquareProgram()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(2, 0));
        program.Codes.Add(new LinearMove(2, 2));
        program.Codes.Add(new LinearMove(0, 2));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    private static NestJobPart Square() =>
        new("square", PartGeometrySnapshot.FromProgram(SquareProgram()), 1, 0, RotationPolicy.Fixed(0));

    private static NestPlateStock Stock(double bottom) =>
        new("sheet", new Size(10, 12), null, 0, new Spacing(0, bottom, 0, 0), 1,
            new[] { TinyZone });

    private static NestJob Job(double bottom) => new(new[] { Square() }, new[] { Stock(bottom) });

    private static IReadOnlyList<string> ClampViolations(NestJob job, NestJobResult result) =>
        NestLayoutCheck.Violations(job, result).Where(v => v.Contains("clamp zone")).ToList();

    [Fact]
    public void RectanglesResultOnATinyClampIsCheckedWithoutThrowing()
    {
        // The PM probe: Rectangles places the square flush against the tiny clamp.
        var job = Job(0);
        NestJobValidator.Validate(job);
        var result = new RectanglesNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);

        var violations = ClampViolations(job, result);

        // Independent oracle: the square's footprint shares no positive area with the zone.
        var pose = Assert.Single(result.Plates.SelectMany(p => p.Placements));
        var overlapX = System.Math.Min(pose.X + 2, 8 + Tiny) - System.Math.Max(pose.X, 8);
        var overlapY = System.Math.Min(pose.Y + 2, Tiny) - System.Math.Max(pose.Y, 0);
        var expectOverlap = overlapX > 0 && overlapY > 0;
        Assert.Equal(expectOverlap, violations.Count > 0);
    }

    [Fact]
    public void DefaultSolvesATinyClampJobToAValidResult()
    {
        var job = Job(0);
        var result = new DefaultNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Empty(NestLayoutCheck.Violations(job, result));
    }

    // (bottom edge spacing, x, y, material overlaps the zone inflated by the clearance).
    // The keep-out is [8 - c, 8 + 1e-5 + c] x [-c, 1e-5 + c] with c = bottom - 0.0005
    // (the shared spacing slack); the 2x2 square spans [x, x+2] x [y, y+2].
    [Theory]
    [InlineData(0.0, 7.5, 0.0, true)]       // covers the zone
    [InlineData(0.0, 6.0, 0.0, false)]      // right edge flush on the zone's left side
    [InlineData(0.0, 8.00001, 0.0, false)]  // left edge flush on the zone's right side
    [InlineData(0.0, 8.0, Tiny, false)]     // bottom edge flush on the zone's top
    [InlineData(0.0, 3.0, 0.0, false)]      // well clear
    [InlineData(0.25, 7.5, 0.1, true)]      // over the zone, inside the clearance band
    [InlineData(0.25, 8.0, 0.24, true)]     // above the zone but inside the clearance
    [InlineData(0.25, 8.25001, 0.1, false)] // exactly at the clearance to the right
    [InlineData(0.25, 7.0, 0.25001, false)] // exactly at the clearance above
    [InlineData(0.25, 3.0, 0.25, false)]    // well clear
    public void LayoutCheckJudgesPlacementsAgainstATinyClamp(double bottom, double x, double y,
        bool overlaps)
    {
        var job = Job(bottom);
        NestJobValidator.Validate(job);
        var stock = job.Plates[0];
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("square", 0, x, y, 0) }) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());

        var violations = ClampViolations(job, result);

        Assert.Equal(overlaps, violations.Count > 0);
    }

    private static Program FrameProgram()
    {
        // 4x4 square with a 2x2 hole in its middle.
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        program.Codes.Add(new RapidMove(1, 1));
        program.Codes.Add(new LinearMove(1, 3));
        program.Codes.Add(new LinearMove(3, 3));
        program.Codes.Add(new LinearMove(3, 1));
        program.Codes.Add(new LinearMove(1, 1));
        return program;
    }

    // The tiny zone inside the frame's hole is clear; inside its material it is not.
    [Theory]
    [InlineData(6.0, -2.0, false)] // hole spans [7, 9] x [-1, 1] around the zone
    [InlineData(6.0, 0.0, true)]   // the frame's bottom bar covers the zone
    public void LayoutCheckSeesHolesAroundATinyClamp(double x, double y, bool overlaps)
    {
        var frame = new NestJobPart("frame", PartGeometrySnapshot.FromProgram(FrameProgram()), 1, 0,
            RotationPolicy.Fixed(0));
        var job = new NestJob(new[] { frame }, new[] { Stock(0) });
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, job.Plates[0], new[] { new NestJobPlacement("frame", 0, x, y, 0) }) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());

        Assert.Equal(overlaps, ClampViolations(job, result).Count > 0);
    }

    [Theory]
    [InlineData(7.5, 0.0, true)]
    [InlineData(6.0, 0.0, false)]
    [InlineData(8.00001, 0.0, false)]
    [InlineData(3.0, 0.0, false)]
    public void InteractiveKeepOutTestJudgesATinyClamp(double x, double y, bool overlaps)
    {
        var drawing = new Drawing("square", SquareProgram());
        var part = new Part(drawing, new Vector(x, y));
        Assert.Equal(overlaps, InteractiveClamps.MaterialInKeepOut(part, new Box(8, 0, Tiny, Tiny)));
    }
    private static Plate TinyZonePlate()
    {
        var plate = new Plate(new Size(10, 12)); // zero edge and part spacing
        plate.ClampZones.Add(TinyZone);
        return plate;
    }

    // Positive-area overlap with the zone, admitting 1e-9 of floating closure (a ten
    // thousandth of the zone): a push that stops flush on the zone is clear.
    private static bool EntersTinyZone(Part part) =>
        System.Math.Min(part.BoundingBox.Right, 8 + Tiny) - System.Math.Max(part.BoundingBox.Left, 8) > 1e-9
        && System.Math.Min(part.BoundingBox.Top, Tiny) - System.Math.Max(part.BoundingBox.Bottom, 0) > 1e-9;

    [Theory]
    [InlineData("Fill")]
    [InlineData("Strip")]
    [InlineData("Vertical Remnant")]
    [InlineData("Horizontal Remnant")]
    public void InteractiveFillNeverCoversATinyClamp(string strategy)
    {
        var plate = TinyZonePlate();
        var item = new NestItem { Drawing = new Drawing("square", SquareProgram()), Quantity = 30 };

        var parts = PlateFillService.FillItem(strategy, plate, item, plate.WorkArea(), null,
            System.Threading.CancellationToken.None);

        Assert.NotEmpty(parts);
        Assert.DoesNotContain(parts, EntersTinyZone);
    }

    [Fact]
    public void PushDownStopsBeforeCoveringATinyClamp()
    {
        var plate = TinyZonePlate();
        var mover = new Part(new Drawing("square", SquareProgram()), new Vector(7.5, 5));
        plate.Parts.Add(mover);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        Assert.False(EntersTinyZone(mover), $"pushed to ({mover.Location.X:R},{mover.Location.Y:R})");
    }
}
