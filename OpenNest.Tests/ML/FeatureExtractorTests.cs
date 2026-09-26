using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.ML;

// PerfCounters assertions in the DEBUG section are global; the class runs inside the
// nonparallel FillCacheCollection so counter resets cannot race other fill tests.
[Collection(nameof(OpenNest.Tests.BestFit.FillCacheCollection))]
public class FeatureExtractorTests
{
    private const int BitmaskCells = 32 * 32;

    private static Drawing MakeRect(double w, double h, double rotation)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        if (!Tolerance.IsEqualTo(rotation, 0))
            pgm.Rotate(rotation, pgm.BoundingBox().Center);
        return new Drawing("rect", pgm) { Source = new SourceInfo { Angle = -rotation } };
    }

    private static Drawing Fixture(string shape) => shape switch
    {
        "rect" => new RectangleShape { Length = 10, Width = 20 }.GetDrawing(),
        "triangle" => new RightTriangleShape { Width = 10, Height = 8 }.GetDrawing(),
        "lshape" => new LShape { Width = 10, Height = 20, LegWidth = 5, LegHeight = 10 }.GetDrawing(),
        "ring" => new RingShape { OuterDiameter = 20, InnerDiameter = 8 }.GetDrawing(),
        "circle" => new CircleShape { Diameter = 15 }.GetDrawing(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    public static TheoryData<string> Shapes()
    {
        var data = new TheoryData<string>();
        foreach (var shape in new[] { "rect", "triangle", "lshape", "ring", "circle" })
            data.Add(shape);
        return data;
    }

    private static (double[] Scalars, byte[]? Bitmap) Snapshot(PartFeatures? features)
    {
        Assert.NotNull(features);
        var scalars = new[]
        {
            features.Area,
            features.Convexity,
            features.AspectRatio,
            features.BoundingBoxFill,
            features.Circularity,
            features.PerimeterToAreaRatio,
            features.VertexCount,
        };
        return (scalars, features.Bitmask is null ? null : (byte[])features.Bitmask.Clone());
    }

    private static void AssertScalarsEqual(PartFeatures expected, PartFeatures actual)
    {
        // Exact equality: both overloads must run identical scalar arithmetic.
        Assert.Equal(expected.Area, actual.Area);
        Assert.Equal(expected.Convexity, actual.Convexity);
        Assert.Equal(expected.AspectRatio, actual.AspectRatio);
        Assert.Equal(expected.BoundingBoxFill, actual.BoundingBoxFill);
        Assert.Equal(expected.Circularity, actual.Circularity);
        Assert.Equal(expected.PerimeterToAreaRatio, actual.PerimeterToAreaRatio);
        Assert.Equal(expected.VertexCount, actual.VertexCount);
    }

    private static string ProgramValues(Program program) =>
        string.Join(";", program.Codes.Select(code => code switch
        {
            RapidMove rapid => $"R{rapid.EndPoint.X:R},{rapid.EndPoint.Y:R}",
            LinearMove linear => $"L{linear.EndPoint.X:R},{linear.EndPoint.Y:R}:{linear.Layer}",
            ArcMove arc => $"A{arc.EndPoint.X:R},{arc.EndPoint.Y:R}c{arc.CenterPoint.X:R},{arc.CenterPoint.Y:R}:{arc.Layer}",
            _ => code.ToString() ?? string.Empty,
        }));

    [Theory]
    [MemberData(nameof(Shapes))]
    public void DefaultOverload_PreservesTrainingBitmap(string shape)
    {
        var features = FeatureExtractor.Extract(Fixture(shape));

        Assert.NotNull(features);
        Assert.NotNull(features.Bitmask);
        Assert.Equal(BitmaskCells, features.Bitmask.Length);
        Assert.All(features.Bitmask, cell => Assert.True(cell is 0 or 1));
        Assert.Contains((byte)1, features.Bitmask);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ScalarOnlyOverload_OmitsBitmap(string shape)
    {
        var features = FeatureExtractor.Extract(Fixture(shape), includeBitmask: false);

        Assert.NotNull(features);
        Assert.Null(features.Bitmask);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Scalars_AreIdenticalWithAndWithoutBitmap(string shape)
    {
        var drawing = Fixture(shape);

        var full = FeatureExtractor.Extract(drawing);
        var scalar = FeatureExtractor.Extract(drawing, includeBitmask: false);

        AssertScalarsEqual(full!, scalar!);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ExplicitTrue_MatchesDefaultOverloadBitForBit(string shape)
    {
        var drawing = Fixture(shape);

        var full = FeatureExtractor.Extract(drawing);
        var explicitTrue = FeatureExtractor.Extract(drawing, includeBitmask: true);

        AssertScalarsEqual(full!, explicitTrue!);
        Assert.NotNull(full.Bitmask);
        Assert.NotNull(explicitTrue.Bitmask);
        Assert.Equal(full.Bitmask, explicitTrue.Bitmask);
    }

    // Frozen SHA-256 of the 1024-byte default-overload bitmap produced by the pre-change
    // implementation at base 6863c8b (captured by running the original code on these exact
    // fixtures). Independent historical oracle: the overload-equivalence tests alone could
    // not detect a rasterization change applied to both overloads.
    private static readonly System.Collections.Generic.Dictionary<string, string> FrozenBitmapSha256 = new()
    {
        ["rect"] = "5A648D8015900D89664E00E125DF179636301A2D8FA191C1AA2BD9358EA53A69",
        ["triangle"] = "EEB88FFCC8B5E31777995FF0A3EEECE3C1CCF4BF9221D9241A40CD8FC055C708",
        ["lshape"] = "D6C2CC56414ADE01E7BA5E3030E6009391EBA28BB07DDF5938F10CB2638B82A7",
        ["ring"] = "151B0A0790A3D60749527427C2F31D4E2D2470E4F1F0C5AA6F643994C70F4223",
        ["circle"] = "151B0A0790A3D60749527427C2F31D4E2D2470E4F1F0C5AA6F643994C70F4223",
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void DefaultOverload_PreservesBaseImplementationBitmap(string shape)
    {
        var features = FeatureExtractor.Extract(Fixture(shape));

        Assert.NotNull(features?.Bitmask);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(features.Bitmask));
        Assert.Equal(FrozenBitmapSha256[shape], hash);
    }

    [Fact]
    public void RectangleBitmap_CoversEveryCell()
    {
        // The material fills the bounding box, so all 32x32 sample centres are interior points.
        var features = FeatureExtractor.Extract(new RectangleShape { Length = 10, Width = 20 }.GetDrawing());

        Assert.NotNull(features?.Bitmask);
        Assert.Equal(BitmaskCells, features.Bitmask.Count(cell => cell == 1));
    }

    [Theory]
    [InlineData("lshape")]
    [InlineData("ring")]
    public void ConcaveAndRingBitmaps_MixSetAndClearCells(string shape)
    {
        // FeatureExtractor rasterizes only ShapeProfile.Perimeter: the L-shape mix comes
        // from its concave outline, and the ring's mix from its circular outer silhouette
        // leaving bounding-box corners clear (the cutout is never rasterized).
        var features = FeatureExtractor.Extract(Fixture(shape));

        Assert.NotNull(features?.Bitmask);
        Assert.Contains((byte)1, features.Bitmask);
        Assert.Contains((byte)0, features.Bitmask);
    }

    [Fact]
    public void RotatedInput_FeaturesMatchAxisAlignedEquivalent()
    {
        var rotated = MakeRect(100, 50, 0.6);
        var axisAligned = MakeRect(100, 50, 0);

        var full = FeatureExtractor.Extract(rotated);
        var scalar = FeatureExtractor.Extract(rotated, includeBitmask: false);
        var reference = FeatureExtractor.Extract(axisAligned);

        Assert.NotNull(full);
        Assert.NotNull(scalar);
        Assert.NotNull(reference);
        AssertScalarClose(reference, full);
        AssertScalarClose(reference, scalar);
        Assert.NotNull(full.Bitmask);
        Assert.Null(scalar.Bitmask);
    }

    [Fact]
    public void CanonicalCopyInput_ExtractsSameScalarsAsSource()
    {
        var drawing = Fixture("lshape");
        var canonical = CanonicalFrame.AsCanonicalCopy(drawing);
        // Exercise the canonical-copy path: extraction consumes the transient copy, not the source.
        Assert.NotSame(drawing.Program, canonical.Program);

        var source = FeatureExtractor.Extract(drawing);
        var fromCanonical = FeatureExtractor.Extract(canonical, includeBitmask: false);

        Assert.NotNull(source);
        Assert.NotNull(fromCanonical);
        AssertScalarsEqual(source, fromCanonical);
    }

    [Fact]
    public void ScribeAndRapidMarks_DoNotAffectEitherOverload()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var plain = new Drawing("rect", pgm);

        var marked = new Program();
        marked.Codes.Add(new RapidMove(new Vector(0, 0)));
        marked.Codes.Add(new LinearMove(new Vector(10, 0)));
        marked.Codes.Add(new LinearMove(new Vector(10, 20)));
        marked.Codes.Add(new LinearMove(new Vector(0, 20)));
        marked.Codes.Add(new LinearMove(new Vector(0, 0)));
        marked.Codes.Add(new RapidMove(new Vector(3, 5)));
        marked.Codes.Add(new LinearMove(new Vector(7, 5)) { Layer = LayerType.Scribe });
        var markedDrawing = new Drawing("rect-marked", marked);

        var expected = Snapshot(FeatureExtractor.Extract(plain));
        var actualFull = Snapshot(FeatureExtractor.Extract(markedDrawing));
        var actualScalar = Snapshot(FeatureExtractor.Extract(markedDrawing, includeBitmask: false));

        Assert.Equal(expected.Scalars, actualFull.Scalars);
        Assert.Equal(expected.Bitmap!, actualFull.Bitmap!);
        Assert.Equal(expected.Scalars, actualScalar.Scalars);
        Assert.Null(actualScalar.Bitmap);
    }

    [Fact]
    public void GeometryWithoutMaterial_ThrowsIdenticallyForBothOverloads()
    {
        // Pre-existing behavior: ShapeProfile.Update unconditionally indexes shapes[0],
        // so extraction throws rather than returning null when no entities chain into a
        // shape. Both overloads must fail the same way.
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(1, 2)));
        pgm.Codes.Add(new RapidMove(new Vector(3, 4)));
        var drawing = new Drawing("rapid-only", pgm);

        Assert.Throws<ArgumentOutOfRangeException>(() => FeatureExtractor.Extract(drawing));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FeatureExtractor.Extract(drawing, includeBitmask: false));
    }

    [Fact]
    public void NullDrawing_ThrowsIdenticallyForBothOverloads()
    {
        // Pre-existing: CanonicalFrame tolerates null but Program dereference throws.
        Assert.Throws<NullReferenceException>(() => FeatureExtractor.Extract(null!));
        Assert.Throws<NullReferenceException>(
            () => FeatureExtractor.Extract(null!, includeBitmask: false));
    }

    [Fact]
    public void Extraction_DoesNotMutateInputDrawing()
    {
        var drawing = Fixture("lshape");
        var beforeProgram = ProgramValues(drawing.Program);
        var beforeArea = drawing.Area;
        var beforeAngle = drawing.Source.Angle;

        FeatureExtractor.Extract(drawing);
        FeatureExtractor.Extract(drawing, includeBitmask: false);

        Assert.Equal(beforeProgram, ProgramValues(drawing.Program));
        Assert.Equal(beforeArea, drawing.Area);
        Assert.Equal(beforeAngle, drawing.Source.Angle);
    }

    [Fact]
    public void RepeatedExtraction_IsDeterministic()
    {
        var drawing = Fixture("ring");

        var first = Snapshot(FeatureExtractor.Extract(drawing));
        var second = Snapshot(FeatureExtractor.Extract(drawing));
        var scalarFirst = Snapshot(FeatureExtractor.Extract(drawing, includeBitmask: false));
        var scalarSecond = Snapshot(FeatureExtractor.Extract(drawing, includeBitmask: false));

        Assert.Equal(first.Scalars, second.Scalars);
        Assert.Equal(first.Bitmap!, second.Bitmap!);
        Assert.Equal(scalarFirst.Scalars, scalarSecond.Scalars);
    }

    private static void AssertScalarClose(PartFeatures reference, PartFeatures actual)
    {
        Assert.Equal(reference.Area, actual.Area, precision: 6);
        Assert.Equal(reference.Convexity, actual.Convexity, precision: 6);
        Assert.Equal(reference.AspectRatio, actual.AspectRatio, precision: 6);
        Assert.Equal(reference.BoundingBoxFill, actual.BoundingBoxFill, precision: 6);
        Assert.Equal(reference.Circularity, actual.Circularity, precision: 6);
        Assert.Equal(reference.PerimeterToAreaRatio, actual.PerimeterToAreaRatio, precision: 6);
        Assert.Equal(reference.VertexCount, actual.VertexCount);
    }

#if DEBUG
    [Fact]
    public void DefaultOverload_RunsFullBitmaskCellScan()
    {
        var drawing = Fixture("lshape");
        FeatureExtractor.Extract(drawing); // warm canonical/JIT path outside the counted window

        PerfCounters.Reset();
        long cells;
        try
        {
            FeatureExtractor.Extract(drawing);
            cells = PerfCounters.FeatureBitmaskCells;
        }
        finally
        {
            PerfCounters.Reset();
        }

        Assert.Equal(BitmaskCells, cells);
    }

    [Fact]
    public void ScalarOnlyOverload_RunsNoBitmaskCellScan()
    {
        var drawing = Fixture("lshape");
        FeatureExtractor.Extract(drawing, includeBitmask: false);

        PerfCounters.Reset();
        long cells;
        try
        {
            var features = FeatureExtractor.Extract(drawing, includeBitmask: false);
            cells = PerfCounters.FeatureBitmaskCells;
            Assert.Null(features.Bitmask);
        }
        finally
        {
            PerfCounters.Reset();
        }

        // Genuine removal: every point-in-polygon cell test in the 32x32 scan is skipped.
        Assert.Equal(0, cells);
    }

    [Fact]
    public void FailedExtraction_DoesNotRunBitmaskScan()
    {
        // Extraction fails before bitmap construction, so the 1024-cell scan never runs.
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(1, 2)));
        var drawing = new Drawing("rapid-only", pgm);

        PerfCounters.Reset();
        long cells;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FeatureExtractor.Extract(drawing));
            cells = PerfCounters.FeatureBitmaskCells;
        }
        finally
        {
            PerfCounters.Reset();
        }

        Assert.Equal(0, cells);
    }
#endif
}
