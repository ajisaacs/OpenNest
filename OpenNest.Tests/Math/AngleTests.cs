using Xunit;

namespace OpenNest.Tests.Math;

public class AngleTests
{
    [Theory]
    [InlineData(10, 0, 90, true)]
    [InlineData(180, 0, 90, false)]
    [InlineData(0, 0, 90, true)]
    [InlineData(90, 0, 90, true)]
    [InlineData(350, 340, 10, true)]
    [InlineData(20, 340, 10, false)]
    [InlineData(-10, 340, 10, true)]
    [InlineData(370, 0, 90, true)]
    [InlineData(123, 45, 45, true)]
    public void IsBetweenDeg_ReturnsExpected(double angle, double a1, double a2, bool expected)
    {
        Assert.Equal(expected, OpenNest.Math.Angle.IsBetweenDeg(angle, a1, a2));
    }

    [Theory]
    [InlineData(45, 90, 0, true)]
    [InlineData(45, 0, 90, false)]
    public void IsBetweenDeg_Reversed_SwapsEndpoints(double angle, double a1, double a2, bool expected)
    {
        Assert.Equal(expected, OpenNest.Math.Angle.IsBetweenDeg(angle, a1, a2, reversed: true));
    }

    [Theory]
    [InlineData(10, 0, 90, false)]
    [InlineData(45, 0, 90, false)]
    [InlineData(180, 0, 90, false)]
    [InlineData(0, 0, 90, false)]
    [InlineData(90, 0, 90, false)]
    [InlineData(350, 340, 10, false)]
    [InlineData(20, 340, 10, false)]
    [InlineData(-10, 340, 10, false)]
    [InlineData(370, 0, 90, false)]
    [InlineData(123, 45, 45, false)]
    [InlineData(45, 90, 0, true)]
    [InlineData(45, 0, 90, true)]
    public void IsBetweenDeg_OrdinaryCases_MatchesRadians(
        double angle, double a1, double a2, bool reversed)
    {
        var radianResult = OpenNest.Math.Angle.IsBetweenRad(
            angle * System.Math.PI / 180,
            a1 * System.Math.PI / 180,
            a2 * System.Math.PI / 180,
            reversed);

        Assert.Equal(radianResult, OpenNest.Math.Angle.IsBetweenDeg(angle, a1, a2, reversed));
    }

    [Theory]
    [InlineData(-0.000005, 0, 90, true)]
    [InlineData(-0.00002, 0, 90, false)]
    [InlineData(90.000005, 0, 90, true)]
    [InlineData(90.00002, 0, 90, false)]
    [InlineData(123, 45, 45.000005, true)]
    [InlineData(123, 45, 45.00002, false)]
    [InlineData(123, 45, 405, false)]
    public void IsBetweenDeg_UsesDegreeToleranceBeforeNormalizingEndpoints(
        double angle, double a1, double a2, bool expected)
    {
        Assert.Equal(expected, OpenNest.Math.Angle.IsBetweenDeg(angle, a1, a2));
    }

    [Theory]
    [InlineData(-0.000005, 0, 1, true)]
    [InlineData(-0.00002, 0, 1, false)]
    [InlineData(1.000005, 0, 1, true)]
    [InlineData(1.00002, 0, 1, false)]
    [InlineData(2, 0.5, 0.500005, true)]
    [InlineData(2, 0.5, 0.50002, false)]
    [InlineData(2, 0.5, 0.5 + 2 * System.Math.PI, false)]
    public void IsBetweenRad_UsesRadianToleranceBeforeNormalizingEndpoints(
        double angle, double a1, double a2, bool expected)
    {
        Assert.Equal(expected, OpenNest.Math.Angle.IsBetweenRad(angle, a1, a2));
    }
}
