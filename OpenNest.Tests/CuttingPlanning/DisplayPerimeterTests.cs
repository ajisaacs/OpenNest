using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Converters;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A supplied blank (a purchased door) whose outline is a display perimeter: shown and treated as
/// material, never cut. Only its cutouts are cut, each as a hole.
/// </summary>
public class DisplayPerimeterTests
{
    // The door fills a 30 x 20 sheet: new Size(width, length) puts Length (30) along X.
    private static Plate DoorPlate(Program program)
    {
        var plate = new Plate(new Size(20, 30));
        plate.Parts.Add(new Part(new Drawing("door", program), Vector.Zero));
        return plate;
    }

    internal static Program Door(LayerType outline = LayerType.DisplayPerimeter, bool cutouts = true,
        LayerType? lastEdge = null)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(0, 20) { Layer = outline });
        program.Codes.Add(new LinearMove(30, 20) { Layer = outline });
        program.Codes.Add(new LinearMove(30, 0) { Layer = outline });
        program.Codes.Add(new LinearMove(0, 0) { Layer = lastEdge ?? outline });
        if (cutouts)
            AddCutouts(program);
        return program;
    }

    private static void AddCutouts(Program program)
    {
        program.MoveTo(5, 5); program.LineTo(5, 10); program.LineTo(10, 10); program.LineTo(10, 5); program.LineTo(5, 5);
        program.MoveTo(15, 5); program.LineTo(15, 12); program.LineTo(25, 12); program.LineTo(25, 5); program.LineTo(15, 5);
        program.MoveTo(9.5, 15);
        program.Codes.Add(new ArcMove(new Vector(9.5, 15), new Vector(8, 15), RotationType.CW));
    }

    // Cutout extents, for checking where pierces and cuts land.
    private static readonly Box[] Cutouts =
    [
        new Box(5, 5, 5, 5),
        new Box(15, 5, 10, 7),
        new Box(6.5, 13.5, 3, 3),
    ];

    private static bool InsideACutout(Vector point) => Cutouts.Any(box =>
        point.X >= box.Left - 1e-6 && point.X <= box.Right + 1e-6 && point.Y >= box.Bottom - 1e-6 && point.Y <= box.Top + 1e-6);

    [Fact]
    public void Door_PlansEveryCutoutAsAHoleAndNeverCutsTheOutline()
    {
        var plate = DoorPlate(Door());
        var part = plate.Parts[0];

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        var result = Assert.Single(proposal.Plates).Result;
        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: false).Status);

        var motions = ExecutionMotionReader.Read(part.Program, part.Location, null, default).Motions;
        Assert.DoesNotContain(motions, m => m.Layer == LayerType.DisplayPerimeter);
        var pierces = motions.Where((m, i) => m.Layer == LayerType.Leadin && m.Start is { } && (i == 0 || motions[i - 1].Rapid))
            .Select(m => m.Start!.Value).ToArray();
        Assert.Equal(3, pierces.Length);
        Assert.All(pierces, p => Assert.True(InsideACutout(p), $"Pierce ({p.X}, {p.Y}) is on the door."));
        Assert.All(motions.Where(m => !m.Rapid), m => Assert.True(InsideACutout(m.End), $"Cut to ({m.End.X}, {m.End.Y}) leaves the cutouts."));
    }

    private static (Plate Plate, Part Part) PlannedDoor()
    {
        var plate = DoorPlate(Door());
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: false).Status);
        return (plate, plate.Parts[0]);
    }

    [Fact]
    public void AfterApply_TheDoorKeepsItsOutlineAsANeverCutTail()
    {
        var (_, part) = PlannedDoor();

        Assert.Equal(30, part.BoundingBox.Length, 6);
        Assert.Equal(20, part.BoundingBox.Width, 6);
        Assert.True(DisplayPerimeterTail.HasTail(part.Program));
        var executed = DisplayPerimeterTail.ForPosting(part.Program, "Part 1");
        Assert.False(DisplayPerimeterTail.HasDisplayPerimeterMotion(executed));
        // Posting and executed reads agree: the tail is the only thing either leaves out.
        var full = ExecutionMotionReader.Read(part.Program, part.Location, null, default).Motions;
        var posted = ExecutionMotionReader.Read(executed, part.Location, null, default).Motions;
        Assert.Equal(full.Count, posted.Count);
        Assert.True(executed.Codes.Count < part.Program.Codes.Count);
    }

    [Fact]
    public void AfterApply_AutomaticCutOffsStillAddNoLineAcrossTheBlank()
    {
        var (plate, _) = PlannedDoor();

        var staging = AutomaticCutOffBatch.Stage([plate],
            new AutomaticCutOffOptions { Spacing = 10, MinimumStripLength = 5 }, new CutOffSettings());

        Assert.Empty(Assert.Single(staging.Plans).Definitions);
    }

    [Fact]
    public void AfterApply_TheDoorPlansAgainWithTheSameHoles()
    {
        var (plate, _) = PlannedDoor();

        var again = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        Assert.True(again.CanApply, string.Join("\n", again.Describe("in")));
    }

    [Fact]
    public void AfterApply_TimingRapidsAndSimulationIgnoreTheTail()
    {
        var (plate, part) = PlannedDoor();

        var executed = DisplayPerimeterTail.WithoutTail(part.Program);
        Assert.Equal(Timing.GetTimingInfo(executed).PierceCount, Timing.GetTimingInfo(part.Program).PierceCount);
        Assert.Equal(Timing.GetTimingInfo(executed).CutDistance, Timing.GetTimingInfo(part.Program).CutDistance, 9);
        // The tail starts at the outline's first corner (0, 0); no rapid may travel there.
        var rapids = RapidEnumerator.Enumerate(plate.Parts);
        Assert.DoesNotContain(rapids, r => r.To.DistanceTo(Vector.Zero) < 1e-9);
        var simulation = CutSimulation.Build(plate, new CutParameters
        {
            Feedrate = 100,
            RapidTravelRate = 600,
            PierceTime = TimeSpan.FromSeconds(1),
            Units = Units.Inches,
        });
        Assert.Equal(3, simulation.Steps.Count(step => step.Kind == CutSimulationStepKind.Pierce));
        Assert.DoesNotContain(simulation.Steps, step => step.Layer == LayerType.DisplayPerimeter);
    }

    [Fact]
    public void ManualAndLegacyLeadIns_RefuseADisplayPerimeterAndLeaveThePartAlone()
    {
        var part = new Part(new Drawing("door", Door()), Vector.Zero);
        var before = ExplicitContourTests.Fingerprint(part.Program);
        var edge = ConvertProgram.ToGeometry(part.Program).First(e => e.Layer == SpecialLayers.DisplayPerimeter);
        var hole = ConvertProgram.ToGeometry(part.Program).First(e => e.Layer == SpecialLayers.Cut);

        var onEdge = Assert.Throws<InvalidOperationException>(() => part.ApplySingleLeadIn(
            ExplicitContourTests.Parameters(), new Vector(0, 10), edge, OpenNest.CNC.CuttingStrategy.ContourType.External));
        Assert.Throws<InvalidOperationException>(() => part.ApplySingleLeadIn(
            ExplicitContourTests.Parameters(), new Vector(5, 7), hole, OpenNest.CNC.CuttingStrategy.ContourType.Internal));
        Assert.Throws<InvalidOperationException>(() => part.ApplyLeadIns(ExplicitContourTests.Parameters(), Vector.Zero));

        Assert.Contains("Plan Cutting", onEdge.Message);
        Assert.False(part.HasManualLeadIns);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(part.Program));
    }

    [Fact]
    public void Posting_RefusesAnUnplannedDisplayPerimeter()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DisplayPerimeterTail.ForPosting(Door(), "Part 4"));

        Assert.Contains("Part 4", error.Message);
        Assert.Contains("Plan Cutting", error.Message);
        var holesOnly = Door(LayerType.Cut);
        Assert.Same(holesOnly, DisplayPerimeterTail.ForPosting(holesOnly, "Part 1"));
    }

    [Fact]
    public void Door_AutomaticCutOffsAddNoLineAcrossTheBlank()
    {
        var plate = DoorPlate(Door());

        var staging = AutomaticCutOffBatch.Stage([plate],
            new AutomaticCutOffOptions { Spacing = 10, MinimumStripLength = 5 }, new CutOffSettings());

        Assert.False(staging.IsBlocked);
        Assert.Empty(Assert.Single(staging.Plans).Definitions);
    }

    [Theory]
    [InlineData("mixed", "one whole closed contour")]
    [InlineData("hole", "enclose every other contour")]
    [InlineData("nothing", "display perimeter with no cutouts")]
    [InlineData("notch", "enclose every other contour")]
    [InlineData("second", "enclose every other contour")]
    [InlineData("tabbedOnly", "closed cutout without tabs")]
    public void InvalidDisplayPerimeters_AreRefusedWithTheirReason(string layout, string reason)
    {
        var program = layout switch
        {
            "mixed" => Door(lastEdge: LayerType.Cut),
            "nothing" => Door(cutouts: false),
            "notch" => NotchedDoor(),
            "second" => SecondDisplayRing(),
            "tabbedOnly" => TabbedOnly(),
            _ => HoleOnDisplay(),
        };
        var plate = DoorPlate(program);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));

        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains(reason, StringComparison.OrdinalIgnoreCase));
        // Not even an unverified plan: nothing about a refused blank may be applied.
        var proposal = CuttingPlanBatch.Capture([DoorPlate(program)], ExplicitContourTests.Parameters(), false).Plan();
        Assert.False(proposal.CanApply);
        Assert.False(proposal.CanApplyWithWarnings);

        // An L-shaped blank with a cutout in its notch: inside the outline's box, outside the blank.
        static Program NotchedDoor()
        {
            var program = new Program();
            Ring(program, LayerType.DisplayPerimeter, (0, 0), (0, 20), (15, 20), (15, 10), (30, 10), (30, 0));
            Ring(program, LayerType.Cut, (2, 2), (2, 6), (6, 6), (6, 2));
            Ring(program, LayerType.Cut, (20, 14), (20, 18), (25, 18), (25, 14));
            return program;
        }

        static Program SecondDisplayRing()
        {
            var program = Door();
            Ring(program, LayerType.DisplayPerimeter, (26, 14), (26, 18), (28, 18), (28, 14));
            return program;
        }

        static Program TabbedOnly()
        {
            var program = Door(cutouts: false);
            program.Codes.Add(new RapidMove(5, 5));
            program.Codes.Add(new LinearMove(5, 10) { Layer = LayerType.Cut });
            program.Codes.Add(new LinearMove(10, 10) { Layer = LayerType.Tab });
            program.Codes.Add(new LinearMove(10, 5) { Layer = LayerType.Cut });
            program.Codes.Add(new LinearMove(5, 5) { Layer = LayerType.Cut });
            return program;
        }

        static Program HoleOnDisplay()
        {
            var program = Door(LayerType.Cut, cutouts: false);
            program.MoveTo(5, 5);
            foreach (var (x, y) in new[] { (5.0, 10.0), (10.0, 10.0), (10.0, 5.0), (5.0, 5.0) })
                program.Codes.Add(new LinearMove(x, y) { Layer = LayerType.DisplayPerimeter });
            return program;
        }
    }

    private static void Ring(Program program, LayerType layer, params (double X, double Y)[] points)
    {
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1).Append(points[0]))
            program.Codes.Add(new LinearMove(x, y) { Layer = layer });
    }

    [Fact]
    public void Preparation_HasNoCutPerimeterAndTreatsEveryContourAsAHole()
    {
        var prepared = PreparedContours.Capture(Door(), ExplicitContourTests.Parameters());

        Assert.False(prepared.HasCutPerimeter);
        Assert.Equal(3, prepared.Count);
        Assert.Equal(prepared.Count, prepared.PerimeterOrdinal);
        Assert.True(PreparedContours.Capture(Door(LayerType.Cut), ExplicitContourTests.Parameters()).HasCutPerimeter);
    }

    [Fact]
    public void CleanBounds_IncludeTheDisplayPerimeter()
    {
        var bounds = NestedPartContainment.CleanBounds(Door(), Vector.Zero, default);

        Assert.Equal(30, bounds.Length, 9);
        Assert.Equal(20, bounds.Width, 9);
    }

    [Fact]
    public void Replay_RefusesAProgramThatCutsTheDisplayPerimeter()
    {
        var material = LeadMaterialSnapshot.Capture(Door(), Vector.Zero);
        Assert.True(material.IsComplete, material.Reason);

        var holesOnly = new Program();
        AddCutouts(holesOnly);
        var cutsOutline = Door(LayerType.Cut);

        Assert.True(ContourProgramVerifier.Verify(
            ExecutionMotionReader.ReadSupported(holesOnly, Vector.Zero, null, default), material, null, default));
        Assert.False(ContourProgramVerifier.Verify(
            ExecutionMotionReader.ReadSupported(cutsOutline, Vector.Zero, null, default), material, null, default));
        // Same count of cut rings as a valid plan: the outline in place of the circular cutout.
        var outlineForAHole = Door(LayerType.Cut, cutouts: false);
        outlineForAHole.MoveTo(5, 5); outlineForAHole.LineTo(5, 10); outlineForAHole.LineTo(10, 10);
        outlineForAHole.LineTo(10, 5); outlineForAHole.LineTo(5, 5);
        outlineForAHole.MoveTo(15, 5); outlineForAHole.LineTo(15, 12); outlineForAHole.LineTo(25, 12);
        outlineForAHole.LineTo(25, 5); outlineForAHole.LineTo(15, 5);
        Assert.False(ContourProgramVerifier.Verify(
            ExecutionMotionReader.ReadSupported(outlineForAHole, Vector.Zero, null, default), material, null, default));
    }
}
