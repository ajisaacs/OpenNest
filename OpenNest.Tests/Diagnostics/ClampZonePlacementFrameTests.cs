using System.Linq;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Diagnostics;

/// <summary>
/// Live plates store clamp zones in the sheet's lower-left frame; parts, cut-offs and motions
/// use the signed quadrant frame. Every clamp check must compare in the placement frame.
/// Fixtures use a real <see cref="ClampRail"/> so an already-signed hand-made zone cannot hide
/// a missing translation. Expected footprints come from the physical rule: a clamp centered c
/// covers c +/- width/2 from the origin along the edge and depth into the sheet.
/// </summary>
public class ClampZonePlacementFrameTests
{
    private const double SheetWidth = 81;
    private const double SheetLength = 120;

    private static int XSign(int quadrant) => quadrant is 2 or 3 ? -1 : 1;
    private static int YSign(int quadrant) => quadrant is 3 or 4 ? -1 : 1;

    private static Box WorldFootprint(int quadrant, double center, double width, double depth)
    {
        var near = center - width / 2;
        var far = center + width / 2;
        var x = XSign(quadrant) > 0 ? near : -far;
        var y = YSign(quadrant) > 0 ? 0 : -depth;
        return new Box(x, y, width, depth);
    }

    private static Plate RailPlate(int quadrant, params double[] centers)
    {
        var plate = new Plate(SheetWidth, SheetLength) { Quadrant = quadrant };
        plate.SetClampRail(new ClampRail(1.5, 0.625, centers));
        return plate;
    }

    private static Nest NestOf(Plate plate)
    {
        var nest = new Nest();
        nest.Plates.Add(plate);
        return nest;
    }

