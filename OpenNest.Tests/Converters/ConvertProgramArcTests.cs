using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Converters;

public class ConvertProgramArcTests
{
    [Fact]
    public void ArcWithCenterNotEquidistant_StartsAtPreviousEndpoint()
    {
        // PEP-exported notch: I0.03 on a 0.0598 chord puts the center 0.0300 from
        // the start but 0.0298 from the end.
        var pgm = new Program(Mode.Incremental);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(0, -0.3573));
        pgm.Codes.Add(new ArcMove(0.0598, 0, 0.03, 0, RotationType.CCW));
        pgm.Codes.Add(new LinearMove(0, 0.3573));

        var arc = ConvertProgram.ToGeometry(pgm).OfType<Arc>().Single();

        Assert.True(arc.StartPoint().DistanceTo(new Vector(0, -0.3573)) < 1e-9);
        Assert.True(arc.EndPoint().DistanceTo(new Vector(0.0598, -0.3573)) < 1e-9);
        Assert.Equal(0.0299, arc.Radius, 9);
    }

    [Fact]
    public void ClosedContourWithInconsistentArc_ChainsIntoSinglePerimeter()
    {
        var pgm = new Program(Mode.Incremental);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(4, 0));
        pgm.Codes.Add(new LinearMove(0, 2));
        pgm.Codes.Add(new LinearMove(-1.9701, 0));
        pgm.Codes.Add(new LinearMove(0, -0.5));
        pgm.Codes.Add(new ArcMove(-0.0598, 0, -0.03, 0, RotationType.CW));
        pgm.Codes.Add(new LinearMove(0, 0.5));
        pgm.Codes.Add(new LinearMove(-1.9701, 0));
        pgm.Codes.Add(new LinearMove(0, -2));

        var entities = ConvertProgram.ToGeometry(pgm)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .ToList();
        var profile = new ShapeProfile(entities);

        Assert.Empty(profile.Cutouts);
        Assert.Equal(entities.Count, profile.Perimeter.Entities.Count);
    }

    [Fact]
    public void ConsistentArc_IsUnchanged()
    {
        var pgm = new Program(Mode.Incremental);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new ArcMove(2, 0, 1, 0, RotationType.CCW));

        var arc = ConvertProgram.ToGeometry(pgm).OfType<Arc>().Single();

        Assert.Equal(1.0, arc.Center.X, 12);
        Assert.Equal(0.0, arc.Center.Y, 12);
        Assert.Equal(1.0, arc.Radius, 12);
    }
}
