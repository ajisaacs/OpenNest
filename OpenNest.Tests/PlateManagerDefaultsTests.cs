using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PlateManagerDefaultsTests
{
    private static Nest CreateNest(bool withRail = true)
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.PlateDefaults.Quadrant = 1;
        nest.PlateDefaults.EdgeSpacing = new Spacing(1, 2, 3, 4);
        nest.PlateDefaults.PartSpacing = 0.25;
        if (withRail)
            nest.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0, 81.0 }));
        return nest;
    }

    private static void ChangeDefaults(Nest nest)
    {
        nest.PlateDefaults.Size = new Size(48, 60);
        nest.PlateDefaults.Quadrant = 3;
        nest.PlateDefaults.EdgeSpacing = new Spacing(5, 6, 7, 8);
        nest.PlateDefaults.PartSpacing = 0.5;
    }

    private static void AssertMatchesDefaults(Nest nest, Plate plate)
    {
        Assert.Equal(nest.PlateDefaults.Size, plate.Size);
        Assert.Equal(nest.PlateDefaults.Quadrant, plate.Quadrant);
        Assert.Equal(nest.PlateDefaults.EdgeSpacing, plate.EdgeSpacing);
        Assert.Equal(nest.PlateDefaults.PartSpacing, plate.PartSpacing);
        Assert.Same(nest.PlateDefaults.ClampRail, plate.ClampRail);
        Assert.Empty(plate.RemovedClamps);
        if (plate.ClampRail == null)
            Assert.Empty(plate.ClampZones);
        else
            Assert.True(plate.ClampRail.Matches(plate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangePlateDefaults_MovesUntouchedTrailingSheet(bool withRail)
    {
        var nest = CreateNest(withRail);
        using var manager = new PlateManager(nest);
        manager.EnsureSentinel();
        var sentinel = Assert.Single(nest.Plates);
        var listChanges = 0;
        var currentChanges = 0;
        manager.PlateListChanged += (_, _) => listChanges++;
        manager.CurrentPlateChanged += (_, e) =>
        {
            Assert.Same(sentinel, e.Plate);
            currentChanges++;
        };

        manager.ChangePlateDefaults(() => ChangeDefaults(nest));

        Assert.Same(sentinel, Assert.Single(nest.Plates));
        AssertMatchesDefaults(nest, sentinel);
        Assert.Equal(0, manager.CurrentIndex);
        Assert.Equal(1, listChanges);
        Assert.Equal(1, currentChanges);
        if (withRail)
        {
            // Independent physical oracle: Q3 origin clamp is at the sheet's upper right.
            var zone = Assert.Single(sentinel.ClampZones);
            Assert.Equal("clamp-1", zone.Id);
            Assert.Equal(53.25, zone.Bounds.X);
            Assert.Equal(47.375, zone.Bounds.Y);
            Assert.Equal(1.5, zone.Bounds.Length);
            Assert.Equal(0.625, zone.Bounds.Width);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangePlateDefaults_ReappliesTheNewRail(bool removeRail)
    {
        var nest = CreateNest();
        var sentinel = nest.CreatePlate();
        using var manager = new PlateManager(nest);
        var newRail = removeRail ? null : new ClampRail(2, 1, new[] { 10.0 });

        manager.ChangePlateDefaults(() =>
        {
            ChangeDefaults(nest);
            nest.PlateDefaults.ClampRail = newRail;
        });

        AssertMatchesDefaults(nest, sentinel);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("length")]
    [InlineData("quadrant")]
    [InlineData("left")]
    [InlineData("bottom")]
    [InlineData("right")]
    [InlineData("top")]
    [InlineData("partSpacing")]
    [InlineData("removed")]
    [InlineData("explicit")]
    [InlineData("detachedMatchingZones")]
    [InlineData("differentRail")]
    [InlineData("editedRailZones")]
    [InlineData("cutOff")]
    [InlineData("part")]
    public void ChangePlateDefaults_PreservesEditedOrUsedTrailingSheet(string edit)
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        switch (edit)
        {
            // Even sub-tolerance changes are edits, not untouched defaults.
            case "width": plate.Size = new Size(48 + 1e-9, 96); break;
            case "length": plate.Size = new Size(48, 96 + 1e-9); break;
            case "quadrant": plate.Quadrant = 2; break;
            case "left": plate.EdgeSpacing.Left += 1e-9; break;
            case "bottom": plate.EdgeSpacing.Bottom += 1e-9; break;
            case "right": plate.EdgeSpacing.Right += 1e-9; break;
            case "top": plate.EdgeSpacing.Top += 1e-9; break;
            case "partSpacing": plate.PartSpacing += 1e-9; break;
            case "removed": plate.SetClampRail(nest.PlateDefaults.ClampRail, new[] { "clamp-1" }); break;
            case "explicit":
                plate.SetClampRail(null!);
                plate.ClampZones.Add(new ClampZone("manual", new Box(10, 0, 2, 1)));
                break;
            case "detachedMatchingZones":
                var zones = plate.ClampZones.ToArray();
                plate.SetClampRail(null!);
                foreach (var zone in zones)
                    plate.ClampZones.Add(zone);
                break;
            case "differentRail":
                plate.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0, 81.0 }, removalMinutes: 8));
                break;
            case "editedRailZones":
                plate.ClampZones.Clear();
                plate.ClampZones.Add(new ClampZone("manual", new Box(10, 0, 2, 1)));
                break;
            case "cutOff": plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical)); break;
            case "part": plate.Parts.Add(new Part(new Drawing("block", new Program()))); break;
        }
        var size = plate.Size;
        var quadrant = plate.Quadrant;
        var spacing = plate.EdgeSpacing;
        var partSpacing = plate.PartSpacing;
        var rail = plate.ClampRail;
        var removed = plate.RemovedClamps.ToArray();
        var savedZones = plate.ClampZones.ToArray();
        var parts = plate.Parts.ToArray();
        var cutOffs = plate.CutOffs.ToArray();
        using var manager = new PlateManager(nest);
        var notifications = 0;
        manager.CurrentPlateChanged += (_, _) => notifications++;
        manager.PlateListChanged += (_, _) => notifications++;

        manager.ChangePlateDefaults(() => ChangeDefaults(nest));

        Assert.Equal(new Size(48, 60), nest.PlateDefaults.Size);
        Assert.Same(plate, Assert.Single(nest.Plates));
        Assert.Equal(size, plate.Size);
        Assert.Equal(quadrant, plate.Quadrant);
        Assert.Equal(spacing, plate.EdgeSpacing);
        Assert.Equal(partSpacing, plate.PartSpacing);
        Assert.Same(rail, plate.ClampRail);
        Assert.Equal(removed, plate.RemovedClamps);
        Assert.Equal(savedZones, plate.ClampZones);
        Assert.Equal(parts, plate.Parts);
        Assert.Equal(cutOffs, plate.CutOffs);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ChangePlateDefaults_PreservesMiddleEmptySheetAndCurrentSelection()
    {
        var nest = CreateNest();
        var middle = nest.CreatePlate();
        var sentinel = nest.CreatePlate();
        using var manager = new PlateManager(nest);
        manager.LoadFirst();
        var currentChanges = 0;
        manager.CurrentPlateChanged += (_, _) => currentChanges++;

        manager.ChangePlateDefaults(() => ChangeDefaults(nest));

        Assert.Equal(2, nest.Plates.Count);
        Assert.Equal(new Size(48, 96), middle.Size);
        Assert.Equal(1, middle.Quadrant);
        Assert.Equal(new Spacing(1, 2, 3, 4), middle.EdgeSpacing);
        Assert.Equal(0.25, middle.PartSpacing);
        Assert.Equal(2, middle.ClampZones.Count);
        AssertMatchesDefaults(nest, sentinel);
        Assert.Same(middle, manager.CurrentPlate);
        Assert.Equal(0, currentChanges);
    }

    [Fact]
    public void ChangePlateDefaults_SetFromExistingMovesOnlyTheSentinel()
    {
        var nest = CreateNest();
        var selected = nest.CreatePlate();
        selected.Size = new Size(48, 60);
        selected.Quadrant = 3;
        selected.EdgeSpacing = new Spacing(5, 6, 7, 8);
        selected.PartSpacing = 0.5;
        var sentinel = nest.CreatePlate();
        using var manager = new PlateManager(nest);

        manager.ChangePlateDefaults(() => nest.PlateDefaults.SetFromExisting(selected));

        AssertMatchesDefaults(nest, sentinel);
        AssertMatchesDefaults(nest, selected);
        Assert.Equal(2, nest.Plates.Count);
        Assert.Same(selected, manager.CurrentPlate);
    }

    [Fact]
    public void ChangePlateDefaults_WithoutPlatesStillAppliesDefaults()
    {
        var nest = CreateNest();
        using var manager = new PlateManager(nest);

        manager.ChangePlateDefaults(() => ChangeDefaults(nest));

        Assert.Empty(nest.Plates);
        manager.EnsureSentinel();
        AssertMatchesDefaults(nest, Assert.Single(nest.Plates));
    }
}
