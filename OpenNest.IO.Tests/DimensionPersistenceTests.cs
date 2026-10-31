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

    private static Drawing WithRadial()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Dimensions.Add(new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = -1.25 });
        drawing.Dimensions.Add(new DrawingDimension
        {
            Kind = DimensionKind.Diameter,
            EntityId = RadialId,
            Angle = 0.75,
            Offset = 0.5,
        });
        return drawing;
    }

    private static readonly Guid RadialId = Guid.Parse("6d6f0d55-2f5b-4e83-9d1e-5a0b8f1c2a11");

    [Fact]
    public void RadialDimensions_RoundTripTheirCurveAndAngle_AndOverallOnesOmitThem()
    {
        var archive = Write(WithRadial());
        var json = NestJson(archive)["drawings"]![0]!["dimensions"]!;

        Assert.False(json[0]!.AsObject().ContainsKey("entityId"));
        Assert.False(json[0]!.AsObject().ContainsKey("angle"));

        var (restored, warnings) = RoundTrip(archive);
        Assert.Empty(warnings);
        var radial = Assert.Single(restored.Drawings).Dimensions[1];
        Assert.Equal(DimensionKind.Diameter, radial.Kind);
        Assert.Equal(RadialId, radial.EntityId);
        Assert.Equal(0.75, radial.Angle);
        Assert.Equal(0.5, radial.Offset);
    }

    [Theory]
    [InlineData("entityId")]
    [InlineData("angle")]
    public void RadialDimensionWithoutItsCurveOrAngle_IsDroppedWithAWarning(string field)
    {
        var archive = Write(WithRadial());
        var json = NestJson(archive);
        json["drawings"]![0]!["dimensions"]![1]!.AsObject().Remove(field);

        var (restored, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        var dimension = Assert.Single(Assert.Single(restored.Drawings).Dimensions);
        Assert.Equal(DimensionKind.OverallHorizontal, dimension.Kind);
        Assert.Contains(warnings, w => w.Contains("dimension 2 could not be read"));
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
