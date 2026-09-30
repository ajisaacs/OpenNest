using System.Drawing;
using OpenNest.Engine.CirclePacking;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.CirclePacking;

public class ItemCloneTests
{
    [Fact]
    public void Clone_FromItemReference_ReturnsItem()
    {
        var item = CreateItem();

        var clone = item.Clone();

        Assert.IsType<Item>(clone);
        Assert.NotSame(item, clone);
    }

    [Fact]
    public void Clone_FromCircleReference_ReturnsItem()
    {
        var item = CreateItem();
        var circle = (Circle)item;

        var clone = circle.Clone();

        Assert.IsType<Item>(clone);
        Assert.NotSame(item, clone);
    }

    [Fact]
    public void Clone_FromEntityReference_ReturnsItem()
    {
        var item = CreateItem();
        var entity = (Entity)item;

        var clone = entity.Clone();

        Assert.IsType<Item>(clone);
        Assert.NotSame(item, clone);
    }

    [Fact]
    public void Clone_PreservesPackingIdAndCreatesNewGeometryId()
    {
        var item = CreateItem();

        var clone = Assert.IsType<Item>(item.Clone());

        Assert.Equal(item.PackingId, clone.PackingId);
        Assert.NotEqual(Guid.Empty, ((Entity)clone).Id);
        Assert.NotEqual(((Entity)item).Id, ((Entity)clone).Id);
    }

    [Fact]
    public void Clone_PreservesRotation()
    {
        var item = CreateItem();

        var clone = Assert.IsType<Item>(item.Clone());

        Assert.Equal(item.Rotation, clone.Rotation);
    }

    [Fact]
    public void Clone_PreservesEntityMetadata()
    {
        var item = CreateItem();

        var clone = Assert.IsType<Item>(item.Clone());

        Assert.Equal(item.Color, clone.Color);
        Assert.Same(item.Layer, clone.Layer);
        Assert.Equal(item.LineTypeName, clone.LineTypeName);
        Assert.Equal(item.IsVisible, clone.IsVisible);
        Assert.Equal(item.Tag, clone.Tag);
    }

    [Fact]
    public void Clone_HasIndependentGeometryAndBounds()
    {
        var item = CreateItem();
        var sourceCenter = item.Center;
        var sourceRadius = item.Radius;
        var sourceBounds = (item.Left, item.Bottom, item.Right, item.Top);

        var clone = Assert.IsType<Item>(item.Clone());

        Assert.Equal(sourceCenter, clone.Center);
        Assert.Equal(sourceRadius, clone.Radius);
        Assert.Equal(sourceBounds, (clone.Left, clone.Bottom, clone.Right, clone.Top));
        Assert.NotSame(item.BoundingBox, clone.BoundingBox);

        var cloneCenter = clone.Center;
        cloneCenter.X += 30;
        cloneCenter.Y -= 10;
        clone.Center = cloneCenter;

        Assert.Equal(sourceCenter, item.Center);
        Assert.Equal(sourceBounds, (item.Left, item.Bottom, item.Right, item.Top));
        Assert.Equal(cloneCenter, clone.Center);
        Assert.Equal(sourceRadius, clone.Radius);
        Assert.NotEqual(sourceBounds, (clone.Left, clone.Bottom, clone.Right, clone.Top));
        Assert.Equal(
            (cloneCenter.X - sourceRadius, cloneCenter.Y - sourceRadius,
                cloneCenter.X + sourceRadius, cloneCenter.Y + sourceRadius),
            (clone.Left, clone.Bottom, clone.Right, clone.Top));

        clone.Radius += 2;

        Assert.Equal(sourceRadius, item.Radius);
        Assert.Equal(sourceBounds, (item.Left, item.Bottom, item.Right, item.Top));
        Assert.Equal(
            (cloneCenter.X - clone.Radius, cloneCenter.Y - clone.Radius,
                cloneCenter.X + clone.Radius, cloneCenter.Y + clone.Radius),
            (clone.Left, clone.Bottom, clone.Right, clone.Top));
    }

    [Fact]
    public void Clone_Twice_CreatesDistinctGeometryIds()
    {
        var item = CreateItem();

        var first = (Entity)Assert.IsType<Item>(item.Clone());
        var second = (Entity)Assert.IsType<Item>(item.Clone());

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(((Entity)item).Id, first.Id);
        Assert.NotEqual(((Entity)item).Id, second.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static Item CreateItem() => new Item
    {
        PackingId = 42,
        Radius = 5,
        Center = new Vector(10, 20),
        Rotation = RotationType.CCW,
        Color = Color.CornflowerBlue,
        Layer = new Layer("packing"),
        LineTypeName = "Dashed",
        IsVisible = false,
        Tag = "packing-item",
    };
}
