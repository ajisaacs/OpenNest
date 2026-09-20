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
        Assert.Null(request.Plates);
        Assert.Equal("Default", request.PlacementStrategy);
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
            Parts = [new NestRequestPart { DxfPath = "test.dxf", Quantity = 5 }],
        };

        Assert.Single(request.Parts);
        Assert.Equal("test.dxf", request.Parts[0].DxfPath);
        Assert.Equal(5, request.Parts[0].Quantity);
    }

    [Fact]
    public void NestRequestPart_Defaults()
    {
        var part = new NestRequestPart { DxfPath = "part.dxf" };

        Assert.Null(part.Id);
        Assert.Equal(1, part.Quantity);
        Assert.True(part.AllowRotation);
        Assert.Equal(0, part.Priority);
    }

    [Fact]
    public void ExplicitPlates_PreserveStockSettings()
    {
        var request = new NestRequest
        {
            Plates =
            [
                new NestRequestPlate
                {
                    Id = "remnant",
                    Size = new Size(24, 48),
                    Quantity = 3,
                    PartSpacing = 0.2,
                    EdgeSpacing = new Spacing(1, 2, 3, 4),
                    Quadrant = 3,
                },
            ],
        };

        var plate = Assert.Single(request.Plates!);
        Assert.Equal("remnant", plate.Id);
        Assert.Equal(24, plate.Size.Width);
        Assert.Equal(48, plate.Size.Length);
        Assert.Equal(3, plate.Quantity);
        Assert.Equal(0.2, plate.PartSpacing);
        Assert.Equal(new Spacing(1, 2, 3, 4), plate.EdgeSpacing);
        Assert.Equal(3, plate.Quadrant);
    }
}
