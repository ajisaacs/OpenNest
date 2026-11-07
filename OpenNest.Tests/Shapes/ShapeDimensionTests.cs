using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class ShapeDimensionTests
{
    public static TheoryData<Type> ShapeTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(ShapeDefinition).Assembly.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && typeof(ShapeDefinition).IsAssignableFrom(t)))
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(ShapeTypes))]
    public void EveryDimensionMeasuresTheParameterItShows(Type shapeType)
    {
        var shape = (ShapeDefinition)Activator.CreateInstance(shapeType)!;
        shape.SetPreviewDefaults();
        var dimensions = shape.GetDimensions();

        Assert.NotEmpty(dimensions);
        Assert.Equal(dimensions.Count, dimensions.Select(d => d.Property).Distinct().Count());
        foreach (var dimension in dimensions)
        {
            var property = shapeType.GetProperty(dimension.Property);
            Assert.NotNull(property);
            Assert.Equal(typeof(double), property!.PropertyType);
            if (dimension.Kind != ShapeDimensionKind.Leader)
                Assert.Equal((double)property.GetValue(shape)!, dimension.Measured, 6);
            if (dimension.CountProperty != null)
                Assert.Equal(typeof(int), shapeType.GetProperty(dimension.CountProperty)!.PropertyType);
        }
    }

    [Fact]
    public void FlangeHoleDiameterCarriesTheHoleCount()
    {
        var flange = new PipeFlangeShape();
        flange.SetPreviewDefaults();

        var holes = flange.GetDimensions().Single(d => d.Property == nameof(PipeFlangeShape.HoleDiameter));

        Assert.Equal(ShapeDimensionKind.Leader, holes.Kind);
        Assert.Equal(nameof(PipeFlangeShape.HoleCount), holes.CountProperty);
        Assert.Equal("holes", holes.CountNoun);
    }

    [Fact]
    public void RoundedRectangleRadiusLeaderStartsOnTheCornerArc()
    {
        var shape = new RoundedRectangleShape { Length = 12, Width = 6, Radius = 2 };
        var radius = shape.GetDimensions().Single(d => d.Property == nameof(RoundedRectangleShape.Radius));

        Assert.Equal(2, radius.Start.DistanceTo(new OpenNest.Geometry.Vector(2, 4)), 6);
    }

    [Theory]
    [InlineData(0.5, "1/2")]
    [InlineData(5.5, "5 1/2")]
    [InlineData(0.875, "7/8")]
    [InlineData(12, "12")]
    [InlineData(0.0625, "1/16")]
    [InlineData(0.1, "0.1")]
    [InlineData(-1.25, "-1 1/4")]
    public void FractionFormatsSixtyFourthsAndFallsBackToDecimals(double value, string expected)
    {
        Assert.Equal(expected, Fraction.Format(value));
        Assert.Equal(value, double.Parse(Fraction.ReplaceFractionsWithDecimals(Fraction.Format(value))), 6);
    }
}