    private static Part Rectangle(double length, double width, Vector location, bool scribe = false)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(length, 0);
        program.LineTo(length, width);
        program.LineTo(0, width);
        program.LineTo(0, 0);
        if (scribe)
            foreach (var line in program.Codes.OfType<LinearMove>())
                line.Layer = LayerType.Scribe;
        return new Part(new Drawing(scribe ? "scribe" : "rectangle", program), location);
    }

    private static void AssertSameBox(Box expected, Box actual)
    {
        Assert.Equal(expected.Left, actual.Left, 9);
        Assert.Equal(expected.Bottom, actual.Bottom, 9);
        Assert.Equal(expected.Length, actual.Length, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ToPlacement_MapsWholeFootprintsWithoutChangingStoredZones(int quadrant)
    {
        var plate = RailPlate(quadrant, 35, 120.5);
        var stored = plate.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y)).ToArray();

        var mapped = ClampZoneFrame.ToPlacement(plate);

        Assert.Equal(new[] { "clamp-1", "clamp-2" }, mapped.Select(z => z.Id));
        AssertSameBox(WorldFootprint(quadrant, 35, 1.5, 0.625), mapped[0].Bounds);
        AssertSameBox(WorldFootprint(quadrant, 120.5, 1.5, 0.625), mapped[1].Bounds);
        Assert.Equal(stored, plate.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y)));
        Assert.All(mapped, z => Assert.DoesNotContain(z, plate.ClampZones));
    }

    [Fact]
    public void ToPlacement_RejectsMissingOrMalformedZones()
    {
        var plate = new Plate(24, 48);
        var zone = new ClampZone("front", new Box(10, 0, 1.5, 0.625));
        plate.ClampZones.Add(zone);
        zone.Bounds.Width = -1;
        Assert.Throws<ArgumentException>(() => ClampZoneFrame.ToPlacement(plate));
        zone.Bounds.Width = 0.625;
        zone.Bounds.X = double.NaN;
        Assert.Throws<ArgumentException>(() => ClampZoneFrame.ToPlacement(plate));
        plate.ClampZones.Clear();
        plate.ClampZones.Add(null!);
        Assert.Throws<ArgumentException>(() => ClampZoneFrame.ToPlacement(plate));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ManualCutoffsAcrossRailClampAreFoundInEveryQuadrant(int quadrant)
    {
        var plate = RailPlate(quadrant, 35);
        var footprint = WorldFootprint(quadrant, 35, 1.5, 0.625);
        plate.CutOffs.Add(new CutOff(new Vector(XSign(quadrant) * 35, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(0, YSign(quadrant) * 0.3), CutOffAxis.Horizontal));
        plate.CutOffs.Add(new CutOff(new Vector(XSign(quadrant) * 50, 0), CutOffAxis.Vertical));

        var findings = ClampCutDiagnostics.Find(plate);

        Assert.Equal(new[] { 0, 1 }, findings.Select(f => f.CutOffIndex));
        Assert.All(findings, f =>
        {
            Assert.Equal("clamp-1", f.ClampId);
            Assert.InRange(f.Location.X, footprint.Left, footprint.Right);
            Assert.InRange(f.Location.Y, footprint.Bottom, footprint.Top);
        });
        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));
        Assert.Equal(2, report.CutOffClampFindings.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PlateCaptureFindsPartMaterialOnRailClampInEveryQuadrant(int quadrant)
    {
        var plate = RailPlate(quadrant, 35);
        // A 4x4 square from 33 to 37 along the edge, flush with the front edge.
        plate.Parts.Add(Rectangle(4, 4, new Vector(XSign(quadrant) > 0 ? 33 : -37,
            YSign(quadrant) > 0 ? 0 : -4)));
        plate.Parts.Add(Rectangle(4, 4, new Vector(XSign(quadrant) > 0 ? 50 : -54,
            YSign(quadrant) > 0 ? 0 : -4)));

        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));

        var finding = Assert.Single(report.ClampFindings);
        Assert.Equal(0, finding.PartAId);
        Assert.Contains("clamp-1", finding.PartBName);
        Assert.Equal(1.5 * 0.625, finding.Area, 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PostingWarnsForCutoffRapidAndPartOnRailClampInEveryQuadrant(int quadrant)
    {
        var cutPlate = RailPlate(quadrant, 35);
        cutPlate.CutOffs.Add(new CutOff(new Vector(XSign(quadrant) * 35, 0), CutOffAxis.Vertical));
        cutPlate.RegenerateCutOffs(new CutOffSettings());
        var cutReport = PostVerificationAnalyzer.Analyze(NestOf(cutPlate));
        Assert.Contains(cutReport.Findings, f => f.Kind == PostVerificationKind.Overlap && f.ClampId == "clamp-1");
        Assert.False(cutReport.CanPost(false));
        Assert.True(cutReport.CanPost(true));

        // The incoming rapid (0,0) -> (+/-40, +/-0.3) passes through the clamp at 35.
        var rapidPlate = RailPlate(quadrant, 35);
        rapidPlate.Parts.Add(Rectangle(0.1, 0.1,
            new Vector(XSign(quadrant) * 40, YSign(quadrant) * 0.3), scribe: true));
        var rapidReport = PostVerificationAnalyzer.Analyze(NestOf(rapidPlate));
        var rapid = Assert.Single(rapidReport.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Equal("clamp-1", rapid.ClampId);
        Assert.False(rapidReport.CanPost(false));

        var partPlate = RailPlate(quadrant, 35);
        partPlate.Parts.Add(Rectangle(4, 4, new Vector(XSign(quadrant) > 0 ? 33 : -37,
            YSign(quadrant) > 0 ? 0 : -4)));
        var partReport = PostVerificationAnalyzer.Analyze(NestOf(partPlate));
        Assert.Contains(partReport.Findings, f => f.Kind == PostVerificationKind.Overlap
            && f.PartNumber == 1 && f.OtherPartNumber == null && f.Overlap != null
            && f.Message.Contains("clamp-1"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RailClampsAwayFromCutoffsAndRapidsProduceNoClampWarnings(int quadrant)
    {
        // The cut-off and every rapid stay between the origin and the clamp at 35.
        var plate = RailPlate(quadrant, 35);
        plate.CutOffs.Add(new CutOff(new Vector(XSign(quadrant) * 20, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        plate.Parts.Add(Rectangle(0.1, 0.1, new Vector(XSign(quadrant) * 10, YSign(quadrant) * 5),
            scribe: true));
        var report = PostVerificationAnalyzer.Analyze(NestOf(plate));
        Assert.DoesNotContain(report.Findings, f => f.ClampId != null || f.Message.Contains("clamp"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("null")]
    public void MalformedActiveClampMakesPostingIncompleteEvenWithoutRapidsOrCutoffs(string invalid)
    {
        var plate = new Plate(24, 48);
        if (invalid == "null")
            plate.ClampZones.Add(null!);
        else
        {
            var zone = new ClampZone("front", new Box(10, 0, 1.5, 0.625));
            plate.ClampZones.Add(zone);
            if (invalid == "NaN")
                zone.Bounds.Y = double.NaN;
            else if (invalid == "negative")
                zone.Bounds.Width = -1;
            else
            {
                zone.Bounds.X = 1e308;
                zone.Bounds.Length = 1e308;
            }
        }
        // Cut-first scribe: no nonzero rapid, no cut-off definitions.
        var program = new Program();
        program.MoveTo(0, 0);
        program.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Scribe });
        plate.Parts.Add(new Part(new Drawing("scribe", program)));

        var report = PostVerificationAnalyzer.Analyze(NestOf(plate));

        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Incomplete
            && f.Message.StartsWith("Clamp check:"));
        Assert.False(report.CanPost(false));
        Assert.Throws<ArgumentException>(() => PlateOverlapAnalyzer.CapturePlate(plate));
    }

    [Fact]
    public void QuadrantChangeMovesExplicitZonesAndStalesDisplayedClampFindings()
    {
        var plate = new Plate(24, 48);
        plate.ClampZones.Add(new ClampZone("front", new Box(10, 0, 1.5, 0.625)));
        plate.Parts.Add(Rectangle(4, 4, new Vector(9.25, 0)));
        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));
        Assert.Single(report.ClampFindings);
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, report));
        Assert.Single(state.DisplayPairs);

        // The stored zone bits are unchanged, but in quadrant 4 the zone is at Y -24..-23.375.
        plate.Quadrant = 4;

        Assert.False(state.EnsureFresh(plate));
        Assert.Empty(state.DisplayPairs);
    }
}
