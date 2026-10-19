using OpenNest.CNC;
using OpenNest.Collections;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PlateManagerTests
{
    private static Nest CreateNest()
    {
        var nest = new Nest("test");
        return nest;
    }

    private static Part MakePart()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing);
    }

    [Fact]
    public void Constructor_EmptyNest_CurrentIndexZero()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void Constructor_NestWithPlates_CurrentIndexZero()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void CurrentPlate_ReturnsPlateAtCurrentIndex()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        Assert.Same(plate, mgr.CurrentPlate);
    }

    [Fact]
    public void CurrentPlate_EmptyNest_ReturnsNull()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);
        Assert.Null(mgr.CurrentPlate);
    }

    [Fact]
    public void Count_DelegatesToNestPlates()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        Assert.Equal(2, mgr.Count);
    }

    [Fact]
    public void LoadFirst_SetsCurrentIndexToZero()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.LoadLast();
        mgr.LoadFirst();
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadLast_SetsCurrentIndexToLastPlate()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.LoadLast();
        Assert.Equal(2, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadNext_AdvancesIndex()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        var result = mgr.LoadNext();
        Assert.True(result);
        Assert.Equal(1, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadNext_AtEnd_ReturnsFalse()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        var result = mgr.LoadNext();
        Assert.False(result);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadPrevious_DecrementsIndex()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.LoadLast();
        var result = mgr.LoadPrevious();
        Assert.True(result);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadPrevious_AtStart_ReturnsFalse()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        var result = mgr.LoadPrevious();
        Assert.False(result);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void LoadAt_SetsExactIndex()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.LoadAt(2);
        Assert.Equal(2, mgr.CurrentIndex);
    }

    [Fact]
    public void IsFirst_WhenAtStart_ReturnsTrue()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        Assert.True(mgr.IsFirst);
    }

    [Fact]
    public void IsLast_WhenAtEnd_ReturnsTrue()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        Assert.True(mgr.IsLast);
    }

    [Fact]
    public void IsFirst_WhenNotAtStart_ReturnsFalse()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.LoadLast();
        Assert.False(mgr.IsFirst);
    }

    [Fact]
    public void Navigation_FiresCurrentPlateChanged()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        PlateChangedEventArgs received = null;
        mgr.CurrentPlateChanged += (s, e) => received = e;
        mgr.LoadNext();
        Assert.NotNull(received);
        Assert.Equal(1, received.Index);
        Assert.Same(nest.Plates[1], received.Plate);
    }

    [Fact]
    public void Dispose_UnsubscribesFromPlateEvents()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);
        var eventFired = false;
        mgr.PlateListChanged += (s, e) => eventFired = true;
        mgr.Dispose();
        nest.CreatePlate();
        Assert.False(eventFired);
    }

    // Task 3: Sentinel plate invariant

    [Fact]
    public void EnsureSentinel_EmptyNest_CreatesOnePlate()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(1, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[0].Parts.Count);
    }

    [Fact]
    public void EnsureSentinel_LastPlateHasParts_CreatesNewEmpty()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(2, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
    }

    [Fact]
    public void EnsureSentinel_TwoTrailingEmpties_TrimsToOne()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(1, nest.Plates.Count);
    }

    [Fact]
    public void EnsureSentinel_PreservesCurrentIndex()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(0, mgr.CurrentIndex);
        Assert.Same(plate, mgr.CurrentPlate);
    }

    // Task 4: Reactive tail-plate subscriptions

    [Fact]
    public void PartAddedToLastPlate_CreatesSentinel()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        var initialCount = nest.Plates.Count;
        nest.Plates[^1].Parts.Add(MakePart());
        Assert.Equal(initialCount + 1, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
    }

    [Fact]
    public void PartRemovedFromSecondToLast_TrimsExtraEmpty()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(2, nest.Plates.Count);
        nest.Plates[0].Parts.RemoveAt(0);
        Assert.Equal(1, nest.Plates.Count);
    }

    [Fact]
    public void ReactiveSubscription_ResubscribesAfterPlateListChange()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        nest.Plates[^1].Parts.Add(MakePart());
        Assert.Equal(3, nest.Plates.Count);
        nest.Plates[^1].Parts.Add(MakePart());
        Assert.Equal(4, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
    }

    // Task 5: Batch mode and plate operations

    [Fact]
    public void BeginBatch_DefersSentinelEnforcement()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();

        mgr.BeginBatch();
        nest.Plates[^1].Parts.Add(MakePart());
        Assert.Equal(2, nest.Plates.Count);
    }

    [Fact]
    public void EndBatch_EnforcesSentinelAndFiresEvents()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();

        mgr.BeginBatch();
        nest.Plates[^1].Parts.Add(MakePart());

        var listChangedCount = 0;
        mgr.PlateListChanged += (s, e) => listChangedCount++;

        mgr.EndBatch();

        Assert.Equal(3, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
        Assert.True(listChangedCount > 0);
    }

    [Fact]
    public void GetOrCreateEmpty_ReturnsSentinelWhenEmpty()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();

        var result = mgr.GetOrCreateEmpty();
        Assert.Same(nest.Plates[^1], result);
        Assert.Equal(0, result.Parts.Count);
    }

    [Fact]
    public void GetOrCreateEmpty_NoEmptyPlate_CreatesNew()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);

        var result = mgr.GetOrCreateEmpty();
        Assert.Equal(0, result.Parts.Count);
    }

    [Fact]
    public void RemoveCurrent_RemovesPlateAndClampsIndex()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        var plate2 = nest.CreatePlate();
        plate2.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.LoadLast();

        mgr.RemoveCurrent();

        // plate1 + sentinel (created by reactive sentinel enforcement)
        Assert.Equal(2, nest.Plates.Count);
        Assert.Same(plate1, nest.Plates[0]);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
        Assert.Equal(0, mgr.CurrentIndex);
    }

    [Fact]
    public void CanRemoveCurrent_OnlyIfMultiplePlatesAndHasParts()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        using var mgr = new PlateManager(nest);

        Assert.False(mgr.CanRemoveCurrent);

        plate1.Parts.Add(MakePart());
        Assert.False(mgr.CanRemoveCurrent);

        nest.CreatePlate();
        // Auto-navigated to plate2 (empty), go back to plate1 (has parts)
        mgr.LoadFirst();
        Assert.True(mgr.CanRemoveCurrent);
    }

    [Fact]
    public void ClearAll_RemovesAllButFirstAndEmptiesRemaining()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        var plate2 = nest.CreatePlate();
        plate2.Parts.Add(MakePart());
        var plate3 = nest.CreatePlate();
        plate3.Parts.Add(MakePart());
        plate3.CutOffs.Add(new CutOff(new Vector(1, 1), CutOffAxis.Vertical));
        plate3.Quantity = 5;
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(4, nest.Plates.Count);
        mgr.LoadAt(2);

        mgr.ClearAll();

        // The first plate survives, empty, with its repeat quantity reset
        Assert.Single(nest.Plates);
        Assert.Same(plate1, nest.Plates[0]);
        Assert.Empty(nest.Plates[0].Parts);
        Assert.Empty(nest.Plates[0].CutOffs);
        Assert.Equal(1, nest.Plates[0].Quantity);
        Assert.Equal(0, mgr.CurrentIndex);
        Assert.Same(plate1, mgr.CurrentPlate);
    }

    [Fact]
    public void ClearAll_NeverExposesNullCurrentPlate()
    {
        var nest = CreateNest();
        for (var i = 0; i < 3; i++)
        {
            var plate = nest.CreatePlate();
            plate.Parts.Add(MakePart());
        }
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();

        var currentPlates = new List<Plate>();
        mgr.CurrentPlateChanged += (s, e) => currentPlates.Add(e.Plate);

        mgr.ClearAll();

        // Every event during the clear reported a live plate (the one that stays)
        Assert.NotEmpty(currentPlates);
        Assert.All(currentPlates, p => Assert.NotNull(p));
        Assert.All(currentPlates, p => Assert.Same(nest.Plates[0], p));
    }

    [Fact]
    public void ClearAll_FiresPlateListAndCurrentPlateChanged()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();

        var listChanged = 0;
        var currentChanged = 0;
        mgr.PlateListChanged += (s, e) => listChanged++;
        mgr.CurrentPlateChanged += (s, e) => currentChanged++;

        mgr.ClearAll();

        Assert.True(listChanged > 0);
        Assert.True(currentChanged > 0);
    }

    [Fact]
    public void ClearAll_SinglePlateWithParts_EmptiesIt()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        plate1.CutOffs.Add(new CutOff(new Vector(2, 2), CutOffAxis.Horizontal));
        plate1.Quantity = 3;
        using var mgr = new PlateManager(nest);

        mgr.ClearAll();

        Assert.Single(nest.Plates);
        Assert.Same(plate1, nest.Plates[0]);
        Assert.Empty(plate1.Parts);
        Assert.Empty(plate1.CutOffs);
        Assert.Equal(1, plate1.Quantity);
    }

    [Fact]
    public void ClearAll_PreservesNestMetadataAndFirstPlateConfiguration()
    {
        var nest = CreateNest();
        nest.Name = "my nest";
        nest.Thickness = 0.25;
        var defaults = nest.PlateDefaults.Size;
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        // Edit the survivor away from the defaults so replacement would be visible
        plate1.Size = new Size(99, 88);
        plate1.PartSpacing = 0.4;
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);

        mgr.ClearAll();

        Assert.Equal("my nest", nest.Name);
        Assert.Equal(0.25, nest.Thickness);
        // The survivor keeps its own configuration; it was not replaced by a default plate
        Assert.Same(plate1, nest.Plates[0]);
        Assert.Equal(new Size(99, 88), nest.Plates[0].Size);
        Assert.Equal(0.4, nest.Plates[0].PartSpacing);
        Assert.Equal(defaults, nest.PlateDefaults.Size);
    }

    [Fact]
    public void ClearAll_EmptyNest_DoesNothing()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);

        mgr.ClearAll();

        Assert.Empty(nest.Plates);
    }

    [Fact]
    public void CanClearAll_ReflectsClearableState()
    {
        var nest = CreateNest();
        using var mgr = new PlateManager(nest);

        Assert.False(mgr.CanClearAll);

        var plate1 = nest.CreatePlate();
        Assert.False(mgr.CanClearAll); // a single untouched empty plate has nothing to clear

        plate1.Quantity = 2;
        Assert.True(mgr.CanClearAll);
        plate1.Quantity = 1;
        Assert.False(mgr.CanClearAll);

        plate1.CutOffs.Add(new CutOff(new Vector(0, 0), CutOffAxis.Vertical));
        Assert.True(mgr.CanClearAll);
        plate1.CutOffs.Clear();
        Assert.False(mgr.CanClearAll);

        plate1.Parts.Add(MakePart());
        Assert.Equal(2, nest.Plates.Count); // trailing sentinel created reactively
        Assert.True(mgr.CanClearAll); // multiple plates are clearable

        plate1.Parts.Clear();
        // The reactive sentinel collapses the now-trailing empty pair back to one plate
        Assert.Single(nest.Plates);
        Assert.False(mgr.CanClearAll);
    }

    [Fact]
    public void RemoveEmptyPlates_SentinelIsRestored()
    {
        var nest = CreateNest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Equal(2, nest.Plates.Count);

        // RemoveEmptyPlates removes the sentinel
        nest.Plates.RemoveEmptyPlates();

        // Sentinel should be restored reactively by OnPlateRemoved
        Assert.Equal(2, nest.Plates.Count);
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
    }

    [Fact]
    public void PlateRemoval_RefreshesTailSubscriptions()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        var plate2 = nest.CreatePlate();
        plate2.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        // plates: [plate1(parts), plate2(parts), sentinel(empty)]

        // Remove plate2 — tail subscriptions should refresh
        nest.Plates.Remove(plate2);
        // plates: [plate1(parts), sentinel(empty)]

        // Verify reactive subscriptions still work on the new tail
        nest.Plates[^1].Parts.Add(MakePart());
        Assert.Equal(0, nest.Plates[^1].Parts.Count);
    }
}
