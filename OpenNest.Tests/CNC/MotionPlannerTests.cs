using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

public class MotionPlannerTests
{
    // 600 in/min = 10 in/s; reaching it at 100 in/s² takes 0.1 s over 0.5 in.
    private static CutParameters Parameters(double deviation = 0, double rapidAcceleration = 100) => new()
    {
        Feedrate = 600,
        RapidTravelRate = 1200,
        PierceTime = TimeSpan.Zero,
        Units = Units.Inches,
        Motion = new MotionProfile
        {
            CutAcceleration = 100,
            RapidAcceleration = rapidAcceleration,
            CornerDeviation = deviation,
        },
    };

    private static CutSimulation Simulate(CutParameters parameters, params ICode[] codes)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        foreach (var code in codes)
            program.Codes.Add(code);
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(new Drawing("part", program), Vector.Zero));
        return CutSimulation.Build(plate, parameters);
    }

    private static double CutSeconds(CutSimulation sim) =>
        sim.Steps.Where(s => s.Kind == CutSimulationStepKind.Cut).Sum(s => s.Duration.TotalSeconds);

    [Fact]
    public void LongLineRampsUpCruisesAndRampsDown()
    {
        var sim = Simulate(Parameters(), new LinearMove(10, 0));

        // 0.1 s up + 9 in at 10 in/s + 0.1 s down.
        Assert.Equal(1.1, CutSeconds(sim), 6);
        var line = sim.Steps.Single(s => s.Kind == CutSimulationStepKind.Cut);
        Assert.Equal(0.5, line.ProgressAt(line.Duration / 2), 6);
        Assert.Equal(0.05, line.ProgressAt(TimeSpan.FromSeconds(0.1)), 6); // 0.5 in ramp of 10 in
        Assert.Equal(10, line.SpeedAt(line.Duration / 2), 6);
    }

    [Fact]
    public void ShortLineNeverReachesFeed()
    {
        var sim = Simulate(Parameters(), new LinearMove(0.5, 0));

        var line = sim.Steps.Single(s => s.Kind == CutSimulationStepKind.Cut);
        Assert.Equal(System.Math.Sqrt(50), line.CruiseSpeed, 6);
        Assert.Equal(2 * System.Math.Sqrt(50) / 100, line.Duration.TotalSeconds, 6); // TimeSpan ticks are 100 ns
    }

    [Fact]
    public void CollinearMovesDoNotSlowBetweenThem()
    {
        var sim = Simulate(Parameters(), new LinearMove(5, 0), new LinearMove(10, 0));

        Assert.Equal(1.1, CutSeconds(sim), 6);
    }

    [Fact]
    public void CornersSlowByTheirDeviation()
    {
        var square = new ICode[] { new LinearMove(10, 0), new LinearMove(10, 10), new LinearMove(0, 10), new LinearMove(0, 0) };

        var stopping = CutSeconds(Simulate(Parameters(deviation: 0), square));
        var rounding = CutSeconds(Simulate(Parameters(deviation: 0.01), square));

        Assert.Equal(4.4, stopping, 6); // every corner comes to rest
        Assert.InRange(rounding, 4.2 + 1e-6, 4.4 - 1e-6);
        // 90°: v² = a·δ·sin45° / (1 − sin45°)
        var expected = System.Math.Sqrt(100 * 0.01 * System.Math.Sqrt(0.5) / (1 - System.Math.Sqrt(0.5)));
        var second = Simulate(Parameters(deviation: 0.01), square).Steps.Where(s => s.Kind == CutSimulationStepKind.Cut).ElementAt(1);
        Assert.Equal(expected, second.EntrySpeed, 6);
    }

    [Fact]
    public void SmallArcsAreCappedByCentripetalAcceleration()
    {
        var sim = Simulate(Parameters(), new RapidMove(0.1, 0), new ArcMove(new Vector(0.1, 0), Vector.Zero, RotationType.CCW));

        var arc = sim.Steps.Single(s => s.IsArc);
        Assert.Equal(System.Math.Sqrt(100 * 0.1), arc.CruiseSpeed, 6); // well under 10 in/s
    }

    [Fact]
    public void TravelUsesTheRapidAcceleration()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(20, 0));
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(new Drawing("part", program), Vector.Zero));

        var sim = CutSimulation.Build(plate, Parameters(rapidAcceleration: 200));

        // 20 in/s reached in 0.1 s over 1 in each way, 18 in at full speed.
        var rapid = sim.Steps.Single();
        Assert.Equal(0.1 + 0.9 + 0.1, rapid.Duration.TotalSeconds, 6);
    }

    [Fact]
    public void PlateTimeUsesTheTimelineTimesQuantity()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(10, 0));
        var plate = new Plate(48, 96) { Quantity = 3 };
        plate.Parts.Add(new Part(new Drawing("part", program), Vector.Zero));
        var nest = new Nest();
        nest.Plates.Add(plate);

        var time = Timing.CalculateTime(nest, Parameters());

        Assert.Equal(3 * 1.1, time.TotalSeconds, 6);
    }

    [Fact]
    public void AccelerationOnlyEverAddsTime()
    {
        var codes = new ICode[]
        {
            new LinearMove(4, 0),
            new ArcMove(new Vector(6, 2), new Vector(4, 2), RotationType.CCW),
            new LinearMove(6, 6),
            new LinearMove(0, 0),
        };
        var constant = Parameters();
        constant.Motion = null;

        var plain = Simulate(constant, codes);
        var planned = Simulate(Parameters(deviation: 0.005), codes);

        Assert.True(planned.TotalTime > plain.TotalTime);
        Assert.Equal(plain.Steps.Count, planned.Steps.Count);
        Assert.Equal(planned.Steps[^1].EndTime, planned.TotalTime);
    }
}
