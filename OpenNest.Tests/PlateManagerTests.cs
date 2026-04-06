using OpenNest.CNC;
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
}
