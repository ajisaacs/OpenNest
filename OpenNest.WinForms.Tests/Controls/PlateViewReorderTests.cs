using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class PlateViewReorderTests
{
    [Fact]
    public void FollowsAnInPlaceReorderOfItsCurrentPlateOnly() => StaTestThread.Run(() =>
    {
        var plate = new Plate(100, 100);
        foreach (var x in new[] { 1.0, 12.0, 23.0 })
            plate.Parts.Add(new Part(
                new Drawing($"part {x}", TestPrograms.ClockwiseSquare(10)), new Vector(x, 1)));
        using var view = new PlateView { Plate = plate };
        var parts = plate.Parts.ToArray();
        var raised = 0;
        view.PartsReordered += (_, _) => raised++;

        plate.Parts.Reorder(new[] { parts[2], parts[0], parts[1] });

        // Part numbers are drawn from the layout order, so it must follow the plate.
        Assert.Equal(1, raised);
        Assert.Equal(new[] { parts[2], parts[0], parts[1] }, view.LayoutParts.Select(layout => layout.BasePart));
        Assert.All(view.LayoutParts, layout => Assert.True(layout.IsDirty));

        view.Plate = new Plate(60, 120);
        plate.Parts.Reorder(parts);
        Assert.Equal(1, raised);
        Assert.Empty(view.LayoutParts);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void KeepsOneLayoutPerOccurrenceOfARepeatedPart() => StaTestThread.Run(() =>
    {
        var plate = new Plate(100, 100);
        var repeated = new Part(
            new Drawing("repeated", TestPrograms.ClockwiseSquare(10)), new Vector(1, 1));
        var other = new Part(
            new Drawing("other", TestPrograms.ClockwiseSquare(10)), new Vector(12, 1));
        plate.Parts.Add(repeated);
        plate.Parts.Add(repeated);
        plate.Parts.Add(other);
        using var view = new PlateView { Plate = plate };
        var before = view.LayoutParts.ToArray();

        plate.Parts.Reorder(new[] { other, repeated, repeated });

        var after = view.LayoutParts;
        Assert.Equal(new[] { other, repeated, repeated }, after.Select(layout => layout.BasePart));
        Assert.Same(before[2], after[0]);
        Assert.Equal(3, after.Distinct().Count());
        Assert.Equal(before.ToHashSet(), after.ToHashSet());
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void DisposedViewIgnoresItsRetainedPlate() => StaTestThread.Run(() =>
    {
        var plate = new Plate(100, 100);
        foreach (var x in new[] { 1.0, 12.0 })
            plate.Parts.Add(new Part(
                new Drawing($"part {x}", TestPrograms.ClockwiseSquare(10)), new Vector(x, 1)));
        var parts = plate.Parts.ToArray();
        var view = new PlateView { Plate = plate };
        var raised = 0;
        view.PartsReordered += (_, _) => raised++;
        var layouts = view.LayoutParts.ToArray();

        view.Dispose();
        plate.Parts.Reorder(new[] { parts[1], parts[0] });
        plate.Parts.Add(new Part(
            new Drawing("late", TestPrograms.ClockwiseSquare(10)), new Vector(23, 1)));

        Assert.Equal(0, raised);
        Assert.Equal(layouts, view.LayoutParts);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
