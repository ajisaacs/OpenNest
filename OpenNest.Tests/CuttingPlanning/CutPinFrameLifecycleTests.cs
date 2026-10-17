using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Pins are stored in the drawing's source frame and must land on the same physical point of a
/// placed part however the drawing was produced, edited, saved, packaged or rotated. The oracle
/// never uses Source.Offset or part rotation bookkeeping: it recovers the rigid source-to-plate
/// transform from the placed geometry itself, using two holes of different sizes.
/// </summary>
public class CutPinFrameLifecycleTests
{
    // Source (DXF) coordinates, deliberately far from the origin.
    private static readonly Vector BigHole = new(23, 13);
    private static readonly Vector SmallHole = new(31.25, 10.75);
    private const double BigRadius = 1;
    private const double SmallRadius = 0.25;

    private static readonly Vector[] SourcePins =
    {
        new(32, 18), // perimeter corner
        new(24, 13), // big hole, rightmost point
        new(31.25, 11), // small hole, top point
    };

    private static readonly double[] Rotations = { 0, Angle.HalfPI, Angle.ToRadians(37) };

    /// <summary>
    /// A 12 x 8 plate at (20, 10) with two pinned holes and an unpinned first-cut hole: the
    /// program origin sits on that hole, so hiding it in the converter moves the origin.
    /// </summary>
    private static (CadImportResult Result, Entity FirstHole) Import()
    {
        var firstHole = new Circle(29, 15, 0.5);
        var entities = new List<Entity>
        {
            new Line(20, 10, 32, 10),
            new Line(32, 10, 32, 18),
            new Line(32, 18, 20, 18),
            new Line(20, 18, 20, 10),
            new Circle(BigHole, BigRadius),
            firstHole,
            new Circle(SmallHole, SmallRadius),
        };
        return (new CadImportResult { Entities = entities, Name = "Synthetic plate", SourcePath = "synthetic.dxf" },
            firstHole);
    }

    private static Drawing ImportPinned(out CadImportResult result, out Entity firstHole)
    {
        (result, firstHole) = Import();
        var drawing = CadImporter.BuildDrawing(result, result.Entities, result.Bends, 1, null, null);
        drawing.CutPins.Add(new ContourCutPin(SourcePins[0], new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
            new NoLeadOut(), new NormalTab { Size = 0.03 }));
        drawing.CutPins.Add(new ContourCutPin(SourcePins[1], new ArcLeadIn { Radius = 0.1 }, new NoLeadOut()));
        drawing.CutPins.Add(new ContourCutPin(SourcePins[2], new LineLeadIn { Length = 0.1 },
            new LineLeadOut { Length = 0.05 }));
        return drawing;
    }

    [Fact]
    public void Import_ReoriginsTheProgramAwayFromTheSourceFrame()
    {
        var drawing = ImportPinned(out _, out _);

        Assert.NotEqual(Vector.Zero, drawing.Source.Offset);
        AssertPinsLand(drawing);
    }

    [Fact]
    public void ConverterEditThatMovesTheProgramOrigin_KeepsPinsOnTheirContours()
    {
        var drawing = ImportPinned(out var result, out var firstHole);
        var before = drawing.Source.Offset;

        // Load into the editor, hide the first-cut hole, rebuild, and apply in place as the desktop does.
        var editable = CadImporter.EditableEntities(drawing);
        firstHole.IsVisible = false;
        result.Entities = editable;
        var visible = editable.Where(e => e.Layer.IsVisible && e.IsVisible).ToList();
        var edited = CadImporter.BuildDrawing(result, visible, result.Bends, 1, null, null);
        CadImporter.ApplyEdit(drawing, edited);

        Assert.NotEqual(before, drawing.Source.Offset);
        Assert.Equal(3, drawing.CutPins.Count);
        AssertPinsLand(drawing);
    }

