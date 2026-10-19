using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// Rail clamps on every sheet edge (<see cref="ClampEdge"/>): positions measured from the
/// plate origin along each edge, footprints for every quadrant, ids that keep origin-side
/// rails unchanged, and the settings/editor round trips behind the Clamps dialog.
/// </summary>
public class ClampEdgeTests
{
    private static readonly Size Sheet = new(48, 120); // Width (Y) 48, Length (X) 120

    private static ClampRail AllEdges() => new(2, 0.5, new[]
    {
        new RailClamp(ClampEdge.OriginSide, 10),
        new RailClamp(ClampEdge.FarSide, 20),
        new RailClamp(ClampEdge.OriginEnd, 5),
        new RailClamp(ClampEdge.FarEnd, 30),
    });

    private static Box Zone(ClampRail rail, int quadrant, int index) =>
        rail.ResolveFor(Sheet, quadrant).Single(z => z.Id == ClampRail.IdOf(index)).Bounds;

    private static void AssertBox(Box expected, Box actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Length, actual.Length, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
    }

    [Fact]
    public void QuadrantOneFootprintsSitOnEachEdge()
    {
        var rail = AllEdges();
        AssertBox(new Box(9, 0, 2, 0.5), Zone(rail, 1, 0));          // origin side: bottom
        AssertBox(new Box(19, 47.5, 2, 0.5), Zone(rail, 1, 1));      // far side: top
        AssertBox(new Box(0, 4, 0.5, 2), Zone(rail, 1, 2));          // origin end: left
        AssertBox(new Box(119.5, 29, 0.5, 2), Zone(rail, 1, 3));     // far end: right
    }

    [Fact]
    public void QuadrantThreeMirrorsEveryEdgeAboutTheOrigin()
    {
        // Quadrant 3: the origin is the sheet's upper-right corner.
        var rail = AllEdges();
        AssertBox(new Box(109, 47.5, 2, 0.5), Zone(rail, 3, 0));     // origin side: top
        AssertBox(new Box(99, 0, 2, 0.5), Zone(rail, 3, 1));         // far side: bottom
        AssertBox(new Box(119.5, 42, 0.5, 2), Zone(rail, 3, 2));     // origin end: right
        AssertBox(new Box(0, 17, 0.5, 2), Zone(rail, 3, 3));         // far end: left
    }

    [Fact]
    public void QuadrantTwoAndFourFlipOneAxis()
    {
        var rail = AllEdges();
        AssertBox(new Box(109, 0, 2, 0.5), Zone(rail, 2, 0));
        AssertBox(new Box(0, 29, 0.5, 2), Zone(rail, 2, 3));
        AssertBox(new Box(9, 47.5, 2, 0.5), Zone(rail, 4, 0));
        AssertBox(new Box(0, 42, 0.5, 2), Zone(rail, 4, 2));
    }

    [Fact]
    public void EndClampsBeyondTheSheetWidthAreSkipped()
    {
        var rail = new ClampRail(2, 0.5, new[]
        {
            new RailClamp(ClampEdge.OriginEnd, 47.5), // still reaches onto a 48" wide sheet
            new RailClamp(ClampEdge.OriginEnd, 50),   // wholly beyond it
        });

        var zone = Assert.Single(rail.ResolveFor(Sheet, 1));
        Assert.Equal("clamp-1", zone.Id);
        Assert.Equal(2, rail.ResolveFor(new Size(60, 120), 1).Count);
    }

    [Fact]
    public void ClampsOrderByEdgeSoOriginSideIdsAreUnchanged()
    {
        var originOnly = new ClampRail(2, 0.5, new double[] { 30, 10 });
        var mixed = originOnly.WithClamps(originOnly.Clamps.Append(new RailClamp(ClampEdge.FarSide, 1)));

        Assert.Equal(new double[] { 10, 30 }, mixed.Centers);
        Assert.Equal(ClampEdge.FarSide, mixed.Clamps[2].Edge);
        var ids = mixed.ResolveFor(Sheet, 1).Select(z => z.Id).ToArray();
        Assert.Equal(new[] { "clamp-1", "clamp-2", "clamp-3" }, ids);
        AssertBox(Zone(originOnly, 1, 1), Zone(mixed, 1, 1));
    }

    [Fact]
    public void EdgeIsPartOfEquality()
    {
        var a = new ClampRail(2, 0.5, new[] { new RailClamp(ClampEdge.OriginSide, 10) });
        var b = new ClampRail(2, 0.5, new[] { new RailClamp(ClampEdge.FarSide, 10) });
        Assert.NotEqual(a, b);
        Assert.Equal(a, new ClampRail(2, 0.5, new double[] { 10 }));
    }

    [Fact]
    public void NestKeepsARailWithOnlyOtherEdgeClamps()
    {
        var nest = new Nest("edges");
        nest.PlateDefaults.Size = Sheet;
        var plate = nest.CreatePlate();
        nest.SetClampRail(new ClampRail(2, 0.5, new[] { new RailClamp(ClampEdge.FarEnd, 12) }));

        Assert.NotNull(nest.PlateDefaults.ClampRail);
        AssertBox(new Box(119.5, 11, 0.5, 2), Assert.Single(plate.ClampZones).Bounds);
    }

    [Fact]
    public void SettingsRoundTripEveryEdgeAndOmitUnusedKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenNestTests", Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "clamps.json");
        try
        {
            var settings = ClampSettings.FromRail(AllEdges(), Units.Inches, enabled: true);
            settings.Save(path);
            var loaded = ClampSettings.Load(path, out var status);

            Assert.Equal(ClampSettingsStatus.Ok, status);
            Assert.Equal(AllEdges(), loaded.RailFor(Units.Inches));
            Assert.Equal(4, loaded.ClampCount);

            ClampSettings.FromRail(new ClampRail(2, 0.5, new double[] { 10 }), Units.Inches, true).Save(path);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("farSideCenters", json);
            Assert.DoesNotContain("originEndCenters", json);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsConvertEveryEdgeBetweenUnits()
    {
        var settings = ClampSettings.FromRail(AllEdges(), Units.Inches, enabled: true);
        var rail = settings.RailFor(Units.Millimeters)!;
        Assert.Equal(30 * 25.4, rail.Clamps.Single(c => c.Edge == ClampEdge.FarEnd).Center, 9);
    }

    [Fact]
    public void EditorAddsClampsPerEdgeAndRefusesDuplicatesOnTheSameEdge()
    {
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: false);

        Assert.True(editor.TryAddClamp(new RailClamp(ClampEdge.FarSide, 10), out _));
        Assert.True(editor.TryAddCenter(10, out _)); // same distance, different edge
        Assert.False(editor.TryAddClamp(new RailClamp(ClampEdge.FarSide, 10), out var error));
        Assert.NotNull(error);

        Assert.Equal(ClampEdge.OriginSide, editor.Clamps[0].Edge);
        editor.RemoveCenterAt(1);
        Assert.Equal(new double[] { 10 }, editor.Centers);
        Assert.Equal(ClampEdge.OriginSide, Assert.Single(editor.BuildRail()!.Clamps).Edge);

        editor.TryAddClamp(new RailClamp(ClampEdge.OriginEnd, 4), out _);
        var saved = editor.ToMachineDefaults();
        Assert.Equal(new double[] { 4 }, saved.OriginEndCenters);
    }
}
