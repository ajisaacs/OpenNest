using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class EllipseArgumentValidationTests
{
    [Theory]
    [InlineData(0.0, 5.0, "semiMajor", 0.0)]
    [InlineData(-3.0, 5.0, "semiMajor", -3.0)]
    [InlineData(10.0, 0.0, "semiMinor", 0.0)]
    [InlineData(10.0, -2.0, "semiMinor", -2.0)]
    [InlineData(0.0, 0.0, "semiMajor", 0.0)]
    [InlineData(0.0, -2.0, "semiMajor", 0.0)]
    [InlineData(-3.0, 0.0, "semiMajor", -3.0)]
    [InlineData(-3.0, -2.0, "semiMajor", -3.0)]
    public void Convert_NonpositiveAxes_IdentifiesFirstInvalidAxis(
        double semiMajor,
        double semiMinor,
        string expectedName,
        double expectedValue
    )
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EllipseConverter.Convert(new Vector(0, 0), semiMajor, semiMinor, 0, 0, Angle.TwoPI)
        );

        Assert.Equal(expectedName, error.ParamName);
        Assert.Equal(expectedValue, Assert.IsType<double>(error.ActualValue));
        Assert.Contains("positive", error.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    public void Convert_InvalidToleranceAndAxes_ValidatesToleranceFirst(double tolerance)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EllipseConverter.Convert(new Vector(0, 0), -3, -2, 0, 0, Angle.TwoPI, tolerance)
        );

        Assert.Equal(nameof(tolerance), error.ParamName);
        Assert.Null(error.ActualValue);
        Assert.Contains("Tolerance must be positive.", error.Message);
    }
}
