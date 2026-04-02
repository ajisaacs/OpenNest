namespace OpenNest.Tests.Engine;

public class NestPhaseExtensionsTests
{
    [Theory]
    [InlineData(NestPhase.Linear, "Trying rotations...")]
    [InlineData(NestPhase.RectBestFit, "Trying best fit...")]
    [InlineData(NestPhase.Pairs, "Trying pairs...")]
    [InlineData(NestPhase.Nfp, "Trying NFP...")]
    [InlineData(NestPhase.Extents, "Trying extents...")]
    [InlineData(NestPhase.Custom, "Custom")]
    public void DisplayName_ReturnsDescription(NestPhase phase, string expected)
    {
        Assert.Equal(expected, phase.DisplayName());
    }

    [Theory]
    [InlineData(NestPhase.Linear, "Linear")]
    [InlineData(NestPhase.RectBestFit, "BestFit")]
    [InlineData(NestPhase.Pairs, "Pairs")]
    [InlineData(NestPhase.Nfp, "NFP")]
    [InlineData(NestPhase.Extents, "Extents")]
    [InlineData(NestPhase.Custom, "Custom")]
    public void ShortName_ReturnsShortLabel(NestPhase phase, string expected)
    {
        Assert.Equal(expected, phase.ShortName());
    }
}
