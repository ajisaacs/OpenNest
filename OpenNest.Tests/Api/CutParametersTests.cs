using OpenNest.Api;

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

    [Fact]
    public void Properties_AreSettable()
    {
        var cp = new CutParameters
        {
            Feedrate = 200,
            RapidTravelRate = 500,
            PierceTime = TimeSpan.FromSeconds(1.0),
            LeadInLength = 0.25,
            PostProcessor = "CL-707",
            Units = Units.Millimeters
        };

        Assert.Equal(200, cp.Feedrate);
        Assert.Equal(0.25, cp.LeadInLength);
        Assert.Equal("CL-707", cp.PostProcessor);
    }
}
