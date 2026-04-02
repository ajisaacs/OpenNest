using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

public class PartLeadInTests
{
    private static Part MakeSquarePart()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing);
    }

    [Fact]
    public void ApplyLeadIns_SetsHasManualLeadIns()
    {
        var part = MakeSquarePart();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
            InternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));

        Assert.True(part.HasManualLeadIns);
    }

    [Fact]
    public void ApplyLeadIns_StoresCuttingParameters()
    {
        var part = MakeSquarePart();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
            InternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));

        Assert.Same(parameters, part.CuttingParameters);
    }

    [Fact]
    public void ApplyLeadIns_ProgramContainsLeadinCodes()
    {
        var part = MakeSquarePart();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));

        var hasLeadin = part.Program.Codes.OfType<LinearMove>().Any(m => m.Layer == LayerType.Leadin);
        Assert.True(hasLeadin);
    }

    [Fact]
    public void RemoveLeadIns_RestoresOriginalProgram()
    {
        var part = MakeSquarePart();
        var originalCodeCount = part.Program.Codes.Count;
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));
        part.RemoveLeadIns();

        Assert.False(part.HasManualLeadIns);
        Assert.Null(part.CuttingParameters);
        Assert.Equal(originalCodeCount, part.Program.Codes.Count);
    }

    [Fact]
    public void RemoveLeadIns_PreservesRotation()
    {
        var part = MakeSquarePart();
        part.Rotate(System.Math.PI / 4); // 45 degrees
        var rotation = part.Rotation;

        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));
        part.RemoveLeadIns();

        Assert.Equal(rotation, part.Rotation, 6);
    }

    [Fact]
    public void RemoveLeadIns_PreservesLocation()
    {
        var part = MakeSquarePart();
        part.Offset(20, 30);
        var location = part.Location;

        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        part.ApplyLeadIns(parameters, new Vector(-5, -5));
        part.RemoveLeadIns();

        Assert.Equal(location.X, part.Location.X, 6);
        Assert.Equal(location.Y, part.Location.Y, 6);
    }

    [Fact]
    public void LeadInsLocked_DefaultsFalse()
    {
        var part = MakeSquarePart();
        Assert.False(part.LeadInsLocked);
    }

    [Fact]
    public void ApplySingleLeadIn_SetsHasManualLeadIns()
    {
        var part = MakeSquarePart();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        part.ApplySingleLeadIn(parameters, new Vector(5, 0), entity, ContourType.External);

        Assert.True(part.HasManualLeadIns);
    }

    [Fact]
    public void ApplySingleLeadIn_ProgramContainsLeadinCodes()
    {
        var part = MakeSquarePart();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        part.ApplySingleLeadIn(parameters, new Vector(5, 0), entity, ContourType.External);

        var hasLeadin = part.Program.Codes.OfType<LinearMove>().Any(m => m.Layer == LayerType.Leadin);
        Assert.True(hasLeadin);
    }

    [Fact]
    public void ApplySingleLeadIn_ThenRemove_RestoresOriginal()
    {
        var part = MakeSquarePart();
        var originalCodeCount = part.Program.Codes.Count;
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        part.ApplySingleLeadIn(parameters, new Vector(5, 0), entity, ContourType.External);
        part.RemoveLeadIns();

        Assert.False(part.HasManualLeadIns);
        Assert.Equal(originalCodeCount, part.Program.Codes.Count);
    }

    [Fact]
    public void ApplySingleLeadIn_PreservesRotation()
    {
        var part = MakeSquarePart();
        part.Rotate(System.Math.PI / 4);
        var rotation = part.Rotation;

        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        // After rotation, the edges change. Use a point on the rotated bottom edge.
        // The rotated square has vertices roughly at rotated positions.
        // We'll use a generic entity that will be matched via fallback.
        var entity = new Line(new Vector(10, 0), new Vector(0, 0));
        part.ApplySingleLeadIn(parameters, new Vector(5, 0), entity, ContourType.External);

        Assert.Equal(rotation, part.Rotation, 6);
    }
}
