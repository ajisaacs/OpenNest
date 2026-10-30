using OpenNest.Dimensions;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

public class DimensionPersistenceTests
{
    private static Drawing WithDimensions()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Dimensions.Add(new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = -1.25 });
        drawing.Dimensions.Add(new DrawingDimension { Kind = DimensionKind.OverallVertical, Offset = 0.75 });
        return drawing;
    }

    [Fact]
    public void Dimensions_RoundTripEveryField()
    {
        var (restored, warnings) = RoundTrip(Write(WithDimensions()));

        Assert.Empty(warnings);
        var dimensions = Assert.Single(restored.Drawings).Dimensions;
        Assert.Equal(2, dimensions.Count);
        Assert.Equal(DimensionKind.OverallHorizontal, dimensions[0].Kind);
        Assert.Equal(-1.25, dimensions[0].Offset);
        Assert.Equal(DimensionKind.OverallVertical, dimensions[1].Kind);
        Assert.Equal(0.75, dimensions[1].Offset);
    }

    [Fact]
    public void NoDimensions_AreOmittedFromTheFile()
    {
        var archive = Write(DrawingPackageFixture.Build());

        Assert.False(NestJson(archive)["drawings"]![0]!.AsObject().ContainsKey("dimensions"));
        Assert.Empty(Assert.Single(RoundTrip(archive).Nest.Drawings).Dimensions);
    }

    [Theory]
    [InlineData("kind", "Radial")]
    [InlineData("kind", "0")]
    [InlineData("offset", 0.0)]
    public void UnreadableDimension_IsDroppedWithAWarning_AndTheRestLoad(string field, object value)
    {
        var archive = Write(WithDimensions());
        var json = NestJson(archive);
        json["drawings"]![0]!["dimensions"]![0]![field] = value is string text ? text : (double)value;

        var (restored, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        var dimension = Assert.Single(Assert.Single(restored.Drawings).Dimensions);
        Assert.Equal(DimensionKind.OverallVertical, dimension.Kind);
        Assert.Contains(warnings, w => w.Contains("dimension 1 could not be read"));
    }

    private static byte[] Write(Drawing drawing)
    {
        var nest = new Nest(drawing.Name) { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }
}
