using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenNest.Tests.Engine;

public class MultiPlateNesterTests
{
    private static Drawing MakeDrawing(string name, double width, double length)
    {
        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(width, 0)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(width, length)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, length)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        var drawing = new Drawing(name, program);
        drawing.UpdateArea();
        return drawing;
    }

    private static NestItem MakeItem(string name, double width, double length, int qty = 1)
    {
        return new NestItem
        {
            Drawing = MakeDrawing(name, width, length),
            Quantity = qty,
        };
    }

    [Fact]
    public void SortByBoundingBoxArea_OrdersLargestFirst()
    {
        var items = new List<NestItem>
        {
            MakeItem("small", 10, 10),
            MakeItem("large", 40, 60),
            MakeItem("medium", 20, 30),
        };

        var sorted = MultiPlateNester.SortItems(items, PartSortOrder.BoundingBoxArea);

        Assert.Equal("large", sorted[0].Drawing.Name);
        Assert.Equal("medium", sorted[1].Drawing.Name);
        Assert.Equal("small", sorted[2].Drawing.Name);
    }

    [Fact]
    public void SortBySize_OrdersByLongestDimension()
    {
        var items = new List<NestItem>
        {
            MakeItem("short-wide", 50, 20),    // longest = 50
            MakeItem("tall-narrow", 10, 80),    // longest = 80
            MakeItem("square", 30, 30),         // longest = 30
        };

        var sorted = MultiPlateNester.SortItems(items, PartSortOrder.Size);

        Assert.Equal("tall-narrow", sorted[0].Drawing.Name);
        Assert.Equal("short-wide", sorted[1].Drawing.Name);
        Assert.Equal("square", sorted[2].Drawing.Name);
    }
}
