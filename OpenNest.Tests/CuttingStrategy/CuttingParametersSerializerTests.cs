using OpenNest.CNC.CuttingStrategy;
using OpenNest.Forms;

namespace OpenNest.Tests.CuttingStrategy;

public class CuttingParametersSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAutoTabFields()
    {
        var original = new CuttingParameters
        {
            AutoTabMinSize = 0.5,
            AutoTabMaxSize = 3.0,
            ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 }
        };

        var json = CuttingParametersSerializer.Serialize(original);
        var restored = CuttingParametersSerializer.Deserialize(json);

        Assert.Equal(0.5, restored.AutoTabMinSize);
        Assert.Equal(3.0, restored.AutoTabMaxSize);
    }

    [Fact]
    public void Deserialize_MissingAutoTabFields_DefaultsToZero()
    {
        var json = "{\"externalLeadIn\":{\"type\":\"None\"},\"externalLeadOut\":{\"type\":\"None\"},\"internalLeadIn\":{\"type\":\"None\"},\"internalLeadOut\":{\"type\":\"None\"},\"arcCircleLeadIn\":{\"type\":\"None\"},\"arcCircleLeadOut\":{\"type\":\"None\"},\"tabsEnabled\":false,\"tabWidth\":0.25,\"pierceClearance\":0.0625}";

        var restored = CuttingParametersSerializer.Deserialize(json);

        Assert.Equal(0.0, restored.AutoTabMinSize);
        Assert.Equal(0.0, restored.AutoTabMaxSize);
    }
}
