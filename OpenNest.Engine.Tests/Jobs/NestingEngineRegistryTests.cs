using OpenNest.Engine.Jobs;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

public class NestingEngineRegistryTests
{
    [Fact]
    public void BuiltInStrategiesAreRegistered()
    {
        var names = NestingEngineRegistry.AvailableEngines.Select(e => e.Name).ToList();

        Assert.Contains("Rectangles", names);
        Assert.Contains("Irregular", names);
        Assert.Contains("Default", names);
        Assert.Contains("Strip", names);
        Assert.Contains("Vertical Remnant", names);
        Assert.Contains("Horizontal Remnant", names);
    }

    [Fact]
    public void RenamedPlugInNamesResolveToTheirBuiltInReplacements()
    {
        Assert.Equal("Irregular", NestingEngineRegistry.ResolveName("Opus55NestingEngine"));
        Assert.Equal("Rectangles", NestingEngineRegistry.ResolveName("rectanglesnestingengine"));
        Assert.Equal("Rectangles", NestingEngineRegistry.ResolveName(" rectangles "));
        Assert.IsType<OpenNest.Engine.NestingEngines.Irregular.IrregularNestingEngine>(
            NestingEngineRegistry.Create("Opus55NestingEngine"));
    }

    [Fact]
    public void RetiredEnginesAreNotSilentlyAliased()
    {
        Assert.Null(NestingEngineRegistry.ResolveName("Gpt6AstraNestingEngine"));
        Assert.Null(NestingEngineRegistry.ResolveName("Qwen38FlashNextNestingEngine"));
        Assert.Null(NestingEngineRegistry.ResolveName("  "));
        Assert.Throws<NotSupportedException>(() => NestingEngineRegistry.Create("Qwen38FlashNextNestingEngine"));
    }

    [Fact]
    public void LeftoverPlugInUnderARenamedNameCannotShadowItsReplacement()
    {
        var before = NestingEngineRegistry.AvailableEngines.Count;

        NestingEngineRegistry.Register("Opus55NestingEngine", "stale plug-in",
            () => new FixedStrategyNestingEngine("Default"));

        Assert.Equal(before, NestingEngineRegistry.AvailableEngines.Count);
        Assert.Equal("Irregular", NestingEngineRegistry.ResolveName("Opus55NestingEngine"));
    }

    [Fact]
    public void EachBuiltInFactoryProducesAWorkingEngine()
    {
        foreach (var info in NestingEngineRegistry.AvailableEngines)
        {
            var engine = info.Factory();
            var result = engine.Solve(FiniteStockJobTests.Job(1));

            Assert.Equal(NestJobStatus.Complete, result.Status);
        }
    }

    [Fact]
    public void DuplicateNameIsSkipped()
    {
        var before = NestingEngineRegistry.AvailableEngines.Count;

        NestingEngineRegistry.Register(
            "Default",
            "duplicate",
            () => new FixedStrategyNestingEngine("Default")
        );

        Assert.Equal(before, NestingEngineRegistry.AvailableEngines.Count);
    }

    [Fact]
    public void CreateResolvesKnownNamesCaseInsensitivelyAndRejectsUnknown()
    {
        Assert.NotNull(NestingEngineRegistry.Create("default"));
        Assert.NotNull(NestingEngineRegistry.Create("Vertical Remnant"));
        Assert.NotNull(NestingEngineRegistry.Create("stockladder"));

        Assert.Throws<NotSupportedException>(() => NestingEngineRegistry.Create("Mystery Engine"));
        Assert.Throws<ArgumentException>(() => NestingEngineRegistry.Create("  "));
        Assert.Throws<ArgumentNullException>(() => NestingEngineRegistry.Create(null!));
    }

    [Fact]
    public void CreateProducesAWorkingEngine()
    {
        var engine = NestingEngineRegistry.Create("Strip");
        var result = engine.Solve(FiniteStockJobTests.Job(1));

        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void LoadPluginsAgainstMissingDirectoryIsANoOp()
    {
        var before = NestingEngineRegistry.AvailableEngines.Count;

        NestingEngineRegistry.LoadPlugins(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        );

        Assert.Equal(before, NestingEngineRegistry.AvailableEngines.Count);
    }
}
