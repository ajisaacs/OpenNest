using OpenNest.Desktop.Interaction;

namespace OpenNest.Desktop.Tests.Interaction;

public class SelectToolTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly TestToolHost host;

    public SelectToolTests()
    {
        host = new TestToolHost(plate);
    }

    private PlateSelection Selection => host.Selection;

    private SelectTool Tool => Assert.IsType<SelectTool>(host.Tools.Current);

    [Fact]
    public void A_click_on_a_part_selects_it_alone()
    {
        host.Click(25, 5);
        host.Click(1, 1);

        Assert.Equal(new[] { plate.Parts[0] }, Selection.Parts);
    }

    [Fact]
    public void Ctrl_click_adds_to_the_selection()
    {
        host.Click(25, 5);
        host.Click(1, 1, ToolModifiers.Control);

        Assert.Equal(new[] { plate.Parts[1], plate.Parts[0] }, Selection.Parts);
    }

    [Fact]
    public void Ctrl_shift_click_replaces_the_selection_as_in_WinForms()
    {
        host.Click(25, 5);
        host.Click(1, 1, ToolModifiers.Control | ToolModifiers.Shift);

        Assert.Equal(new[] { plate.Parts[0] }, Selection.Parts);
    }

    [Fact]
    public void A_click_on_a_selected_part_keeps_the_selection()
    {
        host.Click(25, 5);
        host.Click(1, 1, ToolModifiers.Control);
        host.Click(25, 5);

        Assert.Equal(2, Selection.Parts.Count);
    }

    [Fact]
    public void A_click_on_a_common_line_member_selects_its_group()
    {
        host.Click(45, 5);

        Assert.Equal(new[] { plate.Parts[2], plate.Parts[3] }, Selection.Parts);
    }

    [Fact]
    public void A_click_in_a_hole_or_on_empty_sheet_clears_the_selection()
    {
        host.Click(25, 5);
        host.Click(5, 5);

        Assert.True(Selection.IsEmpty);
        Assert.Null(Tool.Window);
    }

    [Fact]
    public void A_click_near_a_cut_off_selects_it_alone()
    {
        host.Click(25, 5);
        host.Click(80.3, 50);

        Assert.Empty(Selection.Parts);
        Assert.Equal(new[] { plate.CutOffs[0] }, Selection.CutOffs);

        host.Click(25, 5);
        Assert.Empty(Selection.CutOffs);
    }

    [Fact]
    public void Dragging_right_selects_what_the_window_contains()
    {
        host.Drag(-1, -1, 31, 11);

        Assert.Equal(new[] { plate.Parts[0], plate.Parts[1] }, Selection.Parts);
        Assert.Null(Tool.Window);
    }

    [Fact]
    public void Dragging_left_selects_what_the_window_touches_with_common_line_groups_and_cut_offs()
    {
        host.Drag(85, 5, 45, 8);

        Assert.Equal(new[] { plate.Parts[2], plate.Parts[3] }, Selection.Parts);
        Assert.Equal(new[] { plate.CutOffs[0] }, Selection.CutOffs);
    }

    [Fact]
    public void The_window_follows_the_pointer_while_dragging()
    {
        host.Tools.Current.PointerPressed(host.At(60, 60));
        host.Tools.Current.PointerMoved(host.At(70, 65));

        var window = Assert.IsType<SelectionWindow>(Tool.Window);
        Assert.Equal(WindowSelectionMode.Contains, window.Mode);
        Assert.Equal(70, window.Right);
        Assert.True(Tool.IsBusy);
    }

    [Fact]
    public void A_right_click_on_an_unselected_part_selects_it_and_elsewhere_clears()
    {
        host.Click(25, 5);
        host.Click(1, 1, ToolModifiers.Control);

        host.Tools.Current.PointerPressed(host.At(25, 5, ToolButton.Right));
        Assert.Equal(2, Selection.Parts.Count);

        host.Tools.Current.PointerPressed(host.At(25, 25, ToolButton.Right));
        Assert.Equal(new[] { plate.Parts[4] }, Selection.Parts);

        host.Tools.Current.PointerPressed(host.At(70, 70, ToolButton.Right));
        Assert.True(Selection.IsEmpty);
    }

    [Fact]
    public void A_click_skips_parts_already_removed_from_the_selection_plate()
    {
        host.Click(25, 5);
        plate.Parts.RemoveAt(1);

        host.Click(1, 1, ToolModifiers.Control);

        Assert.Equal(new[] { plate.Parts[0] }, Selection.Parts);
    }
}
