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
    // Pre-clamp baseline hashes, captured from the unchanged packer at commit a3f1124.
    [InlineData("1FA61E933D2D0D6F1E3336FCEBBB21CEB7E7D1533FDB253F9D48E947217575CE")]
    public void ZeroClampLayoutIsBitIdenticalToBaseline(string hash) =>
        Assert.Equal(hash, LayoutHash(Engine().Solve(Job(
            new[] { Part("rect", Rectangle(10, 5), 12) },
            new[] { Stock("sheet", 48, 96, spacing: 0.25) }))));

    [Theory]
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
    [InlineData("DBF93C1684D85B5EE7EB24C96A84904D2BA0F9A8183631EFC61638CB8B9B935A")]
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
}
