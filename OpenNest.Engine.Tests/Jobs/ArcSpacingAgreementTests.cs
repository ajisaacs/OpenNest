using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The layout check and the runner's candidate validation must judge part spacing alike. Two
/// D-shaped parts face each other arc to arc, as Fill interlocks them: their spacing outlines
/// circumscribe the arcs and overlap even when the native arcs are exactly at the spacing.
/// </summary>
public class ArcSpacingAgreementTests
{
    private const double Spacing = 0.5;
    private const double Radius = 2.5;

    /// <summary>3 x 5 block closed by a semicircular arc centered at (3, 2.5).</summary>
    private static Program DShape()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 0);
        program.ArcTo(3, 5, 3, Radius, RotationType.CCW);
        program.LineTo(0, 5);
        program.LineTo(0, 0);
        return program;
    }

    private static NestJob Job() =>
        new(
            new[] { new NestJobPart("arc", PartGeometrySnapshot.FromProgram(DShape()), 2) },
            new[] { new NestPlateStock("stock", new Size(30, 50), 1, Spacing, new Spacing(1, 1, 1, 1)) },
            new NestJobOptions("Fill")
        );

    /// <summary>
    /// The pair Fill placed in the irregular-job pins: arcs at 95 and 275 degrees, centers on a
    /// line inside both arcs' spans, so the native gap is the center distance less both radii.
    /// </summary>
    private static NestJobPlacement[] FacingArcs(double gap)
    {
        var first = new Vector(20, 10);
        var direction = new Vector(0.566512327859782, 5.470746181498729);
        var length = System.Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var distance = 2 * Radius + gap;
        var second = new Vector(first.X + direction.X / length * distance,
            first.Y + direction.Y / length * distance);
        return [Pose(0, first, Angle.ToRadians(95)), Pose(1, second, Angle.ToRadians(275))];
    }

    private static NestJobPlacement Pose(int instance, Vector arcCenter, double rotation)
    {
        var (c, s) = (System.Math.Cos(rotation), System.Math.Sin(rotation));
        // The arc's local center (3, 2.5), rotated, lands on arcCenter.
        var x = arcCenter.X - (3 * c - Radius * s);
        var y = arcCenter.Y - (3 * s + Radius * c);
        return new NestJobPlacement("arc", instance, x, y, rotation);
    }

    private static IReadOnlyList<string> Violations(NestJob job, NestJobPlacement[] poses)
    {
        var stock = job.Plates[0];
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, poses) },
            new[] { new PartFulfillment("arc", 2, 2, 0) }, new[] { new StockUsage(stock.Id, 1, null) });
        return NestLayoutCheck.Violations(job, result);
    }

    private static void Validate(NestJob job, NestJobPlacement[] poses) =>
        NestJobValidator.ValidateCandidate(new PlateCandidate(poses), job.Plates[0],
            new Dictionary<string, int> { ["arc"] = 2 }, job.Parts.ToDictionary(p => p.Id));

    [Fact]
    public void ArcsExactlyAtSpacing_PassBothValidators()
    {
        var job = Job();
        var poses = FacingArcs(Spacing);
        var geometry = JobPartGeometry.Read(job.Parts[0].Geometry);

        // The circumscribed outlines alone put this pair too close; only the native contours clear it.
        Assert.False(NestLayoutCheck.Clears(geometry, poses[0], geometry, poses[1], Spacing));
        Assert.Empty(Violations(job, poses));
        Validate(job, poses);
    }

    [Fact]
    public void ArcsCloserThanSpacingLessSlack_FailBothValidators()
    {
        var job = Job();
        // Short by three slacks: the inscribed arcs the runner used to measure hid up to 0.002.
        var poses = FacingArcs(Spacing - 3 * NestTolerances.SpacingSlack);

        Assert.Single(Violations(job, poses));
        var error = Assert.Throws<InvalidOperationException>(() => Validate(job, poses));
        Assert.Contains("spacing", error.Message);
    }
}
