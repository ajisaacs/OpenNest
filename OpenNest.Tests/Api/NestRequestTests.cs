using OpenNest.Api;
using OpenNest.Geometry;

namespace OpenNest.Tests.Api;

public class NestRequestTests
{
    [Fact]
    public void Default_Request_HasSensibleDefaults()
    {
        var request = new NestRequest();

        Assert.Empty(request.Parts);
        Assert.Equal(60, request.SheetSize.Width);
        Assert.Equal(120, request.SheetSize.Length);
        Assert.Equal("Steel, A1011 HR", request.Material);
        Assert.Equal(0.06, request.Thickness);
        Assert.Equal(0.1, request.Spacing);
        Assert.Equal(NestStrategy.Auto, request.Strategy);
        Assert.NotNull(request.Cutting);
    }

    [Fact]
    public void Parts_Accessible_AfterConstruction()
    {
        var request = new NestRequest
        {
            Parts = [new NestRequestPart { DxfPath = "test.dxf", Quantity = 5 }]
        };

        Assert.Single(request.Parts);
        Assert.Equal("test.dxf", request.Parts[0].DxfPath);
        Assert.Equal(5, request.Parts[0].Quantity);
    }

    [Fact]
    public void NestRequestPart_Defaults()
    {
        var part = new NestRequestPart { DxfPath = "part.dxf" };

        Assert.Equal(1, part.Quantity);
        Assert.True(part.AllowRotation);
        Assert.Equal(0, part.Priority);
    }
}
