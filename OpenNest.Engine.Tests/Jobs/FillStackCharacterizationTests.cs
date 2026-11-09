using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Characterization pins for the Fill/fixed-strategy placement stack on irregular parts: the
/// interactive <see cref="PlateFillService"/> surface (single-item fill, whole-plate nest, clamp
/// refills) and one whole job per <see cref="FixedStrategyNestingEngine"/>. Each case pins the
/// sorted poses (part, X, Y, rotation rounded to 1e-6) as a count plus SHA-256 digest; a failure
/// prints the actual layout. They exist to prove filler/nester refactors layout-neutral, so a
/// changed digest is a layout change, never a value to re-capture without explaining it.
/// </summary>
/// <remarks>
/// Captured on Windows/.NET 8 without the optional ML angle model, so irregular parts take the
/// 5-degree sweep; each case gave one layout over 5 test processes of 2 solves each, and every
/// case solves twice in-process and must agree. A digest that differs between machines means
/// a layout decision depends on the machine (as <c>PairFiller</c>'s batches once followed the
/// processor count); fix that rather than repinning. The whole-plate Fill nests fill several
/// irregular drawings with one filler, so later items use the angles
/// <c>AngleCandidateBuilder.RecordProductive</c> learned from earlier ones.
/// Re-captured when quantity trimming began keeping interlocked pairs whole
/// (<c>ShrinkFiller.TrimToCount</c>): nine quantity-limited cases kept their counts but drop
/// different copies. Whole-job leftovers trying pairs first changed none of these pins.
/// The learned-angle nests were re-captured when the best-fit aspect limit began allowing a
/// part's own aspect: the long bar now nests as an interlocked pair, and the notched quantity
/// dropped from 6 to 4 so the Fill nest still differs from a full sweep.
/// The mixed Fill and Strip nests were re-captured when the pairs phase began tiling its leftover
/// strip with pair grids (and strips narrower than the part's bounding box) and trying skipped
/// candidates whose remnant could win: Fill now places 34 instead of 33, and Strip keeps 35 with
/// its L-shapes laid out differently (both layouts pass NestLayoutCheck).
/// Re-captured when the pairs phase began tiling herringbone pairs (<c>HerringbonePatterns</c>):
/// the mixed Strip nest keeps 35 laid out differently, and herringbone pairs won the chevron of
/// the learned-angle nests whatever angles were learned, so a cross replaced it.
/// Not pinned: richer mixes that pair parallelogram, chevron or tee parts at oblique angles
/// with other irregular items. Their Fill and Vertical Remnant nests vary between runs, most
/// likely because best-fit pair candidates are gathered in a <c>ConcurrentBag</c> and equal
/// scores keep that arbitrary order.
/// </remarks>
public class FillStackCharacterizationTests
{
    /// <summary>6x4 block with a 2x2 notch cut into the middle of its top edge.</summary>
    private static Program Notched()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 4);
        program.LineTo(4, 4);
        program.LineTo(4, 2);
        program.LineTo(2, 2);
        program.LineTo(2, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>
    /// 46 x 3 bar with a stepped end: irregular, and too long for the 48 x 28 work area at all
    /// but the sweep angles near 0 and 180 degrees, so only those prove productive and get learned.
    /// </summary>
    private static Program LongBar()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(46, 0);
        program.LineTo(46, 3);
        program.LineTo(42, 3);
        program.LineTo(42, 1.5);
        program.LineTo(0, 1.5);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>
    /// 6 x 6 cross with 2" arms. Filled after <see cref="LongBar"/>, its layout differs between
    /// the learned (pruned) angle list and a full sweep. (A chevron did until herringbone pairs,
    /// which do not depend on learned angles, began to win it.)
    /// </summary>
    private static Program Cross()
    {
        var program = new Program();
        program.MoveTo(2, 0);
        foreach (var (x, y) in new[]
        {
            (4.0, 0.0), (4.0, 2.0), (6.0, 2.0), (6.0, 4.0), (4.0, 4.0), (4.0, 6.0),
            (2.0, 6.0), (2.0, 4.0), (0.0, 4.0), (0.0, 2.0), (2.0, 2.0), (2.0, 0.0),
        })
            program.LineTo(x, y);
        return program;
    }

    private static Program Shape(string name) =>
        name switch
        {
            "lshape" => TestDrawingFactory.LShape(),
            "notched" => Notched(),
            "arc" => TestDrawingFactory.ArcPart(),
            "bar" => LongBar(),
            "cross" => Cross(),
            _ => TestDrawingFactory.Rectangle(4, 3),
        };

    private static Plate MakePlate()
    {
        var plate = new Plate(new Size(30, 50))
        {
            EdgeSpacing = new Spacing(1, 1, 1, 1),
            PartSpacing = 0.5,
        };
        return plate;
    }

    /// <summary>Two front-edge clamps (all on one edge, so the trimmed-strip layout competes).</summary>
    private static Plate MakeClampedPlate()
    {
        var plate = MakePlate();
        plate.ClampZones.Add(new ClampZone("front-1", new Box(12, 0, 2, 1.5)));
        plate.ClampZones.Add(new ClampZone("front-2", new Box(32, 0, 2, 1.5)));
        return plate;
    }

    // --- pose digests ---

    private static string FormatPose(string id, double x, double y, double rotation)
    {
        var twoPi = System.Math.PI * 2;
        var angle = rotation % twoPi;
        if (angle < 0)
            angle += twoPi;
        if (twoPi - angle < 1e-9)
            angle = 0;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}@({1},{2},{3})",
            id,
            Round(x),
            Round(y),
            Round(angle)
        );
    }

    // Normalizes negative zero so rounding never splits one pose into two spellings.
    private static string Round(double value) =>
        (System.Math.Round(value, 6) + 0.0).ToString("F6", CultureInfo.InvariantCulture);

    private static string Layout(IEnumerable<Part> parts) =>
        string.Join(
            "\n",
            parts
                .Select(p => FormatPose(p.BaseDrawing.Name, p.Location.X, p.Location.Y, p.Rotation))
                .Order(StringComparer.Ordinal)
        );

    private static string Layout(NestJobResult result) =>
        string.Join(
            "\n",
            result
                .Plates.SelectMany(plate =>
                    plate.Placements.Select(p =>
                        $"{plate.PlateIndex}:{p.InstanceIndex}:"
                        + FormatPose(p.PartId, p.X, p.Y, p.Rotation)
                    )
                )
                .Order(StringComparer.Ordinal)
        );

    private static string Digest(string layout) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(layout)))[..16];

    /// <summary>Runs <paramref name="solve"/> twice and requires identical layouts.</summary>
    private static string Stable(Func<string> solve)
    {
        var first = solve();
        var second = solve();
        Assert.True(
            first == second,
            $"layout must be deterministic across repeat runs:\n{first}\n---\n{second}"
        );
        return first;
    }

    private static void AssertPinned(string layout, int expectedCount, string expectedDigest)
    {
        var count = layout.Split('\n').Count(line => line.Contains('@'));
        var digest = Digest(layout);
        Assert.True(
            count == expectedCount && digest == expectedDigest,
            $"layout changed: expected {expectedCount} parts digest {expectedDigest}, "
                + $"actual {count} parts digest {digest}\n{layout}"
        );
    }

    // --- interactive single-item fill ---

    [Theory]
    [InlineData("Fill", "lshape", 53, "C215C11EEBD3B59C")]
    [InlineData("Fill", "notched", 46, "32FB33C3C19FDCC8")]
    [InlineData("Fill", "arc", 40, "4E2F91AA3151E9E7")]
    [InlineData("Strip", "lshape", 53, "C215C11EEBD3B59C")]
    [InlineData("Strip", "notched", 46, "32FB33C3C19FDCC8")]
    [InlineData("Strip", "arc", 40, "4E2F91AA3151E9E7")]
    [InlineData("Vertical Remnant", "lshape", 53, "C215C11EEBD3B59C")]
    [InlineData("Vertical Remnant", "notched", 46, "32FB33C3C19FDCC8")]
    [InlineData("Vertical Remnant", "arc", 40, "BBC5C583C3C7A9A7")]
    [InlineData("Horizontal Remnant", "lshape", 53, "C215C11EEBD3B59C")]
    [InlineData("Horizontal Remnant", "notched", 46, "32FB33C3C19FDCC8")]
    [InlineData("Horizontal Remnant", "arc", 40, "4E2F91AA3151E9E7")]
    public void FillItem_IrregularPart_PinnedLayout(
        string strategy,
        string shape,
        int expectedCount,
        string expectedDigest
    )
    {
        var layout = Stable(() =>
        {
            var plate = MakePlate();
            var item = new NestItem { Drawing = new Drawing(shape, Shape(shape)), Quantity = 0 };
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                plate.WorkArea(),
                null,
                CancellationToken.None
            );
            Assert.Empty(NestLayoutCheck.Validate(
                new List<(Plate, List<Part>)> { (plate, parts) },
                new Dictionary<Drawing, (string Name, int Quantity)>
                {
                    [item.Drawing] = (item.Drawing.Name, parts.Count),
                }
            ));
            return Layout(parts);
        });

        AssertPinned(layout, expectedCount, expectedDigest);
    }

    // --- interactive whole-plate nest ---

    /// <summary>
    /// Three irregular fill items (each over 10% of the plate) plus a single rectangle that
    /// takes the pack path. Strip runs the iterative shrink fill on the multi-quantity items.
    /// </summary>
    private static List<NestItem> MixedItems() =>
        [
            new() { Drawing = new Drawing("lshape", TestDrawingFactory.LShape()), Quantity = 14 },
            new() { Drawing = new Drawing("notched", Notched()), Quantity = 9 },
            new() { Drawing = new Drawing("arc", TestDrawingFactory.ArcPart()), Quantity = 11 },
            new() { Drawing = new Drawing("rect", TestDrawingFactory.Rectangle(4, 3)), Quantity = 1 },
        ];

    /// <summary>
    /// The bar fills first and is productive only near 0 and 180 degrees, so the Fill filler
    /// prunes the cross and notched fills to the angles it learned (see
    /// <see cref="Nest_LearnedAngleItems_DependsOnLearnedAngles"/>).
    /// </summary>
    private static List<NestItem> LearnedAngleItems() =>
        [
            new() { Drawing = new Drawing("bar", LongBar()), Quantity = 3 },
            new() { Drawing = new Drawing("cross", Cross()), Quantity = 20 },
            new() { Drawing = new Drawing("notched", Notched()), Quantity = 4 },
            new() { Drawing = new Drawing("rect", TestDrawingFactory.Rectangle(4, 3)), Quantity = 1 },
        ];

    private static string NestLayout(string strategy, Func<List<NestItem>> createItems)
    {
        var plate = MakePlate();
        var items = createItems();
        var parts = PlateFillService.Nest(strategy, plate, items, null, CancellationToken.None);
        // Remaining demand is part of the contract callers read back.
        var remaining = string.Join(",", items.Select(i => $"{i.Drawing.Name}={i.Quantity}"));
        return Layout(parts) + "\nremaining:" + remaining;
    }

    [Theory]
    [InlineData("Fill", 34, "D3D54E9116D96ACF")]
    [InlineData("Strip", 35, "55A0EE3EDF28E68E")]
    [InlineData("Vertical Remnant", 35, "8CC17C7708C8049F")]
    [InlineData("Horizontal Remnant", 30, "A0700052AE771E06")]
    public void Nest_MixedIrregularItems_PinnedLayout(
        string strategy,
        int expectedCount,
        string expectedDigest
    )
    {
        var layout = Stable(() => NestLayout(strategy, MixedItems));

        AssertPinned(layout, expectedCount, expectedDigest);
    }

    [Theory]
    [InlineData("Fill", 28, "BE363E3A47E9B036")]
    [InlineData("Strip", 23, "E4AA82327508D538")]
    [InlineData("Vertical Remnant", 28, "DB2409DB39642666")]
    [InlineData("Horizontal Remnant", 24, "883BD209AE87E2C5")]
    public void Nest_LearnedAngleItems_PinnedLayout(
        string strategy,
        int expectedCount,
        string expectedDigest
    )
    {
        var layout = Stable(() => NestLayout(strategy, LearnedAngleItems));

        AssertPinned(layout, expectedCount, expectedDigest);
    }

    /// <summary>
    /// Keeps <see cref="Nest_LearnedAngleItems_PinnedLayout"/> sensitive to learned angles: the
    /// Fill layout must differ from the same nest with a full sweep for every item. If this
    /// fails, the fixture no longer catches a filler that forgets learned angles between items.
    /// </summary>
    [Fact]
    public void Nest_LearnedAngleItems_DependsOnLearnedAngles()
    {
        string Solve(bool fullSweep)
        {
            var filler = new PlateFiller(MakePlate()) { ForceFullAngleSweep = fullSweep };
            return Layout(filler.Nest(LearnedAngleItems(), null, CancellationToken.None));
        }

        Assert.NotEqual(Solve(fullSweep: true), Solve(fullSweep: false));
    }

    // --- interactive fill beside clamps ---

    [Theory]
    [InlineData("Fill", 40, "5D07DCF3DC9B5F57")]
    [InlineData("Strip", 40, "5D07DCF3DC9B5F57")]
    [InlineData("Vertical Remnant", 40, "7072140D787044BD")]
    [InlineData("Horizontal Remnant", 40, "8FDDB0EA5D6D3620")]
    public void FillItem_WithClampZones_PinnedLayout(
        string strategy,
        int expectedCount,
        string expectedDigest
    )
    {
        var layout = Stable(() =>
        {
            var plate = MakeClampedPlate();
            // A quantity close to a full sheet makes clamp-dropped copies trigger refills.
            var item = new NestItem
            {
                Drawing = new Drawing("lshape", TestDrawingFactory.LShape()),
                Quantity = 40,
            };
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                plate.WorkArea(),
                null,
                CancellationToken.None
            );
            Assert.Empty(NestLayoutCheck.Validate(
                new List<(Plate, List<Part>)> { (plate, parts) },
                new Dictionary<Drawing, (string Name, int Quantity)>
                {
                    [item.Drawing] = (item.Drawing.Name, parts.Count),
                }
            ));
            return Layout(parts);
        });

        AssertPinned(layout, expectedCount, expectedDigest);
    }

    // --- whole-job fixed strategies ---

    private static NestJobPart JobPart(string id, int quantity) =>
        new(id, PartGeometrySnapshot.FromProgram(Shape(id)), quantity);

    private static NestJob IrregularJob() =>
        new(
            new[]
            {
                JobPart("lshape", 10),
                JobPart("notched", 6),
                JobPart("arc", 7),
                JobPart("rect", 3),
            },
            new[] { new NestPlateStock("stock", new Size(30, 50), 3, 0.5, new Spacing(1, 1, 1, 1)) },
            new NestJobOptions("Fill")
        );

    [Theory]
    [InlineData("Fill", 26, "0A7A67E19F821E84")]
    [InlineData("Strip", 26, "06ABFA7EBBF15472")]
    [InlineData("Vertical Remnant", 26, "0A7A67E19F821E84")]
    [InlineData("Horizontal Remnant", 26, "25DC495707C2BCA4")]
    public void FixedStrategyEngine_IrregularJob_PinnedLayout(
        string strategy,
        int expectedCount,
        string expectedDigest
    )
    {
        var layout = Stable(() =>
        {
            var job = IrregularJob();
            var result = new FixedStrategyNestingEngine(strategy).Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            LayoutAssert.Valid(job, result);
            return Layout(result);
        });

        AssertPinned(layout, expectedCount, expectedDigest);
    }
}
