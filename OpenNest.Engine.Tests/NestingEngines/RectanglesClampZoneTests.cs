using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// The Rectangles lane treats clamp zones as hard obstacles: placements never enter
/// the keep-out, demand uses the material between clamps, and layouts with zero
/// clamps stay bit-identical to the pre-clamp baseline (C2). Irregular and Default
/// parity is covered separately by IrregularClampZoneTests.
/// </summary>
public class RectanglesClampZoneTests
{
    private static OpenNest.Engine.NestingEngines.Rectangles.RectanglesNestingEngine Engine() => new();

    // ---- C2 characterization: zero-clamp layouts must not shift by a single bit -------

    private static string LayoutHash(NestJobResult result)
    {
        var sb = new StringBuilder();
        foreach (var sheet in result.Plates)
            foreach (var p in sheet.Placements)
                sb.Append(sheet.PlateIndex).Append('|').Append(sheet.StockId).Append('|')
                    .Append(p.PartId).Append('|')
                    .Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(p.Rotation.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Theory]
    // Pre-clamp baseline hashes, captured from the unchanged packer at commit a3f1124 and
    // re-captured when equal-cost sheet layouts began preferring the larger free offcut.
    [InlineData("3D389D6B993B69634EB45FC2CB0309273DE1BCF57A7C2BD980C63B535667B0C6")]
    public void ZeroClampLayoutIsBitIdenticalToBaseline(string hash) =>
        Assert.Equal(hash, LayoutHash(Engine().Solve(Job(
            new[] { Part("rect", Rectangle(10, 5), 12) },
            new[] { Stock("sheet", 48, 96, spacing: 0.25) }))));

    [Theory]
    // Complete candidates tie here on cost and placed material; the tie must fall to the
    // compactness offcut, then enumeration order, the same way on every platform (hash
    // identical on Windows and Linux).
    [InlineData("9B343B6C5CF2CE8753F2E04ACB0A9C4D5921F4ADC15AA2586AF466769C0F019B")]
    public void ZeroClampMixedShapeLayoutIsBitIdenticalToBaseline(string hash) =>
        Assert.Equal(hash, LayoutHash(Engine().Solve(Job(
            new[]
            {
                Part("disc", Disc(3), 10),
                Part("ell", LShape(12, 8, 4), 10),
                Part("tri", Triangle(9, 6), 10),
            },
            new[] { Stock("sheet", 40, 60, spacing: 0.5, edge: new Spacing(0.5, 0.5, 0.5, 0.5)) }))));

    [Theory]
    [InlineData("F848E4C6213888302A9EBAC2B9839879FB1A4B6D919ACB8932FA1639FF3508A9")]
    public void ZeroClampMultiSheetLayoutIsBitIdenticalToBaseline(string hash) =>
        Assert.Equal(hash, LayoutHash(Engine().Solve(Job(
            new[] { Part("square", Rectangle(10, 10), 30) },
            new[] { Stock("sheet", 25, 45, spacing: 0.25) }))));

    // ---- clamp avoidance ---------------------------------------------------------------

    [Fact]
    public void PartsNeverTouchAClampZone()
    {
        var clamp = new ClampZone("front-center", new Box(19.25, 0, 1.5, 0.625));
        var job = Job(
            new[] { Rectangle("blk", 6, 6, 6) },
            new[] { Stock("sheet", 20, 40, spacing: 0.5, clampZones: [clamp]) });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        foreach (var placement in result.Plates.SelectMany(p => p.Placements))
        {
            // Fixed 0-degree rectangles: analytic independent oracle against the raw zone.
            Assert.False(
                placement.X < 20.75 && placement.X + 6 > 19.25 &&
                placement.Y < 0.625 && placement.Y + 6 > 0,
                $"placement at ({placement.X},{placement.Y}) intrudes into the clamp zone");
        }
    }

    [Fact]
    public void DemandLandsBetweenClampsWhenItFits()
    {
        // One row of three 2x2 parts; only [0,3], [5,7] and [9,12] are open along X.
        var clamps = new[]
        {
            new ClampZone("clamp-a", new Box(3, 0, 2, 1)),
            new ClampZone("clamp-b", new Box(7, 0, 2, 1)),
        };
        var job = Job(
            new[] { Rectangle("row", 2, 2, 3) },
            new[] { Stock("strip", 2, 12, clampZones: clamps) });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var placements = result.Plates.SelectMany(p => p.Placements).ToList();
        Assert.Equal(3, placements.Count);
        Assert.Contains(placements, p => System.Math.Abs(p.X - 5) < 1e-9); // the middle slot
    }

    [Fact]
    public void ClampWallAcrossTheFrontEdgeBlocksTheOnlyViableRow()
    {
        // Without clamps the single viable row sits at y = bottom spacing (1.0). The full-width
        // front clamp with its edge clearance raises that floor above the work area.
        var wall = new ClampZone("rail-wall", new Box(0, 0, 10, 1));
        var job = Job(
            new[] { Rectangle("tall", 4, 4, 2) },
            new[]
            {
                Stock("sheet", 6, 10, spacing: 0,
                    edge: new Spacing(left: 0.25, bottom: 1, right: 0.25, top: 0.25),
                    clampZones: [wall]),
            });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Empty(result.Plates.SelectMany(p => p.Placements));
    }

    [Fact]
    public void ZonesClearOfTheWorkAreaNeedNotShiftAnything()
    {
        // Top-edge zone the demand never reaches: layout stays identical to no clamps.
        var far = new ClampZone("back", new Box(0, 47.5, 1.5, 0.5));
        var jobClamped = Job(
            new[] { Part("rect", Rectangle(10, 5), 12) },
            new[] { Stock("sheet", 48, 96, spacing: 0.25, clampZones: [far]) });
        var jobPlain = Job(
            new[] { Part("rect", Rectangle(10, 5), 12) },
            new[] { Stock("sheet", 48, 96, spacing: 0.25) });

        var clamped = Engine().Solve(jobClamped);
        var plain = Engine().Solve(jobPlain);

        LayoutAssert.Valid(jobClamped, clamped);
        Assert.Equal(LayoutHash(plain), LayoutHash(clamped));
    }

    // ---- large part spacing vs shallow clamp spans (PM d86d22c9, decision D11) --------
    //
    // The packer reserves a trailing part-spacing tail behind every item, so the block it
    // carves per axis is [zone - clearance + s, zone + clearance]. When s exceeds the
    // inflated span, that interval inverts. D11: block a zero-extent line at the clamp
    // edge facing the sheet interior — at (zone max + clearance) when the zone center lies
    // in the lower half of the bin on that axis, else at (zone min - clearance + s). The
    // old collapse to the zone centerline let a part start inside the clamp.
    //
    // The oracle below is independent of the packer and of NestLayoutCheck: material
    // [x, x+w] x [y, y+h] must not share interior with the clamp footprint predicted
    // directly from the rail rule (center +/- width/2 from the origin edge, depth into the
    // sheet, translated exactly like the work area), inflated by the bottom-edge spacing.
    // Boundary contact is clear (C16).

    private static Box InflatedRail(int quadrant, double length, double width,
        double center, double clampWidth, double depth, double clearance)
    {
        var left = quadrant is 1 or 4 ? 0.0 : -length;
        var bottom = quadrant is 1 or 2 ? 0.0 : -width;
        var near = center - clampWidth / 2 + left;
        var far = center + clampWidth / 2 + left;
        return new Box(
            System.Math.Min(near, far) - clearance,
            bottom - clearance,
            clampWidth + 2 * clearance,
            depth + 2 * clearance);
    }

    private static void AssertMaterialClearOfRail(NestJobResult result, Box inflated,
        double partW, double partH)
    {
        Assert.NotEmpty(result.Plates.SelectMany(p => p.Placements));
        foreach (var p in result.Plates.SelectMany(pl => pl.Placements))
        {
            var overlaps = p.X < inflated.Right - 1e-9 && p.X + partW > inflated.Left + 1e-9
                && p.Y < inflated.Top - 1e-9 && p.Y + partH > inflated.Bottom + 1e-9;
            Assert.False(overlaps,
                $"material [{p.X},{p.X + partW}] x [{p.Y},{p.Y + partH}] intrudes into the " +
                $"inflated clamp footprint [{inflated.Left},{inflated.Right}] x " +
                $"[{inflated.Bottom},{inflated.Top}]");
        }
    }

    [Theory]
    // PM d86d22c9 probe fixture: Y degenerate (s=4 > 0.625 + 2x0.25), full-width front
    // rail. Red on ab50900: Rectangles completed at y=0.3125, material on the clamp.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DegenerateYSpanKeepsExactClearanceInEveryQuadrant(int quadrant)
    {
        var stock = Stock("sheet", 4, 5, spacing: 4, edge: new Spacing(0, 0.25, 0, 0),
            quadrant: quadrant, clampZones: [new ClampZone("rail", new Box(0, 0, 5, 0.625))]);
        var job = Job(new[] { Rectangle("square", 2, 2, 1) }, new[] { stock });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertMaterialClearOfRail(result,
            InflatedRail(quadrant, 5, 4, 2.5, 5, 0.625, 0.25), 2, 2);
    }

    [Fact]
    public void DegenerateXSpanMidRailKeepsExactClearance()
    {
        // X degenerate (s=3 > 1.5 + 2x0.25); the 10-wide part is forced across the clamp's
        // X span because neither side can hold it with the tail. Red on ab50900: completed
        // at x=11.5 (the centerline), material inside the clamp.
        var stock = Stock("sheet", 4, 23, spacing: 3, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new ClampZone("mid-rail", new Box(10.75, 0, 1.5, 0.625))]);
        var job = Job(new[] { Rectangle("wide", 10, 2, 1) }, new[] { stock });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertMaterialClearOfRail(result, InflatedRail(1, 23, 4, 11.5, 1.5, 0.625, 0.25), 10, 2);
    }

