using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Bending;

public class BendGrainTests
{
    private static double Rad(double degrees) => Angle.ToRadians(degrees);

    // Grain angle 0 runs along X, so a bend along Y (90°) is across it.
    [Theory]
    [InlineData(90, BendGrainKind.AcrossGrain)]
    [InlineData(270, BendGrainKind.AcrossGrain)] // direction of the line does not matter
    [InlineData(-90, BendGrainKind.AcrossGrain)]
    [InlineData(0, BendGrainKind.WithGrain)]
    [InlineData(180, BendGrainKind.WithGrain)]
    [InlineData(360, BendGrainKind.WithGrain)]
    [InlineData(45, BendGrainKind.Other)]
    [InlineData(135, BendGrainKind.Other)]
    [InlineData(30, BendGrainKind.Other)]
    public void Classify_UsesTheAngleToTheGrainModuloAHalfTurn(double bendDegrees, BendGrainKind expected)
    {
        Assert.Equal(expected, BendGrain.Classify(Rad(bendDegrees), grainAngle: 0));
    }

    [Theory]
    [InlineData(85, BendGrainKind.AcrossGrain)] // exactly on the 5° tolerance
    [InlineData(95, BendGrainKind.AcrossGrain)]
    [InlineData(87, BendGrainKind.AcrossGrain)]
    [InlineData(84.9, BendGrainKind.Other)]
    [InlineData(95.1, BendGrainKind.Other)]
    [InlineData(5, BendGrainKind.WithGrain)] // exactly on the 5° tolerance
    [InlineData(-5, BendGrainKind.WithGrain)]
    [InlineData(5.1, BendGrainKind.Other)]
    [InlineData(174.9, BendGrainKind.Other)]
    [InlineData(175, BendGrainKind.WithGrain)]
    public void Classify_HonorsTheFiveDegreeTolerance(double bendDegrees, BendGrainKind expected)
    {
        Assert.Equal(expected, BendGrain.Classify(Rad(bendDegrees), grainAngle: 0));
    }

    [Theory]
    [InlineData(0, 90, 0, BendGrainKind.AcrossGrain)]
    [InlineData(90, 90, 0, BendGrainKind.WithGrain)] // part turned a quarter, bend now with the grain
    [InlineData(180, 90, 0, BendGrainKind.AcrossGrain)] // a half turn changes nothing
    [InlineData(0, 0, 90, BendGrainKind.AcrossGrain)] // grain along Y
    [InlineData(90, 0, 90, BendGrainKind.WithGrain)] // bend along X, grain along Y
    [InlineData(45, 90, 45, BendGrainKind.AcrossGrain)] // bend lands at 135°, grain at 45°
    [InlineData(45, 0, 45, BendGrainKind.WithGrain)] // bend lands on the 45° grain
    public void Classify_IsRotationAndGrainAware(
        double rotationDegrees, double bendDegrees, double grainDegrees, BendGrainKind expected)
    {
        var angle = Rad(bendDegrees);
        var bend = new Bend
        {
            StartPoint = new Vector(0, 0),
            EndPoint = new Vector(10 * System.Math.Cos(angle), 10 * System.Math.Sin(angle)),
        };

        Assert.Equal(expected, BendGrain.Classify(bend, Rad(rotationDegrees), Rad(grainDegrees)));
    }

    [Fact]
    public void Classify_IsTheSameWhenEitherLineIsTurnedAHalfTurn()
    {
        for (var degrees = -360; degrees <= 360; degrees += 7)
        {
            var angle = Rad(degrees);
            Assert.Equal(
                BendGrain.Classify(angle, 0),
                BendGrain.Classify(angle + System.Math.PI, 0));
            Assert.Equal(
                BendGrain.Classify(angle, Rad(30)),
                BendGrain.Classify(angle, Rad(30) + System.Math.PI));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(30, 30)]
    [InlineData(120, 60)]
    [InlineData(135, 45)]
    [InlineData(180, 0)]
    public void AngleToGrain_IsTheAcuteAngleBetweenTheLines(double bendDegrees, double expectedDegrees)
    {
        Assert.Equal(expectedDegrees, Angle.ToDegrees(BendGrain.AngleToGrain(Rad(bendDegrees), 0)), 9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 6)]
    [InlineData(15, 1)]
    [InlineData(30, 2)]
    [InlineData(45, 3)]
    [InlineData(150, 10)]
    [InlineData(165, 11)]
    [InlineData(180, 0)]
    [InlineData(-30, 10)]
    public void Bucket_NumbersTheDirectionInFifteenDegreeSteps(double bendDegrees, int expected)
    {
        Assert.Equal(expected, BendGrain.Bucket(Rad(bendDegrees), 0));
    }

    [Fact]
    public void Bucket_KeepsTheSideOfTheGrain()
    {
        Assert.NotEqual(BendGrain.Bucket(Rad(30), 0), BendGrain.Bucket(Rad(-30), 0));
    }

    [Fact]
    public void Bucket_ReservesZeroAndSixForWithAndAcrossTheGrain()
    {
        for (var degrees = -360.0; degrees <= 360; degrees += 0.5)
        {
            var angle = Rad(degrees);
            var bucket = BendGrain.Bucket(angle, 0);
            var kind = BendGrain.Classify(angle, 0);

            Assert.InRange(bucket, 0, BendGrain.BucketCount - 1);
            Assert.Equal(kind == BendGrainKind.WithGrain, bucket == 0);
            Assert.Equal(kind == BendGrainKind.AcrossGrain, bucket == BendGrain.BucketCount / 2);
        }
    }
}
