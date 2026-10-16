using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;

namespace OpenNest.Tests.IO;

/// <summary>
/// Pins how the best-fit pair cache round-trips through the <c>bestfits/</c> entries of a
/// .nest archive: which sets are written, their exact bytes, and what a read puts back.
/// </summary>
[Collection(nameof(FillCacheCollection))]
public class BestFitPersistenceTests
{
    private const double Spacing = 0.25;
    private const double UsedWidth = 60;
    private const double UsedLength = 40;
    private const double UnusedWidth = 14;
    private const double UnusedLength = 30;

    // Narrow enough that the filter drops some computed pairs (the T-shape pairs are ~8 x 17).
    private const double NarrowWidth = 8.1;

    // Captured from the writer before the best-fit mapping moved out of OpenNest.IO.
    private const string GoldenBestFitJson = """
        [
          {
            "plateWidth": 60,
            "plateHeight": 40,
            "spacing": 0.25,
            "results": [
              {
                "part1Rotation": 0,
                "part2Rotation": 3.141592653589793,
                "part2OffsetX": 10.3,
                "part2OffsetY": -0.1,
                "strategyType": 2,
                "testNumber": 17,
                "candidateSpacing": 0.25,
                "rotatedArea": 123.456,
                "boundingWidth": 20.5,
                "boundingHeight": 6.0225,
                "optimalRotation": 0.7853981633974483,
                "keep": true,
                "reason": "",
                "trueArea": 100.125,
                "hullAngles": []
              },
              {
                "part1Rotation": 1.5707963267948966,
                "part2Rotation": 4.71238898038469,
                "part2OffsetX": 0,
                "part2OffsetY": 8.125,
                "strategyType": 0,
                "testNumber": 3,
                "candidateSpacing": 0.25,
                "rotatedArea": 98.5,
                "boundingWidth": 8.25,
                "boundingHeight": 11.9,
                "optimalRotation": 0,
                "keep": true,
                "reason": "Valid",
                "trueArea": 80,
                "hullAngles": [
                  0,
                  0.5235987755982988,
                  -1E-12
                ]
              }
            ]
          }
        ]
        """;

