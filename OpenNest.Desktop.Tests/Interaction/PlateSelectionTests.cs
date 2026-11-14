using OpenNest.Desktop.Interaction;

namespace OpenNest.Desktop.Tests.Interaction;

public class PlateSelectionTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly PlateSelection selection = new();
    private int changes;

    public PlateSelectionTests()
    {
        selection.Plate = plate;
        selection.Changed += (_, _) => changes++;
    }

    [Fact]
    public void Selecting_a_common_line_member_brings_its_group()
    {
        selection.Select(plate.Parts[3]);

        Assert.Equal(new[] { plate.Parts[3], plate.Parts[2] }, selection.Parts);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Selecting_what_is_already_selected_changes_nothing()
    {
        selection.Select(plate.Parts[1]);
        selection.Select(plate.Parts[1]);
        selection.ClearCutOffs();

        Assert.Single(selection.Parts);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Select_all_takes_every_part_but_cut_off_segments_once()
    {
        selection.Select(plate.Parts[1]);

        selection.SelectAll();

        var expected = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToList();
        Assert.Equal(expected.Count, selection.Parts.Count);
        Assert.All(expected, part => Assert.True(selection.Contains(part)));
        Assert.Empty(selection.CutOffs);
    }

    [Fact]
    public void Cut_off_segments_are_not_selected_as_parts()
    {
        selection.Select(plate.Parts.Single(p => p.BaseDrawing.IsCutOff));

        Assert.True(selection.IsEmpty);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Select_only_replaces_parts_and_cut_offs()
    {
        selection.Select(plate.Parts[1]);
        selection.SelectOnly(plate.CutOffs[0]);

        Assert.Empty(selection.Parts);
        Assert.Equal(new[] { plate.CutOffs[0] }, selection.CutOffs);

        selection.SelectOnly(plate.Parts[0]);

        Assert.Equal(new[] { plate.Parts[0] }, selection.Parts);
        Assert.Empty(selection.CutOffs);
    }

    [Fact]
    public void Showing_another_plate_clears_the_selection()
    {
        selection.SelectAll();
        selection.Plate = InteractionFixtures.Plate();

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void The_selection_reads_and_sets_by_part_index_in_plate_order()
    {
        selection.Select(plate.Parts[3]);
        selection.Select(plate.Parts[0]);

        Assert.Equal(new[] { 0, 2, 3 }, selection.PartIndices);

        var copy = new PlateSelection { Plate = InteractionFixtures.Plate() };
        copy.SelectIndices(new[] { 1, 3, 99, -1 });

        Assert.Equal(new[] { 1, 2, 3 }, copy.PartIndices);
    }

    [Fact]
    public void Prune_drops_parts_and_cut_offs_no_longer_on_the_plate()
    {
        selection.Select(plate.Parts[1]);
        selection.Select(plate.Parts[0]);
        selection.Select(plate.CutOffs[0]);
        var removed = plate.Parts[1];
        plate.Parts.Remove(removed);
        plate.CutOffs.Clear();

        Assert.True(selection.Prune());

        Assert.False(selection.Contains(removed));
        Assert.Equal(new[] { plate.Parts[0] }, selection.Parts);
        Assert.Empty(selection.CutOffs);
        Assert.False(selection.Prune());
    }
}
