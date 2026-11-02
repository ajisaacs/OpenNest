using OpenNest.Forms;

namespace OpenNest.Desktop.Tests.Forms;

/// <summary>
/// Only the pure strategy mapping is covered here: setting EngineName or loading the saved
/// selection reads and writes the real per-user engine-selection.json.
/// </summary>
public class EngineSelectionTests
{
    [Theory]
    [InlineData("Fill")]
    [InlineData("Strip")]
    [InlineData("vertical remnant")]
    [InlineData("  Horizontal Remnant ")]
    public void BuiltInPlacementStrategies_AreFillStrategies(string name) =>
        Assert.True(EngineSelection.IsFillStrategy(name));

    [Theory]
    [InlineData("Default")]
    [InlineData("Irregular")]
    [InlineData("Rectangles")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void JobsOnlyEnginesAndBlanks_AreNotFillStrategies(string? name) =>
        Assert.False(EngineSelection.IsFillStrategy(name!));

    [Fact]
    public void EveryBuiltInEngineHasADescription()
    {
        foreach (var name in EngineSelection.UiEngineNames)
            Assert.NotEmpty(EngineSelection.Description(name));
    }

    [Fact]
    public void ToolTipSaysWhatFillAreaUsesForJobOnlyEngines()
    {
        Assert.DoesNotContain("Fill Area", EngineSelection.ToolTip("Vertical Remnant"));
        Assert.Contains("Fill Area uses Lattice", EngineSelection.ToolTip("Irregular"));
    }

}
