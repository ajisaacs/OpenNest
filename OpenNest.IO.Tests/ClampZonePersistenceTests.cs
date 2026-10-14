using System.IO.Compression;
using System.Text;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// Clamp zones survive the .nest round trip; files without the "clamps" key load
/// unchanged with zero zones, and a clamp-carrying sheet is never dropped by the
/// empty-plate filter unless the nest's saved rail recreates exactly those zones
/// (see ClampRailPersistenceTests). Malformed zones are skipped rather than poisoning the file.
/// </summary>
public class ClampZonePersistenceTests
{
    private static Nest NestWithClamps(int zoneCount)
    {
        var nest = new Nest("clamped");
        var plate = new Plate(48, 96);
        for (var i = 0; i < zoneCount; i++)
            plate.ClampZones.Add(new ClampZone($"clamp-{i}", new Box(2.25 + 10 * i, 0, 1.5, 0.625)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static Nest RoundTrip(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        return new NestReader(stream).Read();
    }

    [Fact]
    public void ZonesRoundTripWithIdsAndPositions()
    {
        var reopened = RoundTrip(NestWithClamps(8));

        var plate = Assert.Single(reopened.Plates);
        Assert.Equal(8, plate.ClampZones.Count);
        for (var i = 0; i < 8; i++)
        {
            var zone = plate.ClampZones[i];
            Assert.Equal($"clamp-{i}", zone.Id);
            Assert.Equal(2.25 + 10 * i, zone.Bounds.X, 9);
            Assert.Equal(0, zone.Bounds.Y, 9);
            Assert.Equal(1.5, zone.Bounds.Length, 9);
            Assert.Equal(0.625, zone.Bounds.Width, 9);
        }
    }

    [Fact]
    public void ClampOnlyPlateSurvivesTheEmptyPlateFilter()
    {
        // A sheet with no parts and no cut-offs but with clamp definitions is a real
        // configuration (the machine rail exists even on an empty plate) and must persist.
        var reopened = RoundTrip(NestWithClamps(3));

        var plate = Assert.Single(reopened.Plates);
        Assert.Empty(plate.Parts);
        Assert.Equal(3, plate.ClampZones.Count);
    }

    [Fact]
    public void EmptyPlateWithoutClampsIsStillOmitted()
    {
        var nest = new Nest("empty");
        nest.Plates.Add(new Plate(48, 96));
        var reopened = RoundTrip(nest);
        Assert.Empty(reopened.Plates);
    }

    [Fact]
    public void ZonesCoexistWithPlacementsAndCutOffs()
    {
        var nest = new Nest("mixed");
        var plate = new Plate(48, 96);
        plate.ClampZones.Add(new ClampZone("clamp-0", new Box(5.25, 0, 1.5, 0.625)));
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
        nest.Plates.Add(plate);

        var reopened = RoundTrip(nest);

        var reopenedPlate = Assert.Single(reopened.Plates);
        Assert.Single(reopenedPlate.ClampZones);
        Assert.Equal("clamp-0", reopenedPlate.ClampZones[0].Id);
        Assert.Single(reopenedPlate.CutOffs);
    }

    [Fact]
    public void FileWithoutClampsKeyLoadsUnchanged()
    {
        // Files written before this feature have no "clamps" property at all.
        var withZones = new MemoryStream();
        Assert.True(new NestWriter(NestWithClamps(2)).Write(withZones));
        var legacy = RewriteNestJson(withZones,
            json => System.Text.RegularExpressions.Regex.Replace(json, @",\s*""clamps""\s*:\s*\[[^\]]*\]", ""));

        legacy.Position = 0;
        var reopened = new NestReader(legacy).Read();

        var plate = Assert.Single(reopened.Plates);
        Assert.Empty(plate.Parts);
        Assert.Empty(plate.ClampZones);
    }

    [Fact]
    public void MalformedZonesAreSkippedWithoutThrowing()
    {
        // Hand-edited sizes must not poison the file: drop bad entries, keep good ones.
        var withZones = new MemoryStream();
        Assert.True(new NestWriter(NestWithClamps(1)).Write(withZones));
        var tampered = RewriteNestJson(withZones, json => json.Replace(
            "\"clamps\": [",
            "\"clamps\": [{\"id\":\"bad\",\"x\":0,\"y\":0,\"length\":-5,\"width\":0.625},"
                + "{\"id\":\"\",\"x\":0,\"y\":0,\"length\":1.5,\"width\":0.625},")
        );

        tampered.Position = 0;
        var reopened = new NestReader(tampered).Read();

        var plate = Assert.Single(reopened.Plates);
        var zone = Assert.Single(plate.ClampZones);
        Assert.Equal("clamp-0", zone.Id);
    }

    [Fact]
    public void WriterEmitsCamelCaseClampsArray()
    {
        var withZones = new MemoryStream();
        Assert.True(new NestWriter(NestWithClamps(1)).Write(withZones));
        var json = ReadNestJson(withZones);

        Assert.Contains("\"clamps\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"clamp-0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"length\": 1.5", json, StringComparison.Ordinal);
        Assert.Contains("\"width\": 0.625", json, StringComparison.Ordinal);
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
