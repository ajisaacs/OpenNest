using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PlateDisplayNumberingTests
{
    private static Nest CreateNest() => new("test");

    private static Part MakePart()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Part(new Drawing("test", pgm));
    }

    [Fact]
    public void PopulatedThenSentinel_TotalIsOneAndRealPlateDisplaysAsOne()
    {
        var nest = CreateNest();
        var real = nest.CreatePlate();
        real.Parts.Add(MakePart());
        nest.CreatePlate(); // The trailing empty sentinel.

        Assert.Equal(1, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.Equal(1, PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 0));
        Assert.Equal("Plate 1 of 1", PlateDisplayNumbering.FormatHeader(nest.Plates, 0, null));
    }

    [Fact]
    public void Sentinel_IsLabeledAsNewPlateNotNumbered()
    {
        var nest = CreateNest();
        var real = nest.CreatePlate();
        real.Parts.Add(MakePart());
        nest.CreatePlate();

        var sentinel = nest.Plates.Count - 1;
        Assert.True(PlateDisplayNumbering.IsTrailingSentinel(nest.Plates, sentinel));
        Assert.Null(PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, sentinel));
        Assert.Equal("New plate (empty)", PlateDisplayNumbering.FormatHeader(nest.Plates, sentinel, null));
    }

    [Fact]
    public void TwoRealPlatesThenSentinel_TotalIsTwo()
    {
        var nest = CreateNest();
        nest.Plates.Add(WithPart());
        nest.Plates.Add(WithPart());
        nest.CreatePlate(); // sentinel

        Assert.Equal(2, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.Equal(1, PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 0));
        Assert.Equal(2, PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 1));
        Assert.Null(PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 2));
    }

    [Fact]
    public void NoPlates_TotalIsZeroAndHeaderSaysNoPlates()
    {
        var nest = CreateNest();

        Assert.Equal(0, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.Null(PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 0));
        Assert.Equal("No plates", PlateDisplayNumbering.FormatHeader(nest.Plates, 0, null));
    }

    [Fact]
    public void SingleEmptyPlate_IsTheNewPlateView_NotNumbered()
    {
        var nest = CreateNest();
        nest.CreatePlate(); // only the sentinel

        Assert.Equal(0, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.True(PlateDisplayNumbering.IsTrailingSentinel(nest.Plates, 0));
        Assert.Equal("New plate (empty)", PlateDisplayNumbering.FormatHeader(nest.Plates, 0, null));
    }

    [Fact]
    public void InteriorEmptyPlate_KeepsItsSlotAndNumber()
    {
        var nest = CreateNest();
        nest.Plates.Add(WithPart());
        nest.CreatePlate(); // An empty interior plate: EnsureSentinel only trims at the tail.
        nest.Plates.Add(WithPart());
        nest.CreatePlate(); // sentinel

        Assert.Equal(3, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.Equal(2, PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 1));
        Assert.Equal(3, PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 2));
        Assert.False(PlateDisplayNumbering.IsTrailingSentinel(nest.Plates, 1));
    }

    [Fact]
    public void LastPlateWithParts_IsNotASentinel()
    {
        var nest = CreateNest();
        nest.Plates.Add(WithPart());

        Assert.Equal(1, PlateDisplayNumbering.DisplayedPlateCount(nest.Plates));
        Assert.False(PlateDisplayNumbering.IsTrailingSentinel(nest.Plates, 0));
    }

    [Fact]
    public void IndexOutsideCollection_HasNoNumberAndIsNotASentinel()
    {
        var nest = CreateNest();
        nest.Plates.Add(WithPart());

        Assert.Null(PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, 5));
        Assert.Null(PlateDisplayNumbering.DisplayedPlateNumber(nest.Plates, -1));
        Assert.False(PlateDisplayNumbering.IsTrailingSentinel(nest.Plates, 5));
    }

    [Fact]
    public void Helper_DoesNotMutateTheNest()
    {
        var nest = CreateNest();
        nest.Plates.Add(WithPart());
        nest.CreatePlate();
        var before = nest.Plates.Count;

        PlateDisplayNumbering.DisplayedPlateCount(nest.Plates);
        PlateDisplayNumbering.FormatHeader(nest.Plates, 1, "100 x 100");

        Assert.Equal(before, nest.Plates.Count);
    }

    private static Plate WithPart()
    {
        var plate = new Plate();
        plate.Parts.Add(MakePart());
        return plate;
    }
}
