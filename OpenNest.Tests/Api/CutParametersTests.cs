namespace OpenNest.Tests.Api;

public class CutParametersTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var cp = CutParameters.Default;

        Assert.Equal(100, cp.Feedrate);
        Assert.Equal(300, cp.RapidTravelRate);
        Assert.Equal(TimeSpan.FromSeconds(0.5), cp.PierceTime);
        Assert.Equal(Units.Inches, cp.Units);
    }
}
