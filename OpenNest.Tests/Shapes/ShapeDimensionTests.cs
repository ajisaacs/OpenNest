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

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void FlangeHoleLeaderCrossesAHoleThroughItsCenter(int holeCount)
    {
        var flange = new PipeFlangeShape { OD = 7.5, HoleDiameter = 0.875, HolePatternDiameter = 5.5, HoleCount = holeCount };

        var holes = flange.GetDimensions().Single(d => d.Property == nameof(PipeFlangeShape.HoleDiameter));

        Assert.NotNull(holes.HoleCenter);
        var center = holes.HoleCenter!.Value;
        Assert.Equal(5.5 / 2, center.DistanceTo(new OpenNest.Geometry.Vector(0, 0)), 6);
        Assert.Equal(0.875 / 2, holes.HoleRadius, 6);
        Assert.Equal(holes.HoleRadius, holes.Start.DistanceTo(center), 6);
        Assert.Equal(nameof(PipeFlangeShape.HoleCount), holes.CountProperty);
    }

    [Fact]
    public void FlangeHolePatternMeasuresADottedBoltCircle()
    {
        var flange = new PipeFlangeShape();
        flange.SetPreviewDefaults();

        var pattern = flange.GetDimensions().Single(d => d.Property == nameof(PipeFlangeShape.HolePatternDiameter));

        Assert.True(pattern.DrawsCircle);
        Assert.Equal(ShapeDimensionKind.Leader, pattern.Kind);
        Assert.Equal(flange.HolePatternDiameter / 2, pattern.CircleRadius, 6);
        Assert.Equal(pattern.CircleRadius, pattern.Start.DistanceTo(pattern.CircleCenter!.Value), 6);
        // The leader runs out from the circle, so it never crosses the flange.
        Assert.Equal(pattern.Start.X / pattern.CircleRadius, pattern.Side.X, 6);
        Assert.Equal(pattern.Start.Y / pattern.CircleRadius, pattern.Side.Y, 6);
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
