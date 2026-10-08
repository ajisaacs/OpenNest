using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class PlateHeaderNumberingTests
{
    [Fact]
    public void OnePopulatedPlateWithSentinel_HeaderSaysOneOfOne() => RunSta(() =>
    {
        var nest = new Nest("one plate");
        var plate = nest.CreatePlate();
        plate.Parts.Add(Square("a"));
        using var form = new EditNestForm(nest);
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();

        // The constructor's EnsureSentinel keeps the trailing empty new-plate workspace.
        Assert.Equal(2, nest.Plates.Count);
        Assert.Equal("Plate 1 of 1", Header(form).Split("  |  ")[0]);
    });

    [Fact]
    public void NavigatingOntoTheSentinelAndBack_RelabelsWithoutTouchingNavigation() => RunSta(() =>
    {
        var nest = new Nest("sentinel navigation");
        var plate = nest.CreatePlate();
        plate.Parts.Add(Square("a"));
        using var form = new EditNestForm(nest);
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();

        Assert.True(form.PlateManager.LoadNext()); // onto the sentinel
        Assert.Equal("New plate (empty)", Header(form));
        Assert.Equal(1, form.PlateManager.CurrentIndex); // navigation itself is unchanged

        Assert.True(form.PlateManager.LoadPrevious());
        Assert.Equal("Plate 1 of 1", Header(form).Split("  |  ")[0]);
        Assert.Equal(2, nest.Plates.Count); // the collection was never mutated for display
    });

    [Fact]
    public void SingleEmptyPlate_EditorShowsTheNewPlateView() => RunSta(() =>
    {
        var nest = new Nest("empty");
        using var form = new EditNestForm(nest); // EnsureSentinel creates the single empty plate.
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();

        Assert.Single(nest.Plates);
        Assert.Equal("New plate (empty)", Header(form));
    });

    [Fact]
    public void InteriorEmptyPlate_KeepsItsNumberAndTheTotalCountsIt() => RunSta(() =>
    {
        var nest = new Nest("interior empty");
        nest.CreatePlate().Parts.Add(Square("a"));
        nest.CreatePlate(); // An empty interior plate survives EnsureSentinel (only the tail trims).
        nest.CreatePlate().Parts.Add(Square("b"));
        using var form = new EditNestForm(nest);
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();

        Assert.Equal(4, nest.Plates.Count); // three real + sentinel
        form.PlateManager.LoadAt(1);
        Assert.Equal("Plate 2 of 3", Header(form).Split("  |  ")[0]);
        form.PlateManager.LoadAt(2);
        Assert.Equal("Plate 3 of 3", Header(form).Split("  |  ")[0]);
        form.PlateManager.LoadAt(3);
        Assert.Equal("New plate (empty)", Header(form));
        Assert.Equal(4, nest.Plates.Count);
    });

    private static string Header(EditNestForm form) =>
        ((Label)typeof(EditNestForm)
            .GetField("plateInfoLabel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!).Text;

    private static Part Square(string name)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return new Part(new Drawing(name, program), new Vector(1, 1));
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");
}
