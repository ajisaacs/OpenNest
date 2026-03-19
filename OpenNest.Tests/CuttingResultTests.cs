using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class CuttingResultTests
{
    [Fact]
    public void CuttingResult_StoresValues()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(1, 2)));
        var point = new Vector(3, 4);

        var result = new CuttingResult { Program = pgm, LastCutPoint = point };

        Assert.Same(pgm, result.Program);
        Assert.Equal(3, result.LastCutPoint.X);
        Assert.Equal(4, result.LastCutPoint.Y);
    }
}
