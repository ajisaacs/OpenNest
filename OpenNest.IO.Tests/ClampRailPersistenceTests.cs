using System.IO.Compression;
using System.Text;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// The nest's machine clamp rail persists on the plate defaults so plates created after a
/// reload still get the clamps; plates whose saved zones are the rail's follow it again,
/// anything else is preserved as saved, and an empty sheet the rail recreates is not
/// written (the editor's trailing sentinel stays out of the file).
/// </summary>
public class ClampRailPersistenceTests
{
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private static ClampRail Machine() => new(1.5, 0.625, MachineCenters);

    /// <summary>A rail nest whose first sheet is kept by a cut-off; the second is empty.</summary>
    private static Nest RailNest(bool withEmptyTail = true)
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(Machine());
        var used = nest.CreatePlate();
        used.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
        if (withEmptyTail)
            nest.CreatePlate();
        return nest;
    }

    private static MemoryStream Write(Nest nest)
    {
        var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        return stream;
    }

    private static Nest RoundTrip(Nest nest) => new NestReader(Write(nest)).Read();

    [Fact]
    public void RailRoundTripsAndPlatesFollowItAfterReload()
    {
        var reopened = RoundTrip(RailNest());

        Assert.Equal(Machine(), reopened.PlateDefaults.ClampRail);
        var plate = Assert.Single(reopened.Plates); // the empty tail was not written
        Assert.Same(reopened.PlateDefaults.ClampRail, plate.ClampRail);
        Assert.Equal(7, plate.ClampZones.Count); // 96" sheet: the clamp at 111" is skipped

        // Plates created or resized after the reload still get the machine clamps.
        Assert.Equal(7, reopened.CreatePlate().ClampZones.Count);
        plate.Size = new Size(48, 120);
        Assert.Equal(8, plate.ClampZones.Count);
    }

    [Fact]
    public void EmptyPlateWithZonesTheRailCannotRecreateIsKept()
    {
        var nest = RailNest(withEmptyTail: false);
        var odd = nest.CreatePlate();
        odd.SetClampRail(null!);
        odd.ClampZones.Add(new ClampZone("manual", new Box(10, 0, 2, 1)));

        var reopened = RoundTrip(nest);

        Assert.Equal(2, reopened.Plates.Count);
        var kept = reopened.Plates[1];
        Assert.Equal("manual", Assert.Single(kept.ClampZones).Id);
        Assert.Null(kept.ClampRail); // preserved as saved, not re-resolved
    }

    [Fact]
    public void EmptyRailSheetThatDiffersFromThePlateDefaultsIsKept()
    {
        // The rail recreates its zones, but nothing recreates this sheet's size and quadrant.
        var nest = RailNest(withEmptyTail: false);
        var odd = nest.CreatePlate();
        odd.Size = new Size(48, 60);
        odd.Quadrant = 3;

        var reopened = RoundTrip(nest);

        Assert.Equal(2, reopened.Plates.Count);
        var kept = reopened.Plates[1];
        Assert.Equal(new Size(48, 60), kept.Size);
        Assert.Equal(3, kept.Quadrant);
        Assert.Equal(4, kept.ClampZones.Count);
        Assert.Same(reopened.PlateDefaults.ClampRail, kept.ClampRail);
    }

    [Fact]
    public void SavedZonesThatDifferFromTheRailArePreservedExactly()
    {
        var nest = RailNest(withEmptyTail: false);
        var plate = nest.Plates[0];
        plate.SetClampRail(null!);
        plate.ClampZones.Add(new ClampZone("clamp-1", new Box(5.5, 0, 1.25, 0.5)));

        var reopenedPlate = Assert.Single(RoundTrip(nest).Plates);

        var zone = Assert.Single(reopenedPlate.ClampZones);
        Assert.Equal(5.5, zone.Bounds.X);
        Assert.Equal(1.25, zone.Bounds.Length);
        Assert.Null(reopenedPlate.ClampRail);
    }

    [Fact]
    public void NestWithoutRailWritesNoRailKeyAndReadsNone()
    {
        var nest = new Nest("plain");
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
        nest.Plates.Add(plate);
        var stream = Write(nest);

        Assert.DoesNotContain("clampRail", ReadNestJson(stream), StringComparison.Ordinal);
        stream.Position = 0;
        var reopened = new NestReader(stream).Read();
        Assert.Null(reopened.PlateDefaults.ClampRail);
        Assert.Empty(reopened.CreatePlate().ClampZones);
    }

    [Fact]
    public void WriterEmitsCamelCaseRailOnPlateDefaults()
    {
        var json = ReadNestJson(Write(RailNest()));

        Assert.Contains("\"clampRail\": {", json, StringComparison.Ordinal);
        Assert.Contains("\"depth\": 0.625", json, StringComparison.Ordinal);
        Assert.Contains("\"centers\": [", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"width\": 1.5", "\"width\": -1.5")]
    [InlineData("\"centers\": [", "\"centers\": [-6, ")]
    [InlineData("\"width\": 1.5", "\"width\": \"bad\"")]
    [InlineData("\"width\": 1.5", "\"width\": null")]
    [InlineData("\"centers\": [", "\"centers\": [\"bad\", ")]
    [InlineData("\"centers\": [", "\"centers\": [null, ")]
    [InlineData("\"centers\": [", "\"centers\": 6, \"unused\": [")]
    [InlineData("\"clampRail\": {", "\"clampRail\": [6], \"unused\": {")]
    public void MalformedRailIsIgnoredWhileTheNestLoads(string find, string replace)
    {
        var tampered = RewriteNestJson(Write(RailNest()), json =>
        {
            var start = json.IndexOf("\"clampRail\"", StringComparison.Ordinal);
            Assert.True(start >= 0);
            var at = json.IndexOf(find, start, StringComparison.Ordinal);
            Assert.True(at >= 0);
            return json[..at] + replace + json[(at + find.Length)..];
        });

        var reopened = new NestReader(tampered).Read();

        Assert.Null(reopened.PlateDefaults.ClampRail);
        var plate = Assert.Single(reopened.Plates);
        Assert.Equal(7, plate.ClampZones.Count); // saved zones still load, explicitly
        Assert.Null(plate.ClampRail);
    }

    private static string ReadNestJson(MemoryStream zipBytes)
    {
        zipBytes.Position = 0;
        using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("nest.json");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static MemoryStream RewriteNestJson(MemoryStream zipBytes, Func<string, string> rewrite)
    {
        zipBytes.Position = 0;
        var output = new MemoryStream();
        using (var source = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var newEntry = target.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var outStream = newEntry.Open();
                if (entry.FullName == "nest.json")
                {
                    using var text = new StreamReader(input, Encoding.UTF8);
                    using var writer = new StreamWriter(outStream, new UTF8Encoding(false));
                    writer.Write(rewrite(text.ReadToEnd()));
                }
                else
                {
                    input.CopyTo(outStream);
                }
            }
        }
        output.Position = 0;
        return output;
    }
}
