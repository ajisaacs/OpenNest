using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

public class ApplySingleTests
{
    private static Program MakeSquareProgram()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return pgm;
    }

    private static Program MakeSquareWithHoleProgram()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        pgm.Codes.Add(new RapidMove(new Vector(12, 10)));
        pgm.Codes.Add(new ArcMove(new Vector(12, 10), new Vector(10, 10), RotationType.CW));
        return pgm;
    }

    private static List<ICode> GetLeadInCodes(Program program)
    {
        var result = new List<ICode>();
        foreach (var code in program.Codes)
        {
            if (code is LinearMove lm && lm.Layer == LayerType.Leadin)
                result.Add(lm);
            else if (code is ArcMove am && am.Layer == LayerType.Leadin)
                result.Add(am);
        }
        return result;
    }

    [Fact]
    public void ApplySingle_ExternalContour_PlacesLeadInAtExactPoint()
    {
        var pgm = MakeSquareProgram();
        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
            }
        };

        var clickPoint = new Vector(5, 0);
        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        var result = strategy.ApplySingle(pgm, clickPoint, entity, ContourType.External);

        var hasLeadin = result.Program.Codes.OfType<LinearMove>().Any(m => m.Layer == LayerType.Leadin);
        Assert.True(hasLeadin);
    }

    [Fact]
    public void ApplySingle_ContourStartsAtClickPoint()
    {
        var pgm = MakeSquareProgram();
        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
            }
        };

        var clickPoint = new Vector(5, 0);
        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        var result = strategy.ApplySingle(pgm, clickPoint, entity, ContourType.External);

        // Convert back to absolute to check positions
        result.Program.Mode = Mode.Absolute;

        var firstLinear = result.Program.Codes.OfType<LinearMove>()
            .First(m => m.Layer == LayerType.Leadin);
        Assert.Equal(clickPoint.X, firstLinear.EndPoint.X, 4);
        Assert.Equal(clickPoint.Y, firstLinear.EndPoint.Y, 4);
    }

    [Fact]
    public void ApplySingle_ProgramModeIsIncremental()
    {
        var pgm = MakeSquareProgram();
        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
            }
        };

        var clickPoint = new Vector(5, 0);
        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        var result = strategy.ApplySingle(pgm, clickPoint, entity, ContourType.External);

        Assert.Equal(Mode.Incremental, result.Program.Mode);
    }

    [Fact]
    public void ApplySingle_OnlyTargetContourGetsLeadIn()
    {
        var pgm = MakeSquareWithHoleProgram();
        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
                InternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 }
            }
        };

        var clickPoint = new Vector(10, 0);
        var entity = new Line(new Vector(20, 0), new Vector(0, 0));
        var result = strategy.ApplySingle(pgm, clickPoint, entity, ContourType.External);

        var leadinMoves = GetLeadInCodes(result.Program);
        Assert.Single(leadinMoves);
    }
}
