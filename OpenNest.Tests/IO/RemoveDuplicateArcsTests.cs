using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.IO;

public class RemoveDuplicateArcsTests
{
    [Fact]
    public void RemoveDuplicateArcs_RemovesArcMatchingCircle_SameLayer()
    {
        var layer = new Layer("0");
        var circle = new Circle(10, 10, 5) { Layer = layer };
        var arc = new Arc(10, 10, 5, 0, Angle.ToRadians(90)) { Layer = layer };
        var line = new Line(0, 0, 10, 0) { Layer = layer };
        var entities = new List<Entity> { circle, arc, line };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Equal(2, entities.Count);
        Assert.Contains(circle, entities);
        Assert.Contains(line, entities);
        Assert.DoesNotContain(arc, entities);
    }

    [Fact]
    public void RemoveDuplicateArcs_KeepsArcOnDifferentLayer()
    {
        var layer1 = new Layer("cut");
        var layer2 = new Layer("etch");
        var circle = new Circle(10, 10, 5) { Layer = layer1 };
        var arc = new Arc(10, 10, 5, 0, Angle.ToRadians(90)) { Layer = layer2 };
        var entities = new List<Entity> { circle, arc };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Equal(2, entities.Count);
        Assert.Contains(arc, entities);
    }

    [Fact]
    public void RemoveDuplicateArcs_KeepsArcWithDifferentRadius()
    {
        var layer = new Layer("0");
        var circle = new Circle(10, 10, 5) { Layer = layer };
        var arc = new Arc(10, 10, 3, 0, Angle.ToRadians(90)) { Layer = layer };
        var entities = new List<Entity> { circle, arc };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void RemoveDuplicateArcs_KeepsArcWithDifferentCenter()
    {
        var layer = new Layer("0");
        var circle = new Circle(10, 10, 5) { Layer = layer };
        var arc = new Arc(20, 20, 5, 0, Angle.ToRadians(90)) { Layer = layer };
        var entities = new List<Entity> { circle, arc };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void RemoveDuplicateArcs_NoCircles_NoChange()
    {
        var arc = new Arc(10, 10, 5, 0, Angle.ToRadians(90));
        var line = new Line(0, 0, 10, 0);
        var entities = new List<Entity> { arc, line };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void RemoveDuplicateArcs_MultipleArcsMatchOneCircle_RemovesAll()
    {
        var layer = new Layer("0");
        var circle = new Circle(10, 10, 5) { Layer = layer };
        var arc1 = new Arc(10, 10, 5, 0, Angle.ToRadians(90)) { Layer = layer };
        var arc2 = new Arc(10, 10, 5, Angle.ToRadians(90), Angle.ToRadians(180)) { Layer = layer };
        var entities = new List<Entity> { circle, arc1, arc2 };

        CadImporter.RemoveDuplicateArcs(entities);

        Assert.Single(entities);
        Assert.Contains(circle, entities);
    }
}
