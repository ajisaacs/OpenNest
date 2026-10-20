using OpenNest.CNC.CuttingStrategy;
using OpenNest.Forms;

namespace OpenNest.Tests.CuttingStrategy;

public class CuttingParametersSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesSupportTabs()
    {
        var original = new CuttingParameters
        {
            SupportTabs = true,
            ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
        };

        var json = CuttingParametersSerializer.Serialize(original);
        var restored = CuttingParametersSerializer.Deserialize(json);

        Assert.True(restored.SupportTabs);
    }

    [Fact]
    public void Deserialize_RetiredAutoTabFields_AreIgnored()
    {
        // Settings saved before Auto Tab was removed still load; its sizes are dropped.
        var json =
            "{\"externalLeadIn\":{\"type\":\"None\"},\"externalLeadOut\":{\"type\":\"None\"},\"internalLeadIn\":{\"type\":\"None\"},\"internalLeadOut\":{\"type\":\"None\"},\"arcCircleLeadIn\":{\"type\":\"None\"},\"arcCircleLeadOut\":{\"type\":\"None\"},\"tabsEnabled\":false,\"tabWidth\":0.25,\"pierceClearance\":0.0625,\"autoTabMinSize\":0.5,\"autoTabMaxSize\":3.0}";

        var restored = CuttingParametersSerializer.Deserialize(json);

        Assert.False(restored.TabsEnabled);
        Assert.False(restored.SupportTabs);
        Assert.Equal(0.0625, restored.PierceClearance);
    }
}
