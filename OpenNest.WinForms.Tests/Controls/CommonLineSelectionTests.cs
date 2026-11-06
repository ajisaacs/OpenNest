using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class CommonLineSelectionTests
{
    private const double Kerf = 0.06;

    [Fact]
    public void SelectingOneMemberSelectsItsWholeGroup() => RunSta(() =>
    {
        var (plate, left, right, loose) = JoinedPairWithLoosePart();
        using var view = new PlateView { Plate = plate };

        view.Selection.Select(Layout(view, left));

        Assert.Equal(new[] { left, right }, view.SelectedParts.Select(p => p.BasePart));
        Assert.All(view.SelectedParts, p => Assert.True(p.IsSelected));
        Assert.False(Layout(view, loose).IsSelected);
    });

    [Fact]
    public void SelectingALoosePartSelectsOnlyThatPart() => RunSta(() =>
    {
        var (plate, _, _, loose) = JoinedPairWithLoosePart();
        using var view = new PlateView { Plate = plate };

        view.Selection.Select(Layout(view, loose));

        Assert.Equal(new[] { loose }, view.SelectedParts.Select(p => p.BasePart));
    });

    [Fact]
    public void NudgingOneMemberMovesTheJoinedPairAndKeepsItsSharedLine() => RunSta(() =>
    {
        var (plate, left, right, _) = JoinedPairWithLoosePart();
        using var view = new PlateView { Plate = plate };
        var leftStart = left.BoundingBox.Location;
        var rightStart = right.BoundingBox.Location;

        view.Selection.Select(Layout(view, right));
        view.Selection.NudgeSelected(1, 2);

        Assert.Equal(leftStart + new Vector(1, 2), left.BoundingBox.Location);
        Assert.Equal(rightStart + new Vector(1, 2), right.BoundingBox.Location);
        var group = Assert.Single(plate.CommonLines);
        Assert.True(CommonLineGeometry.Check(group).IsIntact);
    });

    [Fact]
    public void PartMenuOffersRemoveForAGroupAndJoinWhenALoosePartIsAdded() => RunSta(() =>
    {
        var (plate, left, _, loose) = JoinedPairWithLoosePart();
        using var view = new PlateView { Plate = plate };
        var joins = 0;
        var removes = 0;
        view.JoinCommonLineCommand = () => joins++;
        view.RemoveCommonLineCommand = () => removes++;
        var onLeft = view.PointWorldToControl(left.BoundingBox.Center);

        // Off the selection there is no menu.
        Assert.Null(view.PartMenu.BuildMenu(onLeft));

        view.Selection.Select(Layout(view, left));
        var items = MenuItems(view.PartMenu.BuildMenu(onLeft));
        Assert.Equal(new[] { "Join Common Line", "Remove Common Line" }, items.Select(i => i.Text));
        Assert.False(items[0].Enabled);
        Assert.True(items[1].Enabled);

        view.Selection.Select(Layout(view, loose));
        items = MenuItems(view.PartMenu.BuildMenu(onLeft));
        Assert.True(items[0].Enabled);
        items[0].PerformClick();
        items[1].PerformClick();
        Assert.Equal(1, joins);
        Assert.Equal(1, removes);
    });

    [Fact]
    public void PartMenuOffersJoinButNotRemoveForLooseParts() => RunSta(() =>
    {
        var (plate, _, _, loose) = JoinedPairWithLoosePart();
        var other = new Part(loose.BaseDrawing, new Vector(30, 10));
        plate.Parts.Add(other);
        using var view = new PlateView { Plate = plate };
        view.JoinCommonLineCommand = () => { };
        view.RemoveCommonLineCommand = () => { };

        view.Selection.Select(Layout(view, loose));
        var onLoose = view.PointWorldToControl(loose.BoundingBox.Center);
        var items = MenuItems(view.PartMenu.BuildMenu(onLoose));
        Assert.False(items[0].Enabled);
        Assert.False(items[1].Enabled);

        view.Selection.Select(Layout(view, other));
        items = MenuItems(view.PartMenu.BuildMenu(onLoose));
        Assert.True(items[0].Enabled);
        Assert.False(items[1].Enabled);

        view.InspectionOnly = true;
        Assert.Null(view.PartMenu.BuildMenu(onLoose));
    });

    [Fact]
    public void PartMenuIsAbsentWithoutHostCommands() => RunSta(() =>
    {
        var (plate, left, _, _) = JoinedPairWithLoosePart();
        using var view = new PlateView { Plate = plate };
        view.Selection.Select(Layout(view, left));

        Assert.Null(view.PartMenu.BuildMenu(view.PointWorldToControl(left.BoundingBox.Center)));
    });

    private static List<System.Windows.Forms.ToolStripItem> MenuItems(System.Windows.Forms.ContextMenuStrip? menu)
    {
        Assert.NotNull(menu);
        return menu.Items.Cast<System.Windows.Forms.ToolStripItem>().ToList();
    }

    private static (Plate Plate, Part Left, Part Right, Part Loose) JoinedPairWithLoosePart()
    {
        var plate = new Plate(40, 20);
        var drawing = new Drawing("square", Square(4));
        var left = new Part(drawing, new Vector(2, 2));
        var right = new Part(drawing, new Vector(6 + Kerf, 2));
        var loose = new Part(drawing, new Vector(20, 10));
        plate.Parts.Add(left);
        plate.Parts.Add(right);
        plate.Parts.Add(loose);
        plate.JoinCommonLine(new[] { left, right }, Kerf);
        Assert.True(CommonLineGeometry.Check(plate.CommonLines[0]).IsIntact);
        return (plate, left, right, loose);
    }

    private static CNC.Program Square(double size)
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(size, 0);
        program.LineTo(size, size);
        program.LineTo(0, size);
        program.LineTo(0, 0);
        return program;
    }

    private static LayoutPart Layout(PlateView view, Part part) =>
        view.LayoutParts.Single(p => ReferenceEquals(p.BasePart, part));

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "Common-line selection test timed out.");
}
