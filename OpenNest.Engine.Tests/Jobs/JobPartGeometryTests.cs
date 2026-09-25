using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class JobPartGeometryTests
{
    [Fact]
    public void RapidsAndEtchAreExcludedFromBoundsAndArea()
    {
        var program = TestDrawingFactory.Rectangle(10, 10);
        program.MoveTo(9.5, 5);
        program.Codes.Add(new LinearMove(14, 5) { Layer = LayerType.Scribe });
        program.MoveTo(100, 100);

        var geometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(program));

        Assert.Equal(100, geometry.MaterialArea);
        Assert.Equal(0, geometry.Bounds.Left);
        Assert.Equal(0, geometry.Bounds.Bottom);
        Assert.Equal(10, geometry.Bounds.Right);
        Assert.Equal(10, geometry.Bounds.Top);
        Assert.Empty(geometry.Cutouts);
        Assert.All(geometry.Perimeter.Entities, e => Assert.True(SpecialLayers.IsMaterial(e.Layer)));
    }

    [Fact]
    public void EtchTickCrossingIntoNotchIsIgnored()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 4);
        program.LineTo(6, 4);
        program.LineTo(6, 6);
        program.LineTo(10, 6);
        program.LineTo(10, 10);
        program.LineTo(0, 10);
        program.LineTo(0, 0);
        program.MoveTo(5, 5);
        program.Codes.Add(new LinearMove(8, 5) { Layer = LayerType.Scribe });

        var geometry = JobPartGeometry.TryRead(PartGeometrySnapshot.FromProgram(program));

        Assert.NotNull(geometry);
        Assert.Equal(92, geometry.MaterialArea);
        Assert.Equal(8, geometry.Perimeter.Entities.Count);
        Assert.Empty(geometry.Cutouts);
    }

    [Fact]
    public void MapperRoundTripPreservesHoleAreaAndHostWinding()
    {
        var program = TestDrawingFactory.Rectangle(10, 10);
        program.MoveTo(3, 3);
        program.LineTo(7, 3);
        program.LineTo(7, 7);
        program.LineTo(3, 7);
        program.LineTo(3, 3);
        var part = DrawingJobMapper.FromDrawing("frame", new Drawing("frame", program), 1);
        var motions = part.Geometry.Motions.ToArray();

        var geometry = JobPartGeometry.Read(part.Geometry);

        Assert.Equal(84, geometry.MaterialArea);
        Assert.Equal(16, Assert.Single(geometry.Cutouts).Area());
        Assert.Same(geometry.Perimeter, geometry.Profile.Perimeter);
        Assert.Equal(RotationType.CW, geometry.Perimeter.ToPolygon().RotationDirection());
        Assert.Equal(RotationType.CCW, geometry.Cutouts[0].ToPolygon().RotationDirection());
        Assert.Equal(motions, part.Geometry.Motions);
    }

    [Fact]
    public void AnalyticCircleIsPreserved()
    {
        var program = new Program();
        program.MoveTo(5, 0);
        program.Codes.Add(new ArcMove(5, 0, 0, 0, RotationType.CW));

        var geometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(program));

        Assert.IsType<Circle>(Assert.Single(geometry.Perimeter.Entities));
        Assert.Equal(25 * System.Math.PI, geometry.MaterialArea, 10);
    }

    [Fact]
    public void OpenCutLeavingMaterialIsUnusableButInternalMarkDoesNotChangeArea()
    {
        var program = TestDrawingFactory.Rectangle(10, 10);
        program.MoveTo(5, 5);
        program.LineTo(7, 5);
        Assert.Equal(100, JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(program)).MaterialArea);
        program.LineTo(11, 5);
        var snapshot = PartGeometrySnapshot.FromProgram(program);

        var error = Assert.Throws<ArgumentException>(() => JobPartGeometry.Read(snapshot));
        Assert.Contains("Open geometry leaves the closed material region", error.Message);
        Assert.Null(JobPartGeometry.TryRead(snapshot));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("open")]
    [InlineData("zero-length")]
    [InlineData("non-finite")]
    [InlineData("rapid-only")]
    [InlineData("scribe-only")]
    public void UnusableSnapshotsReturnNullWithoutThrowing(string kind)
    {
        var program = new Program();
        if (kind == "open")
        {
            program.MoveTo(0, 0);
            program.LineTo(1, 1);
        }
        if (kind == "zero-length")
        {
            program = TestDrawingFactory.Rectangle(10, 10);
            program.LineTo(0, 0);
        }
        if (kind == "non-finite")
            program.LineTo(double.NaN, 0);
        if (kind == "rapid-only")
            program.MoveTo(100, 100);
        if (kind == "scribe-only")
            program.Codes.Add(new LinearMove(1, 1) { Layer = LayerType.Scribe });
        var snapshot = PartGeometrySnapshot.FromProgram(program);

        Assert.Throws<ArgumentException>(() => JobPartGeometry.Read(snapshot));
        Assert.Null(JobPartGeometry.TryRead(snapshot));
    }
}