    [Fact]
    public void DegenerateXSpanFarRailClearsTheInteriorFacingEdge()
    {
        // The clamp center sits in the upper half of the bin, so D11 blocks the
        // origin-facing edge (zone min - clearance + s = 20). Red on ab50900: the
        // centerline split let the 2x2 land at x=21, material inside the clamp;
        // after D11 it lands at x=20 — boundary contact, which is clear.
        var stock = Stock("sheet", 4, 23, spacing: 3, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new ClampZone("far-rail", new Box(20.25, 0, 1.5, 0.625))]);
        var job = Job(new[] { Rectangle("small", 2, 2, 1) }, new[] { stock });

        var result = Engine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertMaterialClearOfRail(result, InflatedRail(1, 23, 4, 21, 1.5, 0.625, 0.25), 2, 2);
    }

    // ---- C15 keep rows: non-inverted spans and between-clamp packing stay bit-exact ----

    private static NestJobResult KeepFixtureLargeSpacingNonInverted() => Engine().Solve(Job(
        new[] { Rectangle("blk", 6, 6, 6) },
        new[] { Stock("sheet", 20, 40, spacing: 0.5, clampZones: [new ClampZone("front-center", new Box(19.25, 0, 1.5, 0.625))]) }));

    private static NestJobResult KeepFixtureBetweenClamps() => Engine().Solve(Job(
        new[] { Rectangle("row", 2, 2, 3) },
        new[] { Stock("strip", 2, 12, clampZones: new[]
        {
            new ClampZone("clamp-a", new Box(3, 0, 2, 1)),
            new ClampZone("clamp-b", new Box(7, 0, 2, 1)),
        }) }));

    private static NestJobResult KeepFixtureBetweenClampsWithSpacing() => Engine().Solve(Job(
        new[] { Rectangle("row", 2, 2, 3) },
        new[] { Stock("strip", 2, 12, spacing: 0.5, clampZones: new[]
        {
            new ClampZone("clamp-a", new Box(3, 0, 2, 1)),
            new ClampZone("clamp-b", new Box(7, 0, 2, 1)),
        }) }));

    private static NestJobResult KeepFixtureMidRailSmallSpacing() => Engine().Solve(Job(
        new[] { Rectangle("wide", 10, 2, 2) },
        new[] { Stock("sheet", 4, 23, spacing: 0.5, edge: new Spacing(0, 0.25, 0, 0),
            clampZones: [new ClampZone("mid-rail", new Box(10.75, 0, 1.5, 0.625))]) }));

    [Theory]
    // Captured from the unchanged packer at ab50900 (see progress task-6.2 evidence):
    // non-inverted clamp spans must keep today's blocks and layouts bit-for-bit.
    // KEEP_LARGE_SPACING was re-captured when equal-cost layouts began preferring the larger offcut.
    [InlineData("KEEP_LARGE_SPACING")]
    [InlineData("KEEP_BETWEEN_CLAMPS")]
    [InlineData("KEEP_BETWEEN_SPACED")]
    [InlineData("KEEP_MID_RAIL_SMALL_SPACING")]
    public void NonInvertedClampLayoutsMatchAb50900Digests(string fixture)
    {
        var hash = fixture switch
        {
            "KEEP_LARGE_SPACING" => "3F390B18E99C6625E06A19295145F01EA85FEB7089DA99ED093E683AC71F7D02",
            "KEEP_BETWEEN_CLAMPS" => "F09B878211EB7C49649647003410B20191F635E3CD8DED6E03BEA5BD63B17D61",
            "KEEP_BETWEEN_SPACED" => "F09B878211EB7C49649647003410B20191F635E3CD8DED6E03BEA5BD63B17D61",
            "KEEP_MID_RAIL_SMALL_SPACING" => "97868486E834CA0B1DB110FF717635E38537DB96DF5CDFBB2AFE37E4B1366B71",
            _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
        };
        var actual = fixture switch
        {
            "KEEP_LARGE_SPACING" => LayoutHash(KeepFixtureLargeSpacingNonInverted()),
            "KEEP_BETWEEN_CLAMPS" => LayoutHash(KeepFixtureBetweenClamps()),
            "KEEP_BETWEEN_SPACED" => LayoutHash(KeepFixtureBetweenClampsWithSpacing()),
            "KEEP_MID_RAIL_SMALL_SPACING" => LayoutHash(KeepFixtureMidRailSmallSpacing()),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
        };
        Assert.Equal(hash, actual);
    }
}
