using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

public class CutSimulationTests
{
    private static readonly CutParameters Parameters = new()
    {
        Feedrate = 100, // in/min
        RapidTravelRate = 600,
        PierceTime = TimeSpan.FromSeconds(1),
        Units = Units.Inches,
    };

    private static Program Square(double size)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(size, 0));
        program.Codes.Add(new LinearMove(size, size));
        program.Codes.Add(new LinearMove(0, size));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    private static Plate PlateWith(params (Program Program, Vector Location)[] parts)
    {
        var plate = new Plate(48, 96);
        foreach (var (program, location) in parts)
            plate.Parts.Add(new Part(new Drawing("part", program), location));
        return plate;
    }

    [Fact]
    public void SquareIsRapidPierceThenFourTimedCuts()
    {
        var plate = PlateWith((Square(10), new Vector(30, 40)));

        var sim = CutSimulation.Build(plate, Parameters);

        Assert.Equal(
            new[]
            {
                CutSimulationStepKind.Rapid,
                CutSimulationStepKind.Pierce,
                CutSimulationStepKind.Cut,
                CutSimulationStepKind.Cut,
                CutSimulationStepKind.Cut,
                CutSimulationStepKind.Cut,
            },
            sim.Steps.Select(s => s.Kind));
        Assert.Equal(Vector.Zero, sim.Steps[0].Start);
        Assert.Equal(new Vector(30, 40), sim.Steps[0].End);
        Assert.Equal(new Vector(40, 40), sim.Steps[2].End);

        var expected = TimeSpan.FromMinutes(50.0 / 600) + TimeSpan.FromSeconds(1) + TimeSpan.FromMinutes(40.0 / 100);
        Assert.Equal(expected.TotalSeconds, sim.TotalTime.TotalSeconds, 6);
    }

    [Fact]
    public void SecondPartTravelsFromWhereTheFirstFinished()
    {
        var plate = PlateWith((Square(10), new Vector(0, 0)), (Square(5), new Vector(20, 0)));

        var sim = CutSimulation.Build(plate, Parameters);

        var travel = sim.Steps.Single(s => s.Kind == CutSimulationStepKind.Rapid && s.PartIndex == 1);
        Assert.Equal(new Vector(0, 0), travel.Start);
        Assert.Equal(new Vector(20, 0), travel.End);
        Assert.Equal(2, sim.Steps.Count(s => s.Kind == CutSimulationStepKind.Pierce));
    }

    [Fact]
    public void RapidInPlaceStillEndsTheCut()
    {
        // The second part starts exactly where the first one finished.
        var plate = PlateWith((Square(10), Vector.Zero), (Square(10), Vector.Zero));

        var sim = CutSimulation.Build(plate, Parameters);

        Assert.Equal(2, sim.Steps.Count(s => s.Kind == CutSimulationStepKind.Pierce));
        Assert.DoesNotContain(sim.Steps, s => s.Kind == CutSimulationStepKind.Rapid);
    }

    [Fact]
    public void UncutLayersTravelHeadUpAndRepierce()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(6, 0) { Layer = LayerType.Tab });
        program.Codes.Add(new LinearMove(10, 0));
        var plate = PlateWith((program, Vector.Zero));

        var sim = CutSimulation.Build(plate, Parameters);

        Assert.Equal(
            new[]
            {
                CutSimulationStepKind.Pierce,
                CutSimulationStepKind.Cut,
                CutSimulationStepKind.Rapid,
                CutSimulationStepKind.Pierce,
                CutSimulationStepKind.Cut,
            },
            sim.Steps.Select(s => s.Kind));
    }

    [Fact]
    public void FullCircleArcSweepsOnceInItsDirection()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(2, 0));
        program.Codes.Add(new ArcMove(new Vector(2, 0), new Vector(0, 0), RotationType.CW));
        var plate = PlateWith((program, Vector.Zero));

        var sim = CutSimulation.Build(plate, Parameters);

        var arc = sim.Steps.Single(s => s.IsArc);
        Assert.Equal(-2 * System.Math.PI, arc.Sweep, 9);
        Assert.Equal(4 * System.Math.PI, arc.Length, 9);
        var quarter = arc.PointAt(0.25);
        Assert.Equal(0, quarter.X, 9);
        Assert.Equal(-2, quarter.Y, 9); // clockwise from (2, 0)
    }

    [Fact]
    public void ToolPositionInterpolatesAcrossTheTimeline()
    {
        var plate = PlateWith((Square(10), Vector.Zero));
        var sim = CutSimulation.Build(plate, Parameters);
        var firstCut = sim.Steps.First(s => s.Kind == CutSimulationStepKind.Cut);

        var halfway = firstCut.StartTime + firstCut.Duration / 2;

        Assert.Equal(new Vector(5, 0), sim.ToolPositionAt(halfway));
        Assert.Equal(sim.Steps.ToList().IndexOf(firstCut), sim.IndexAt(halfway));
        Assert.Equal(sim.Steps.Count, sim.IndexAt(sim.TotalTime));
        Assert.Equal(Vector.Zero, sim.ToolPositionAt(sim.TotalTime));
    }

    [Fact]
    public void MillimeterRatesArePerSecond()
    {
        var plate = PlateWith((Square(100), Vector.Zero));
        var parameters = new CutParameters
        {
            Feedrate = 50,
            RapidTravelRate = 500,
            PierceTime = TimeSpan.Zero,
            Units = Units.Millimeters,
        };

        var sim = CutSimulation.Build(plate, parameters);

        Assert.Equal(8, sim.TotalTime.TotalSeconds, 6); // 400 mm at 50 mm/s; the rapid is zero length
    }

    private static Program SquareWithHole()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(6, 5));
        program.Codes.Add(new ArcMove(new Vector(6, 5), new Vector(5, 5), RotationType.CW));
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(10, 10));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    [Fact]
    public void ContoursAndPartsStartAtTheirPierces()
    {
        var plate = PlateWith((SquareWithHole(), Vector.Zero), (SquareWithHole(), new Vector(20, 0)));

        var sim = CutSimulation.Build(plate, Parameters);

        var pierces = sim.Steps.Where(s => s.Kind == CutSimulationStepKind.Pierce).Select(s => s.StartTime).ToArray();
        Assert.Equal(4, pierces.Length);
        Assert.Equal(pierces, sim.ContourStarts);
        Assert.Equal(new[] { pierces[0], pierces[2] }, sim.PartStarts);
    }

    [Fact]
    public void JumpsMoveBetweenContoursAndParts()
    {
        var plate = PlateWith((SquareWithHole(), Vector.Zero), (SquareWithHole(), new Vector(20, 0)));
        var sim = CutSimulation.Build(plate, Parameters);
        var contours = sim.ContourStarts;

        Assert.Equal(contours[0], sim.NextContour(TimeSpan.Zero));
        Assert.Equal(contours[2], sim.NextContour(contours[1]));
        Assert.Equal(sim.TotalTime, sim.NextContour(contours[3]));
        Assert.Equal(sim.PartStarts[1], sim.NextPart(contours[0]));
        Assert.Equal(sim.TotalTime, sim.NextPart(sim.PartStarts[1]));

        // Mid-contour goes back to that contour's start; at a start it goes to the one before.
        var midSecond = contours[1] + TimeSpan.FromSeconds(2);
        Assert.Equal(contours[1], sim.PreviousContour(midSecond));
        Assert.Equal(contours[0], sim.PreviousContour(contours[1]));
        Assert.Equal(TimeSpan.Zero, sim.PreviousContour(contours[0]));
        Assert.Equal(sim.PartStarts[0], sim.PreviousPart(sim.PartStarts[1]));
        Assert.Equal(sim.PartStarts[1], sim.PreviousPart(sim.TotalTime));
    }

    [Fact]
    public void EmptyPlateHasNoTimeline()
    {
        var sim = CutSimulation.Build(new Plate(48, 96), Parameters);

        Assert.Empty(sim.Steps);
        Assert.Equal(TimeSpan.Zero, sim.TotalTime);
        Assert.Equal(-1, sim.IndexAt(TimeSpan.Zero));
    }
}
