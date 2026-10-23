using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenNest.Geometry;
using static OpenNest.IO.Tests.NestArchiveJson;

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
    public void ChangedDefaultsMoveUntouchedSentinelAndSaveNoEmptySheets()
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.PlateDefaults.Quadrant = 1;
        nest.SetClampRail(Machine());
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        var sentinel = Assert.Single(nest.Plates);

        manager.ChangePlateDefaults(() =>
        {
            nest.PlateDefaults.Size = new Size(48, 60);
            nest.PlateDefaults.Quadrant = 3;
            nest.PlateDefaults.EdgeSpacing = new Spacing(1, 2, 3, 4);
            nest.PlateDefaults.PartSpacing = 0.5;
        });
        manager.EnsureSentinel();

        Assert.Same(sentinel, Assert.Single(nest.Plates));
        using var stream = Write(nest);
        using var json = JsonDocument.Parse(ReadNestJson(stream));
        Assert.Equal(0, json.RootElement.GetProperty("plates").GetArrayLength());
        stream.Position = 0;
        var reopened = new NestReader(stream).Read();
        Assert.Empty(reopened.Plates);
        using var reopenedManager = new PlateManager(reopened);
        reopenedManager.EnsureSentinel();
        var newSentinel = Assert.Single(reopened.Plates);
        Assert.Equal(new Size(48, 60), newSentinel.Size);
        Assert.Equal(3, newSentinel.Quadrant);
        Assert.Equal(new Spacing(1, 2, 3, 4), newSentinel.EdgeSpacing);
        Assert.Equal(0.5, newSentinel.PartSpacing);
        Assert.Same(reopened.PlateDefaults.ClampRail, newSentinel.ClampRail);
        Assert.True(newSentinel.ClampRail.Matches(newSentinel));
        Assert.Equal(4, newSentinel.ClampZones.Count);
        Assert.Equal(53.25, newSentinel.ClampZones[0].Bounds.X);
        Assert.Equal(47.375, newSentinel.ClampZones[0].Bounds.Y);
        using var secondSave = Write(reopened);
        using var secondJson = JsonDocument.Parse(ReadNestJson(secondSave));
        Assert.Equal(0, secondJson.RootElement.GetProperty("plates").GetArrayLength());
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("middle")]
    [InlineData("removed")]
    [InlineData("cutOff")]
    [InlineData("explicit")]
    public void ChangedDefaultsPreserveAndWriteConfiguredEmptySheets(string configuration)
    {
        var nest = RailNest(withEmptyTail: false);
        var plate = nest.CreatePlate();
        switch (configuration)
        {
            case "edited":
                plate.Size = new Size(48, 84);
                plate.Quadrant = 2;
                break;
            case "middle":
                var separator = nest.CreatePlate();
                separator.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
                nest.CreatePlate();
                break;
            case "removed":
                plate.SetClampRail(nest.PlateDefaults.ClampRail, new[] { "clamp-1" });
                break;
            case "cutOff":
                plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
                break;
            case "explicit":
                plate.SetClampRail(null!);
                plate.ClampZones.Add(new ClampZone("manual", new Box(10, 0, 2, 1)));
                break;
        }
        var oldSize = plate.Size;
        var oldQuadrant = plate.Quadrant;
        var oldZones = plate.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)).ToArray();
        using var manager = new PlateManager(nest);

        manager.ChangePlateDefaults(() =>
        {
            nest.PlateDefaults.Size = new Size(48, 60);
            nest.PlateDefaults.Quadrant = 3;
        });

        Assert.Equal(oldSize, plate.Size);
        Assert.Equal(oldQuadrant, plate.Quadrant);
        var reopened = RoundTrip(nest);
        Assert.Equal(configuration == "middle" ? 3 : 2, reopened.Plates.Count);
        var kept = reopened.Plates[1];
        Assert.Equal(oldSize, kept.Size);
        Assert.Equal(oldQuadrant, kept.Quadrant);
        Assert.Equal(oldZones, kept.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)));
        if (configuration == "removed")
            Assert.Equal(new[] { "clamp-1" }, kept.RemovedClamps);
        if (configuration == "cutOff")
            Assert.Single(kept.CutOffs);
        if (configuration == "explicit")
            Assert.Null(kept.ClampRail);
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
        // Sheets without a removal are written without the key, exactly like a file from
        // before the feature.
        var withoutKey = Write(RailNest());
        Assert.DoesNotContain("\"removedClamps\"", ReadNestJson(withoutKey), StringComparison.Ordinal);

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

    // PM 6ded8ac4 (b): an invalid removal time falls back to the default instead of
    // dropping the rail and every sheet's removal state with it.
    [Theory]
    [InlineData("-1")]
    [InlineData("\"x\"")]
    public void InvalidRemovalMinutesFallBackToTheDefault(string minutes)
    {
        var tampered = RewriteNestJson(RemovedClamp3Nest(), text =>
        {
            var root = JsonNode.Parse(text)!;
            root["plateDefaults"]!["clampRail"]!["removalMinutes"] = JsonNode.Parse(minutes);
            return root.ToJsonString();
        });

        var reopened = new NestReader(tampered).Read();

        Assert.NotNull(reopened.PlateDefaults.ClampRail);
        Assert.Equal(ClampRail.DefaultRemovalMinutes, reopened.PlateDefaults.ClampRail.RemovalMinutes);
        var plate = Assert.Single(reopened.Plates);
        Assert.Equal(new[] { "clamp-3" }, plate.RemovedClamps);
        Assert.NotNull(plate.ClampRail);
    }

    // PM 6ded8ac4 (c): sheets without a removal write no removedClamps key at all.
    [Fact]
    public void SheetsWithoutRemovedClampsWriteNoRemovedClampsKey()
    {
        var json = ReadNestJson(Write(RailNest()));

        Assert.DoesNotContain("\"removedClamps\"", json, StringComparison.Ordinal);
        Assert.Contains("\"removedClamps\"", ReadNestJson(RemovedClamp3Nest()), StringComparison.Ordinal);
    }

    // PM 22daec3d: a sheet following a rail other than the nest default keeps its rail,
    // removed clamps and removal time through save/reload.
    [Fact]
    public void SheetRailDifferentFromTheDefaultsSurvivesReload()
    {
        var nest = new Nest("no-default-rail");
        nest.PlateDefaults.Size = new Size(48, 120);
        var plate = nest.CreatePlate();
        plate.SetClampRail(Machine(), new[] { "clamp-3" });

        var reopened = Assert.Single(RoundTrip(nest).Plates);

        Assert.Equal(Machine(), reopened.ClampRail);
        Assert.Equal(new[] { "clamp-3" }, reopened.RemovedClamps);
        Assert.Equal(ClampRail.DefaultRemovalMinutes, reopened.ClampRemovalMinutes);
        Assert.Null(RoundTrip(nest).PlateDefaults.ClampRail);
    }

    [Fact]
    public void SheetsFollowingTheDefaultRailWriteNoPerSheetRail()
    {
        var json = ReadNestJson(RemovedClamp3Nest());
        var root = JsonNode.Parse(json)!;
        Assert.Null(root["plates"]![0]!["clampRail"]);
    }

    // PM 455f6763 (C17): an empty rail sheet whose spacing differs from the defaults is
    // configured stock, not the editor's sentinel, and is written.
    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.0, 0.125)]
    public void EmptyRailSheetWithOtherSpacingIsKept(double partSpacing, double edge)
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();
        plate.PartSpacing = partSpacing;
        plate.EdgeSpacing = new Spacing(edge, edge, edge, edge);

        var kept = Assert.Single(RoundTrip(nest).Plates);

        Assert.Equal(partSpacing, kept.PartSpacing);
        Assert.Equal(edge, kept.EdgeSpacing.Left);
    }

    // Delta review: a sheet whose rail differs from the defaults only in removal time is
    // configured stock too (ChangePlateDefaults leaves it alone) and must be written.
    [Fact]
    public void EmptySheetOnARailWithOtherRemovalTimeIsKept()
    {
        var nest = new Nest("railed");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();
        plate.SetClampRail(new ClampRail(1.5, 0.625, MachineCenters, removalMinutes: 7));

        var kept = Assert.Single(RoundTrip(nest).Plates);

        Assert.Equal(7, kept.ClampRail.RemovalMinutes);
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
}
