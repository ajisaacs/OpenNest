using OpenNest.Geometry;
using OpenNest.Labels;

namespace OpenNest.Tests.Labels;

public class LabelBoxRotationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(90, 90)]
    [InlineData(-90, 90)]
    [InlineData(120, -60)]
    [InlineData(180, 0)]
    [InlineData(-135, 45)]
    [InlineData(270, 90)]
    [InlineData(405, 45)]
    public void ReadableRotation_KeepsTextFromReadingUpsideDown(double degrees, double expected)
    {
        var turned = LabelBox.ReadableRotation(degrees * System.Math.PI / 180);
        Assert.Equal(expected, turned * 180 / System.Math.PI, 9);
    }

    [Fact]
    public void WithRotation_CoversTheSameRectangleAsTheHalfTurn()
    {
        var box = new LabelBox(new Vector(3, 4), 2, 0.5, 0);
        var turned = box.WithRotation(System.Math.PI * 1.25);

        Assert.Equal(System.Math.PI / 4, turned.Rotation, 9);
        Assert.Equal(box.Center, turned.Center);
        var halfTurn = new LabelBox(box.Center, 2, 0.5, System.Math.PI * 1.25).Corners();
        foreach (var corner in turned.Corners())
            Assert.Contains(halfTurn, c => c.DistanceTo(corner) < 1e-9);
    }

    [Fact]
    public void ReadableRotation_PassesNonfiniteThrough() =>
        Assert.True(double.IsNaN(LabelBox.ReadableRotation(double.NaN)));
}