    [Fact]
    public void ConverterEditOfADrawingWithoutSourceEntities_KeepsPinsOnTheirContours()
    {
        var drawing = ImportPinned(out var result, out _);
        drawing.SourceEntities = null;

        var editable = CadImporter.EditableEntities(drawing);
        var edited = CadImporter.BuildDrawing(
            new CadImportResult { Entities = editable, Name = result.Name }, editable, new List<OpenNest.Bending.Bend>(), 1,
            null, null);
        CadImporter.ApplyEdit(drawing, edited);

        AssertPinsLand(drawing);
    }

    [Fact]
    public void SaveAndReload_KeepsPinsOnEveryPlacement()
    {
        var drawing = ImportPinned(out _, out _);
        var nest = new Nest("Synthetic") { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        var plate = new Plate(96, 48);
        nest.Plates.Add(plate);
        for (var i = 0; i < Rotations.Length; i++)
            plate.Parts.Add(Place(drawing, Rotations[i], new Vector(5 + 25 * i, 20)));

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        var reader = new NestReader(new MemoryStream(stream.ToArray()));
        var restored = reader.Read();

        Assert.Empty(reader.Warnings);
        var parts = Assert.Single(restored.Plates).Parts;
        Assert.Equal(Rotations.Length, parts.Count);
        foreach (var part in parts)
            AssertPinsLand(part);
    }

    [Fact]
    public void Package_KeepsPinsOnTheirContours()
    {
        var drawing = ImportPinned(out _, out _);

        var contents = DrawingPackage.Read(DrawingPackage.Write(drawing, Units.Inches));

        Assert.Equal(drawing.Source.Offset, contents.Drawing.Source.Offset);
        AssertPinsLand(contents.Drawing);
    }

    private static Part Place(Drawing drawing, double rotation, Vector location)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Offset(location);
        return part;
    }

    /// <summary>Places the drawing at each rotation, directly and the way engines do, and checks every pin.</summary>
    private static void AssertPinsLand(Drawing drawing)
    {
        foreach (var rotation in Rotations)
        {
            AssertPinsLand(Place(drawing, rotation, new Vector(50, 40)));
            var atOrigin = Part.CreateAtOrigin(drawing, rotation);
            atOrigin.Offset(new Vector(7, 3));
            AssertPinsLand(atOrigin);
        }
    }

    private static void AssertPinsLand(Part part)
    {
        var results = CutPinResolver.Resolve(part);

        Assert.Equal(SourcePins.Length, results.Count);
        Assert.All(results, r => Assert.True(r.IsResolved, $"Pin {r.PinIndex + 1}: {r.Status}"));
        Assert.Equal(SourcePins.Length, results.Select(r => r.ContourOrdinal).Distinct().Count());
        var toPlate = SourceToPlate(part);
        for (var i = 0; i < SourcePins.Length; i++)
        {
            var expected = toPlate(SourcePins[i]);
            var actual = results[i].Point + part.Location;
            Assert.True(expected.DistanceTo(actual) <= 1e-9,
                $"Pin {i + 1} at rotation {part.Rotation}: expected {expected}, got {actual}.");
        }
    }

    /// <summary>The rigid source-to-plate transform recovered from the placed holes alone.</summary>
    private static Func<Vector, Vector> SourceToPlate(Part part)
    {
        var shapes = ShapeBuilder.GetShapes(
            ConvertProgram.ToGeometry(part.Program).Where(e => SpecialLayers.IsMaterial(e.Layer)));
        var big = HoleCentre(shapes, BigRadius) + part.Location;
        var small = HoleCentre(shapes, SmallRadius) + part.Location;
        var turn = Direction(small - big) - Direction(SmallHole - BigHole);
        return source => (source - BigHole).Rotate(turn) + big;
    }

    // A circle's bounding box is centred on it at any rotation.
    private static Vector HoleCentre(List<Shape> shapes, double radius)
    {
        var box = Assert.Single(shapes.Select(s => s.Entities.GetBoundingBox()),
            b => System.Math.Abs(b.Width - 2 * radius) < 1e-6 && System.Math.Abs(b.Length - 2 * radius) < 1e-6);
        return box.Center;
    }

    private static double Direction(Vector v) => System.Math.Atan2(v.Y, v.X);
}
