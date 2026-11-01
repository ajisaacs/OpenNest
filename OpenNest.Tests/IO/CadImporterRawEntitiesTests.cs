using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;
using CadArc = ACadSharp.Entities.Arc;
using CadCircle = ACadSharp.Entities.Circle;
using CadLayer = ACadSharp.Tables.Layer;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.Tests.IO;

/// <summary>
/// A 4 x 6 plate whose bottom edge is two collinear lines, with one hole drawn the way CAM
/// exports do (two overlapping arcs and a seam chord) and one ETCH mark.
/// </summary>
public class CadImporterRawEntitiesTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("opennest-raw-").FullName;

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Dxf_RawEntitiesHoldTheFile_WhileEntitiesHoldTheCleanedGeometry()
    {
        var result = CadImporter.Import(WritePlate());

        Assert.Empty(result.RawEntities.OfType<Circle>());
        Assert.Equal(2, result.RawEntities.OfType<Arc>().Count());
        Assert.Equal(7, result.RawEntities.OfType<Line>().Count());
        Assert.Contains(result.RawEntities, e => e.Layer.Name == "ETCH");

        Assert.Single(result.Entities.OfType<Circle>());
        Assert.Empty(result.Entities.OfType<Arc>());
        Assert.Equal(4, result.Entities.OfType<Line>().Count());
    }

    [Fact]
    public void RawEntities_ShareNoInstancesWithTheWorkingEntities()
    {
        var result = CadImporter.Import(WritePlate());

        Assert.Empty(result.RawEntities.Intersect(result.Entities));
        Assert.Empty(result.RawEntities.Select(e => e.Layer).Intersect(result.Entities.Select(e => e.Layer)));
    }

    [Fact]
    public void RawEntities_DoNotDisturbFileIds()
    {
        var path = WritePlate();

        var first = CadImporter.Import(path);
        var second = CadImporter.Import(path);

        Assert.Equal(first.Entities.Select(e => e.Id), second.Entities.Select(e => e.Id));
        Assert.Empty(first.RawEntities.Select(e => e.Id).Intersect(first.Entities.Select(e => e.Id)));
    }

    [Fact]
    public void Svg_RawEntitiesKeepWhatTheOptimizerMerges()
    {
        var path = Path.Combine(directory, "plate.svg");
        File.WriteAllText(
            path,
            "<svg xmlns='http://www.w3.org/2000/svg' width='4in' height='4in' viewBox='0 0 4 4'>"
                + "<line x1='0' y1='0' x2='2' y2='0'/><line x1='2' y1='0' x2='4' y2='0'/>"
                + "<circle cx='2' cy='2' r='1'/><circle cx='2' cy='2' r='1'/></svg>"
        );

        var result = CadImporter.Import(path);

        Assert.Equal(2, result.RawEntities.OfType<Line>().Count());
        Assert.Equal(2, result.RawEntities.OfType<Circle>().Count());
        Assert.Single(result.Entities.OfType<Line>());
        Assert.Single(result.Entities.OfType<Circle>());
        Assert.Empty(result.RawEntities.Intersect(result.Entities));
    }

    private string WritePlate()
    {
        var layer = new CadLayer("0");
        var etch = new CadLayer("ETCH");
        var document = new CadDocument();
        document.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Inches;

        void Add(double x1, double y1, double x2, double y2, CadLayer on) =>
            document.Entities.Add(new CadLine(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0)) { Layer = on });

        Add(0, 0, 2, 0, layer);
        Add(2, 0, 4, 0, layer);
        Add(4, 0, 4, 6, layer);
        Add(4, 6, 0, 6, layer);
        Add(0, 6, 0, 0, layer);
        Add(1, 1, 1, 2, etch);

        const double radius = 0.3125;
        document.Entities.Add(HoleArc(210, 30, radius, layer));
        document.Entities.Add(HoleArc(30, 215.5, radius, layer));
        var overshoot = OnHole(215.5, radius);
        var seam = OnHole(210, radius);
        Add(overshoot.X, overshoot.Y, seam.X, seam.Y, layer);

        var path = Path.Combine(directory, "plate.dxf");
        DxfWriter.Write(path, document, false);
        return path;
    }

    private static CadArc HoleArc(double start, double end, double radius, CadLayer layer) =>
        new()
        {
            Center = new XYZ(2, 3, 0),
            Radius = radius,
            StartAngle = Angle.ToRadians(start),
            EndAngle = Angle.ToRadians(end),
            Layer = layer,
        };

    private static Vector OnHole(double degrees, double radius)
    {
        var angle = Angle.ToRadians(degrees);
        return new Vector(2 + radius * System.Math.Cos(angle), 3 + radius * System.Math.Sin(angle));
    }
}
