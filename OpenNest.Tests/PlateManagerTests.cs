using OpenNest.CNC;
using OpenNest.Collections;
using OpenNest.CommonLines;
using OpenNest.CutOffs;
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
        Assert.Single(nest.Plates);
        Assert.Empty(nest.Plates[0].Parts);
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
        Assert.Empty(nest.Plates[^1].Parts);
    }

    [Fact]
    public void EnsureSentinel_TwoTrailingEmpties_TrimsToOne()
    {
        var nest = CreateNest();
        nest.CreatePlate();
        nest.CreatePlate();
        using var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        Assert.Single(nest.Plates);
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
        Assert.Empty(nest.Plates[^1].Parts);
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
        Assert.Single(nest.Plates);
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
        Assert.Empty(nest.Plates[^1].Parts);
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
        Assert.Empty(nest.Plates[^1].Parts);
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
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void GetOrCreateEmpty_NoEmptyPlate_CreatesNew()
    {
        var nest = CreateNest();
        var plate1 = nest.CreatePlate();
        plate1.Parts.Add(MakePart());
        using var mgr = new PlateManager(nest);

        var result = mgr.GetOrCreateEmpty();
        Assert.Empty(result.Parts);
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
        Assert.Empty(nest.Plates[^1].Parts);
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
        Assert.Empty(nest.Plates[^1].Parts);
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
        Assert.Empty(nest.Plates[^1].Parts);
    }

    private static (Nest Nest, PlateManager Manager, Plate[] Filled) NestWithFilledPlates(int count)
    {
        var nest = CreateNest();
        var filled = new Plate[count];
        for (var i = 0; i < count; i++)
        {
            filled[i] = nest.CreatePlate();
            filled[i].Parts.Add(MakePart());
        }
        var mgr = new PlateManager(nest);
        mgr.EnsureSentinel();
        return (nest, mgr, filled);
    }

    [Fact]
    public void MovePlate_MovesPlateAndShiftsTheOthers()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(3);
        using var _ = mgr;

        Assert.True(mgr.MovePlate(2, 0));

        Assert.Equal(new[] { filled[2], filled[0], filled[1] }, nest.Plates.Take(3));
        Assert.Equal(4, nest.Plates.Count);
        Assert.Empty(nest.Plates[^1].Parts);
    }

    [Fact]
    public void MovePlate_CurrentPlateKeepsItsIdentity()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(3);
        using var _ = mgr;
        mgr.LoadAt(1);
        var listChanged = 0;
        var currentChanged = 0;
        mgr.PlateListChanged += (s, e) => listChanged++;
        mgr.CurrentPlateChanged += (s, e) => currentChanged++;

        Assert.True(mgr.MovePlate(0, 2));

        Assert.Same(filled[1], mgr.CurrentPlate);
        Assert.Equal(0, mgr.CurrentIndex);
        Assert.Equal(1, listChanged);
        Assert.Equal(0, currentChanged);
    }

    [Fact]
    public void MovePlate_DoesNotChangeQuantities()
    {
        var (nest, mgr, _) = NestWithFilledPlates(3);
        using var __ = mgr;
        var drawing = nest.Plates[0].Parts[0].BaseDrawing;
        var nested = drawing.Quantity.Nested;

        mgr.MovePlate(0, 2);

        Assert.Equal(nested, drawing.Quantity.Nested);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void MovePlate_RefusesNoOpsAndTheSentinel(int from, int to)
    {
        var (nest, mgr, filled) = NestWithFilledPlates(3);
        using var _ = mgr;
        var sentinel = nest.Plates[^1];

        Assert.Equal(3, mgr.MovableCount);
        Assert.False(mgr.MovePlate(from, to));
        Assert.Equal(filled.Append(sentinel), nest.Plates);
    }

    [Fact]
    public void MovePlate_TailSubscriptionsFollowTheNewOrder()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(2);
        using var _ = mgr;

        mgr.MovePlate(1, 0);
        // filled[0] is now second to last; emptying it must collapse the doubled empty tail.
        filled[0].Parts.Clear();

        Assert.Equal(new[] { filled[1], filled[0] }, nest.Plates);
    }

    [Fact]
    public void MovePlate_EmptyPlateMovedToTheEndBecomesTheSentinel()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(2);
        using var _ = mgr;
        var empty = nest.Plates[^1];
        nest.Plates.Insert(0, new Plate(10, 10));
        var configured = nest.Plates[0];
        mgr.EnsureSentinel();
        mgr.LoadAt(0);

        Assert.True(mgr.MovePlate(0, 2));

        Assert.Equal(new[] { filled[0], filled[1], configured }, nest.Plates);
        Assert.Same(configured, mgr.CurrentPlate);
        Assert.DoesNotContain(empty, nest.Plates);
    }

    [Fact]
    public void DuplicatePlate_InsertsACopyAtTheSlotAndMakesItCurrent()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(2);
        using var _ = mgr;
        var source = filled[1];
        source.Parts[0].Offset(new Vector(5, 7));
        source.Quantity = 3;
        var drawing = source.Parts[0].BaseDrawing;
        var nested = drawing.Quantity.Nested;

        var copy = mgr.DuplicatePlate(1, 0);

        Assert.NotNull(copy);
        Assert.Equal(new[] { copy, filled[0], filled[1] }, nest.Plates.Take(3));
        Assert.Equal(4, nest.Plates.Count);
        Assert.Empty(nest.Plates[^1].Parts);
        Assert.Same(copy, mgr.CurrentPlate);
        Assert.Equal(0, mgr.CurrentIndex);
        Assert.Equal(1, copy.Quantity);
        var part = Assert.Single(copy.Parts);
        Assert.NotSame(source.Parts[0], part);
        Assert.Same(drawing, part.BaseDrawing);
        Assert.Equal(source.Parts[0].Location, part.Location);
        Assert.Equal(nested + 1, drawing.Quantity.Nested);
    }

    [Fact]
    public void DuplicatePlate_AtTheEndLandsBeforeTheSentinel()
    {
        var (nest, mgr, filled) = NestWithFilledPlates(2);
        using var _ = mgr;
        var sentinel = nest.Plates[^1];

        var copy = mgr.DuplicatePlate(0, 2);

        Assert.Equal(new[] { filled[0], filled[1], copy, sentinel }, nest.Plates);
        Assert.Same(copy, mgr.CurrentPlate);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    [InlineData(0, -1)]
    public void DuplicatePlate_RefusesTheSentinelAndSlotsPastIt(int from, int to)
    {
        var (nest, mgr, _) = NestWithFilledPlates(2);
        using var __ = mgr;
        var before = nest.Plates.ToArray();

        Assert.Null(mgr.DuplicatePlate(from, to));
        Assert.Equal(before, nest.Plates);
    }

    [Fact]
    public void CopyLayout_CopiesTheSheetAndRemapsCommonLines()
    {
        var plate = new Plate(48, 96)
        {
            Quantity = 2,
            Quadrant = 3,
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.5, 0.5, 0.5, 0.5),
            GrainAngle = 1.5,
            CutCommonLinesTwice = true,
        };
        plate.StockItemCodes = new[] { "A", "B" };
        var a = MakePart();
        var b = MakePart();
        b.Offset(new Vector(10.1, 0));
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        plate.CommonLines.Add(new CommonLineGroup(new[] { a, b }, 0.1));

        var copy = plate.CopyLayout();

        Assert.Equal(plate.Size, copy.Size);
        Assert.Equal(3, copy.Quadrant);
        Assert.Equal(0.25, copy.PartSpacing);
        Assert.Equal(plate.EdgeSpacing, copy.EdgeSpacing);
        Assert.Equal(1.5, copy.GrainAngle);
        Assert.True(copy.CutCommonLinesTwice);
        Assert.Equal(1, copy.Quantity);
        Assert.Empty(copy.StockItemCodes);
        Assert.Equal(2, copy.Parts.Count);
        var group = Assert.Single(copy.CommonLines);
        Assert.Equal(new[] { copy.Parts[0], copy.Parts[1] }, group.Members);
        Assert.Equal(0.1, group.Kerf);
    }

    [Fact]
    public void CopyLayout_RegeneratesCutOffsInTheirPlaces()
    {
        var plate = new Plate(48, 96) { Quantity = 1 };
        plate.Parts.Add(MakePart());
        var second = MakePart();
        second.Offset(new Vector(30, 0));
        plate.Parts.Add(second);
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        var order = plate.Parts.Select(p => p.BaseDrawing.IsCutOff).ToArray();
        Assert.Contains(true, order);

        var copy = plate.CopyLayout();

        var cutoff = Assert.Single(copy.CutOffs);
        Assert.NotSame(plate.CutOffs[0], cutoff);
        Assert.Equal(plate.CutOffs[0].Position, cutoff.Position);
        Assert.Equal(order, copy.Parts.Select(p => p.BaseDrawing.IsCutOff));
        Assert.All(copy.Parts.Where(p => p.BaseDrawing.IsCutOff), p => Assert.Same(cutoff.Drawing, p.BaseDrawing));
    }
}
