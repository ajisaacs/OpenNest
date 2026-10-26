using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests;

/// <summary>
/// The multi-phase fill engine and placement strategy are named "Fill". "Default" names the
/// router engine; fill-strategy callers still read "Default" as Fill.
/// </summary>
public class FillStrategyNameTests
{
    [Fact]
    public void FillIsListedBesideTheDefaultRouter()
    {
        var engines = NestingEngineRegistry.AvailableEngines.Select(e => e.Name).ToList();

        Assert.Equal("Default", engines[0]);
        Assert.Contains("Fill", engines);
        Assert.IsType<FixedStrategyNestingEngine>(NestingEngineRegistry.Create("Fill"));
        Assert.Equal(new[] { "Fill", "Strip", "Vertical Remnant", "Horizontal Remnant" },
            PlateFillService.BuiltInStrategies);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData(" default ")]
    public void DefaultNamesTheRouter(string name)
    {
        Assert.Equal("Default", NestingEngineRegistry.ResolveName(name));
        Assert.IsType<DefaultNestingEngine>(NestingEngineRegistry.Create(name));
    }

    [Fact]
    public void PlugInNamedDefaultCannotReplaceTheRouter()
    {
        var before = NestingEngineRegistry.AvailableEngines.Count;

        NestingEngineRegistry.Register("Default", "stale plug-in", () => new FixedStrategyNestingEngine("Strip"));

        Assert.Equal(before, NestingEngineRegistry.AvailableEngines.Count);
        Assert.IsType<DefaultNestingEngine>(NestingEngineRegistry.Create("Default"));
    }

    [Theory]
    [InlineData(null, "Fill")]
    [InlineData("", "Fill")]
    [InlineData("fill", "Fill")]
    [InlineData("Default", "Fill")]
    [InlineData("DEFAULT", "Fill")]
    public void PlacementStrategyResolvesFillAndItsLegacyName(string? strategy, string expected)
    {
        Assert.Equal(expected, PlateFillService.ResolveStrategy(strategy!));
    }

    [Fact]
    public void LegacyStrategyNameStillFillsAPlate()
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        var parts = PlateFillService.FillItem("Default", plate,
            new NestItem { Drawing = drawing, Quantity = 6 }, plate.WorkArea(), null, CancellationToken.None);

        Assert.Equal(6, parts.Count);
    }

    [Theory]
    [InlineData("strip")]
    [InlineData("HORIZONTAL REMNANT")]
    [InlineData("default")]
    public void FillServiceIgnoresCaseWhereTheJobFactoryDoesNot(string strategy)
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(6, 4));

        var parts = PlateFillService.FillItem(strategy, plate,
            new NestItem { Drawing = drawing, Quantity = 6 }, plate.WorkArea(), null, CancellationToken.None);

        Assert.Equal(6, parts.Count);
        Assert.Throws<NotSupportedException>(() => PlateNesterFactory.Create(strategy));
    }

    [Fact]
    public void FillServiceRejectsUnknownNamesWithTheKnownList()
    {
        var error = Assert.Throws<NotSupportedException>(() => PlateFillService.FillItem("Mystery",
            new Plate(new Size(60, 80)), new NestItem(), new Box(0, 0, 1, 1), null, CancellationToken.None));

        Assert.Equal("Unknown placement strategy: Mystery. Known strategies: "
            + "Fill, Strip, Vertical Remnant, Horizontal Remnant.", error.Message);
    }

    [Fact]
    public void WholeJobRunnerAcceptsFillAndItsLegacyName()
    {
        Assert.IsType<PlateNester>(PlateNesterFactory.Create("Fill"));
        Assert.IsType<PlateNester>(PlateNesterFactory.Create("Default"));
        Assert.Equal("Fill", new NestJobOptions().PlacementStrategy);

        var job = FiniteStockJobTests.Job(1);
        var legacy = new NestJobRunner(PlateNesterFactory.Create)
            .Solve(new NestJob(job.Parts, job.Plates, new NestJobOptions("Default")));
        Assert.Equal(NestJobStatus.Complete, legacy.Status);
    }
}
