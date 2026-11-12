using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Geometry;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Tests.Data;

public class DrawingGeometrySummaryTests
{
    private static void Rectangle(CncProgram program, double x, double y, double width, double height)
    {
        program.Codes.Add(new RapidMove(new Vector(x, y)));
        program.Codes.Add(new LinearMove(new Vector(x, y + height)));
        program.Codes.Add(new LinearMove(new Vector(x + width, y + height)));
        program.Codes.Add(new LinearMove(new Vector(x + width, y)));
        program.Codes.Add(new LinearMove(new Vector(x, y)));
    }

    [Fact]
    public void Measure_ReportsTheBoundingSidesNetAreaAndHolesInInches()
    {
        var program = new CncProgram();
        Rectangle(program, 0, 0, 2, 5);
        Rectangle(program, 0.5, 0.5, 1, 1);
        Rectangle(program, 0.5, 3, 1, 1);

        var summary = DrawingGeometrySummary.Measure(new Drawing("Synthetic plate", program), Units.Inches);

        Assert.True(summary.IsMeasured);
        Assert.Equal((5.0, 2.0, 8.0, 2), (summary.Length, summary.Width, summary.Area, summary.Holes));
        Assert.Equal(DrawingGeometrySummary.CurrentVersion, summary.Version);
        Assert.Null(summary.GetValidationError());
    }

    [Fact]
    public void Measure_ConvertsMillimeterDrawingsToInches()
    {
        var program = new CncProgram();
        Rectangle(program, 0, 0, 50.8, 25.4);

        var summary = DrawingGeometrySummary.Measure(new Drawing("Synthetic metric", program), Units.Millimeters);

        Assert.Equal((2.0, 1.0, 2.0, 0), (summary.Length, summary.Width, summary.Area, summary.Holes));
    }

    [Fact]
    public void Measure_WithoutClosedCutGeometry_IsAFailedSummary()
    {
        var program = new CncProgram();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(3, 0)));

        var summary = DrawingGeometrySummary.Measure(new Drawing("Synthetic line", program), Units.Inches);

        Assert.False(summary.IsMeasured);
        Assert.Null(summary.GetValidationError());
    }

    [Fact]
    public void Failed_TrimsLongErrorsToTheStoredLimit()
    {
        var summary = DrawingGeometrySummary.Failed(new string('x', 500));

        Assert.Equal(DrawingGeometrySummary.MaxErrorLength, summary.Error.Length);
        Assert.Null(summary.GetValidationError());
    }
}
