using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests;

/// <summary>
/// The grain belongs to the sheet: turning the plate turns its grain, and grain angles are
/// lines, so angles π apart are the same grain.
/// </summary>
public class PlateGrainTests
{
    [Theory]
    [InlineData(RotationType.CW)]
    [InlineData(RotationType.CCW)]
    public void Rotate90_TurnsTheGrainWithTheSheet(RotationType direction)
    {
        // A 60 x 120 sheet whose grain runs side to side along its 120" length.
        var plate = new Plate(new Size(60, 120)) { GrainAngle = 0 };

        plate.Rotate90(direction);

        Assert.Equal(new Size(120, 60), plate.Size);
        Assert.Equal(Angle.HalfPI, plate.GrainAngle, 9);

        plate.Rotate90(direction);

        Assert.Equal(0, plate.GrainAngle, 9);
    }

    [Fact]
    public void Rotate90_TurnsAnAngledGrainByAQuarterTurn()
    {
        var plate = new Plate(new Size(60, 120)) { GrainAngle = Angle.ToRadians(30) };

        plate.Rotate90(RotationType.CCW);

        Assert.Equal(Angle.ToRadians(120), plate.GrainAngle, 9);
    }

    [Fact]
    public void Rotate180_LeavesTheGrainLineAlone()
    {
        var plate = new Plate(new Size(60, 120)) { GrainAngle = Angle.ToRadians(30) };

        plate.Rotate180();

        Assert.Equal(Angle.ToRadians(30), plate.GrainAngle, 9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(180, 0)]
    [InlineData(-90, 90)]
    [InlineData(270, 90)]
    [InlineData(210, 30)]
    public void NormalizeGrainAngle_KeepsTheLineInZeroToPi(double degrees, double expected)
    {
        Assert.Equal(Angle.ToRadians(expected), Plate.NormalizeGrainAngle(Angle.ToRadians(degrees)), 9);
    }
}
