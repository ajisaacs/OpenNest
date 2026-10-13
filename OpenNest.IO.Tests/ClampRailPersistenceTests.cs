using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // D9: the rail's removal-minutes setting round-trips; a file without it defaults.

    [Fact]
    public void RemovalMinutesRoundTrips()
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(new ClampRail(1.5, 0.625, MachineCenters, removalMinutes: 8));

        var reopened = RoundTrip(nest);

        Assert.Equal(8, reopened.PlateDefaults.ClampRail.RemovalMinutes);
    }

    [Fact]
    public void MissingRemovalMinutesDefaultsOnLoad()
    {
        // The writer always emits removalMinutes; simulate an older file that predates it by
        // stripping the key directly rather than relying on exact JSON formatting elsewhere.
        var json = ReadNestJson(Write(RailNest()));
        Assert.Contains("\"removalMinutes\"", json, StringComparison.Ordinal);

        var withoutKey = RewriteNestJson(Write(RailNest()),
            text => RemoveJsonProperty(text, "removalMinutes"));

        var reopened = new NestReader(withoutKey).Read();

        Assert.Equal(ClampRail.DefaultRemovalMinutes, reopened.PlateDefaults.ClampRail.RemovalMinutes);
    }

    // D8/C13: last-resort per-sheet clamp removal persists and keeps following the rail.

    [Fact]
    public void RemovedClampsRoundTripAndTheReducedRailStillFollowsResizes()
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 120);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();
        plate.SetClampRail(Machine(), new[] { "clamp-3" });

        var reopened = RoundTrip(nest);
        var reopenedPlate = Assert.Single(reopened.Plates);

        Assert.Equal(7, reopenedPlate.ClampZones.Count);
        Assert.DoesNotContain(reopenedPlate.ClampZones, z => z.Id == "clamp-3");
        Assert.Equal(new[] { "clamp-3" }, reopenedPlate.RemovedClamps);
        Assert.Same(reopened.PlateDefaults.ClampRail, reopenedPlate.ClampRail); // still rail-following

        // The plate still follows the rail minus its removed clamp after a resize (C13).
        reopenedPlate.Size = new Size(48, 60);
        Assert.Equal(new[] { "clamp-1", "clamp-2", "clamp-4" }, reopenedPlate.ClampZones.Select(z => z.Id));
    }

    [Fact]
    public void AnEmptyPlateWithOnlyRemovedClampsIsNotDropped()
    {
        // Every clamp removed: ClampZones is empty, but the removal state must still be saved.
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0 }));
        var plate = nest.CreatePlate();
        plate.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0 }), new[] { "clamp-1" });
        Assert.Empty(plate.ClampZones);

        var reopened = RoundTrip(nest);

        var kept = Assert.Single(reopened.Plates);
        Assert.Equal(new[] { "clamp-1" }, kept.RemovedClamps);
    }

    [Fact]
    public void LegacyFileWithoutRemovedClampsKeyLoadsWithNoneRemoved()
    {
        var withoutKey = RewriteNestJson(Write(RailNest()),
            text => RemoveJsonProperty(text, "removedClamps"));

        var reopened = new NestReader(withoutKey).Read();

        var plate = Assert.Single(reopened.Plates);
        Assert.Empty(plate.RemovedClamps);
        Assert.Equal(7, plate.ClampZones.Count);
    }

    // Slice 6 task 6.4 (Slice 3b quality review): optional removal fields read leniently
    // and are only written when there is something to say.

    private static MemoryStream RemovedClamp3Nest()
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 120);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();
        plate.SetClampRail(Machine(), new[] { "clamp-3" });
        return Write(nest);
    }

    private static MemoryStream WithPlateProperty(MemoryStream file, string name, string json) =>
        RewriteNestJson(file, text =>
        {
            var root = JsonNode.Parse(text)!;
            root["plates"]![0]![name] = JsonNode.Parse(json);
            return root.ToJsonString();
        });

    // PM 9f3d676b: a wrongly shaped removedClamps value must not fail the whole nest.
    [Theory]
    [InlineData("\"clamp-3\"", false)]
    [InlineData("[-1]", false)]
    [InlineData("{\"id\": \"clamp-3\"}", false)]
    [InlineData("[-1, \"clamp-3\"]", true)]
    public void MalformedRemovedClampsLoadWithoutFailingTheNest(string value, bool keepsRemoval)
    {
        var tampered = WithPlateProperty(RemovedClamp3Nest(), "removedClamps", value);

        var plate = Assert.Single(new NestReader(tampered).Read().Plates);

        // The saved zones always load, so the removed clamp's area is never re-blocked or lost.
        Assert.Equal(7, plate.ClampZones.Count);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-3");
        Assert.Equal(keepsRemoval, plate.RemovedClamps.Contains("clamp-3"));
        Assert.Equal(keepsRemoval, plate.ClampRail != null);
    }

    /// <summary>Removes a named property wherever it appears in the document, simulating a
    /// key an older writer never emitted. Rebuilds via <see cref="JsonNode"/> so the result
    /// is always valid JSON regardless of whether the property was first, last or only one
    /// in its object - unlike string surgery, which breaks on a dangling comma.</summary>
    private static string RemoveJsonProperty(string json, string name)
    {
        var root = JsonNode.Parse(json)!;
        var removed = RemovePropertyRecursive(root, name);
        Assert.True(removed, $"Property '{name}' was not found to remove.");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool RemovePropertyRecursive(JsonNode node, string name)
    {
        switch (node)
        {
            case JsonObject obj:
                var removed = obj.Remove(name);
                foreach (var child in obj.ToList())
                    if (child.Value != null && RemovePropertyRecursive(child.Value, name))
                        removed = true;
                return removed;
            case JsonArray arr:
                var any = false;
                foreach (var item in arr)
                    if (item != null && RemovePropertyRecursive(item, name))
                        any = true;
                return any;
            default:
                return false;
        }
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