    [Fact]
    public void Write_PopulatedCache_WritesGoldenBytesForUsedSizesAndKeptResultsOnly()
    {
        BestFitCache.Clear();
        var nest = CreateNest(MakeTShape());
        PopulateHandBuiltSets(nest.Drawings.First());

        var entries = ReadEntries(Save(nest));

        Assert.Equal(new[] { "bestfits/bestfit-1" }, entries.Keys.Where(IsBestFitEntry).ToArray());
        var expected = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(GoldenBestFitJson.ReplaceLineEndings(System.Environment.NewLine)))
            .ToArray();
        Assert.Equal(expected, entries["bestfits/bestfit-1"]);
    }

    [Fact]
    public void Read_HandBuiltSets_RestoresKeptResultsForUsedSizeFieldByField()
    {
        BestFitCache.Clear();
        var nest = CreateNest(MakeTShape());
        var source = nest.Drawings.First();
        PopulateHandBuiltSets(source);
        var expected = BestFitCache.GetAllForDrawing(source)[(UsedWidth, UsedLength, Spacing)]
            .Where(r => r.Keep)
            .ToList();
        var bytes = Save(nest);

        BestFitCache.Clear();
        var loaded = Load(bytes);
        var drawing = loaded.Drawings.Single();
        var restored = BestFitCache.GetAllForDrawing(drawing);

        Assert.Equal(new[] { (UsedWidth, UsedLength, Spacing) }, restored.Keys.ToArray());
        AssertSameResults(expected, restored[(UsedWidth, UsedLength, Spacing)], drawing);
    }

    [Fact]
    public void RoundTrip_ComputedSets_RestoresUsedSizeOnlyAndKeptResultsOnly()
    {
        BestFitCache.Clear();
        var nest = CreateNest(MakeTShape(), NarrowWidth, UsedLength);
        var source = nest.Drawings.First();
        var used = BestFitCache.GetOrCompute(source, NarrowWidth, UsedLength, Spacing);
        BestFitCache.GetOrCompute(source, UnusedWidth, UnusedLength, Spacing);
        Assert.Contains(used, r => r.Keep);
        Assert.Contains(used, r => !r.Keep);
        var bytes = Save(nest);

        BestFitCache.Clear();
        var drawing = Load(bytes).Drawings.Single();
        var restored = BestFitCache.GetAllForDrawing(drawing);

        Assert.Equal(new[] { (NarrowWidth, UsedLength, Spacing) }, restored.Keys.ToArray());
        AssertSameResults(used.Where(r => r.Keep).ToList(), restored[(NarrowWidth, UsedLength, Spacing)], drawing);
    }

    [Fact]
    public void RoundTrip_RotatedSource_LoadedCandidatesHoldTheSourceDrawingNotACanonicalCopy()
    {
        // Pins current behaviour: computed candidates hold a canonical copy (angle 0), while
        // loaded candidates hold the loaded source drawing itself, so BuildCanonicalParts on a
        // loaded result builds from the source frame.
        BestFitCache.Clear();
        var nest = CreateNest(MakeRotatedTShape());
        var source = nest.Drawings.First();
        var computed = BestFitCache.GetOrCompute(source, UsedWidth, UsedLength, Spacing).First(r => r.Keep);
        Assert.NotSame(source, computed.Candidate.Drawing);
        Assert.Equal(0.0, computed.Candidate.Drawing.Source.Angle);
        var bytes = Save(nest);

        BestFitCache.Clear();
        var drawing = Load(bytes).Drawings.Single();
        var loaded = BestFitCache.GetAllForDrawing(drawing)[(UsedWidth, UsedLength, Spacing)].First();

        Assert.False(Tolerance.IsEqualTo(drawing.Source.Angle, 0));
        Assert.Equal(source.Source.Angle, drawing.Source.Angle, 9);
        Assert.Same(drawing, loaded.Candidate.Drawing);

        Assert.Equal(0.0, computed.BuildCanonicalParts()[0].BaseDrawing.Source.Angle);
        Assert.Same(drawing, loaded.BuildCanonicalParts()[0].BaseDrawing);

        // The computed pair rebuilds to its saved bounds; the loaded one, built from the
        // source frame with canonical-frame offsets, does not.
        var computedBounds = computed.GetCutBounds(computed.BuildSourceParts(source));
        Assert.Equal(computed.BoundingWidth, computedBounds.Length, 2);
        Assert.Equal(computed.BoundingHeight, computedBounds.Width, 2);
        var loadedBounds = loaded.GetCutBounds(loaded.BuildSourceParts(drawing));
        Assert.NotEqual(loaded.BoundingWidth * loaded.BoundingHeight, loadedBounds.Length * loadedBounds.Width, 2);
    }

    [Fact]
    public void RoundTrip_NoBestFits_LoadsAndSavesUnchanged()
    {
        BestFitCache.Clear();
        var original = ReadEntries(Save(CreateNest(MakeTShape())));
        Assert.DoesNotContain(original.Keys, IsBestFitEntry);

        BestFitCache.Clear();
        var loaded = Load(Save(CreateNest(MakeTShape())));
        Assert.Empty(BestFitCache.GetAllForDrawing(loaded.Drawings.Single()));
        var resaved = ReadEntries(Save(loaded));

        Assert.Equal(original.Keys.OrderBy(k => k), resaved.Keys.OrderBy(k => k));
        foreach (var name in original.Keys.Where(k => k != "nest.json"))
            Assert.Equal(original[name], resaved[name]);
    }

    private static bool IsBestFitEntry(string name) => name.StartsWith("bestfits/", System.StringComparison.Ordinal);

    private static Drawing MakeTShape() => new TShape { Width = 10, Height = 8 }.GetDrawing();

    private static Drawing MakeRotatedTShape()
    {
        var drawing = MakeTShape();
        drawing.Program.Rotate(Angle.ToRadians(30), drawing.Program.BoundingBox().Center);
        drawing.RecomputeCanonicalAngle();
        return drawing;
    }

    private static Nest CreateNest(Drawing drawing, double width = UsedWidth, double length = UsedLength)
    {
        var nest = new Nest { Name = "BestFits" };
        nest.Drawings.Add(drawing);
        var plate = new Plate(width, length) { PartSpacing = Spacing };
        plate.Parts.Add(new Part(drawing, new Vector(1, 1)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static void PopulateHandBuiltSets(Drawing drawing)
    {
        BestFitCache.Populate(drawing, UsedWidth, UsedLength, Spacing, new()
        {
            MakeResult(drawing, 0, System.Math.PI, new Vector(0.1 + 0.2 + 10, -0.1), 2, 17,
                123.456, 20.5, 6.0225, System.Math.PI / 4, true, null, 100.125, null),
            MakeResult(drawing, 0, 0, new Vector(5, 5), 1, 9,
                50, 5, 10, 0, false, "Too wide", 40, new() { 1.0 }),
            MakeResult(drawing, System.Math.PI / 2, 3 * System.Math.PI / 2, new Vector(0, 8.125), 0, 3,
                98.5, 8.25, 11.9, 0, true, "Valid", 80, new() { 0.0, System.Math.PI / 6, -1e-12 }),
        });
        BestFitCache.Populate(drawing, UnusedWidth, UnusedLength, Spacing, new()
        {
            MakeResult(drawing, 0, System.Math.PI, new Vector(3, 4), 0, 1,
                10, 2, 5, 0, true, "Valid", 8, new()),
        });
    }

    private static BestFitResult MakeResult(
        Drawing drawing, double rotation1, double rotation2, Vector offset, int strategy, int test,
        double rotatedArea, double width, double height, double optimal, bool keep, string reason,
        double trueArea, System.Collections.Generic.List<double> hullAngles) =>
        new()
        {
            Candidate = new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = rotation1,
                Part2Rotation = rotation2,
                Part2Offset = offset,
                StrategyIndex = strategy,
                TestNumber = test,
                Spacing = Spacing,
            },
            RotatedArea = rotatedArea,
            BoundingWidth = width,
            BoundingHeight = height,
            OptimalRotation = optimal,
            Keep = keep,
            Reason = reason,
            TrueArea = trueArea,
            HullAngles = hullAngles,
        };

    private static void AssertSameResults(
        System.Collections.Generic.List<BestFitResult> expected,
        System.Collections.Generic.List<BestFitResult> actual,
        Drawing loadedDrawing)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            Assert.Same(loadedDrawing, a.Candidate.Drawing);
            AssertBits(e.Candidate.Part1Rotation, a.Candidate.Part1Rotation);
            AssertBits(e.Candidate.Part2Rotation, a.Candidate.Part2Rotation);
            AssertBits(e.Candidate.Part2Offset.X, a.Candidate.Part2Offset.X);
            AssertBits(e.Candidate.Part2Offset.Y, a.Candidate.Part2Offset.Y);
            Assert.Equal(e.Candidate.StrategyIndex, a.Candidate.StrategyIndex);
            Assert.Equal(e.Candidate.TestNumber, a.Candidate.TestNumber);
            AssertBits(e.Candidate.Spacing, a.Candidate.Spacing);
            AssertBits(e.RotatedArea, a.RotatedArea);
            AssertBits(e.BoundingWidth, a.BoundingWidth);
            AssertBits(e.BoundingHeight, a.BoundingHeight);
            AssertBits(e.OptimalRotation, a.OptimalRotation);
            Assert.Equal(e.Keep, a.Keep);
            Assert.Equal(e.Reason ?? "", a.Reason);
            AssertBits(e.TrueArea, a.TrueArea);
            Assert.Equal(
                (e.HullAngles ?? new()).Select(System.BitConverter.DoubleToInt64Bits),
                a.HullAngles.Select(System.BitConverter.DoubleToInt64Bits));
        }
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(System.BitConverter.DoubleToInt64Bits(expected), System.BitConverter.DoubleToInt64Bits(actual));

    private static byte[] Save(Nest nest)
    {
        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        return stream.ToArray();
    }

    private static Nest Load(byte[] bytes) => new NestReader(new MemoryStream(bytes)).Read();

    private static System.Collections.Generic.Dictionary<string, byte[]> ReadEntries(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var entryStream = e.Open();
            using var copy = new MemoryStream();
            entryStream.CopyTo(copy);
            return copy.ToArray();
        });
    }
}
