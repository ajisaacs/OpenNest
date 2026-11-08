using System.Drawing;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class PlateReorderTests
{
    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(0, 3, 2)]
    [InlineData(3, 0, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(2, 3, 2)]
    public void DropSlotMapsToTheLandingIndex(int from, int slot, int expected) =>
        Assert.Equal(expected, EditNestForm.PlateDropTarget(from, slot));

    [Fact]
    public void DropSlotFollowsTheRowHalvesAndStopsBeforeTheSentinel() => UndoTestEnv.Run(env =>
    {
        var list = (ListView)Assert.Single(env.Editor.Controls.Find("platesListView", true));
        var movable = env.Editor.PlateManager.MovableCount;
        Assert.Equal(env.Nest.Plates.Count - 1, movable);

        var first = list.Items[0].Bounds;
        Assert.Equal(0, env.Editor.GetPlateDropSlot(new Point(5, first.Top + 1)));
        Assert.Equal(1, env.Editor.GetPlateDropSlot(new Point(5, first.Bottom - 1)));

        var sentinel = list.Items[movable].Bounds;
        Assert.Equal(movable, env.Editor.GetPlateDropSlot(new Point(5, sentinel.Top + 1)));
        Assert.Equal(movable, env.Editor.GetPlateDropSlot(new Point(5, list.ClientSize.Height - 1)));
    });

    [Fact]
    public void MovingAPlateKeepsItCurrentAndSelected() => UndoTestEnv.Run(env =>
    {
        var plate = env.Nest.Plates[0];
        env.Editor.PlateManager.LoadAt(0);

        Assert.True(env.Editor.MovePlate(0, 1));

        Assert.Same(plate, env.Nest.Plates[1]);
        Assert.Same(plate, env.Editor.PlateManager.CurrentPlate);
        var list = (ListView)Assert.Single(env.Editor.Controls.Find("platesListView", true));
        Assert.True(list.Items[1].Selected);
        Assert.Equal("2", list.Items[1].Text);
    });

    [Fact]
    public void DuplicatingTheCurrentPlateInsertsItsCopyAfterItAndSelectsIt() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var source = env.Nest.Plates[0];
        var count = env.Nest.Plates.Count;

        Assert.True(env.Editor.DuplicateCurrentPlate());

        Assert.Equal(count + 1, env.Nest.Plates.Count);
        Assert.Same(source, env.Nest.Plates[0]);
        var copy = env.Nest.Plates[1];
        Assert.NotSame(source, copy);
        Assert.Equal(source.Parts.Count, copy.Parts.Count);
        Assert.Same(copy, env.Editor.PlateManager.CurrentPlate);
        if (source.CuttingParameters != null)
            Assert.NotSame(source.CuttingParameters, copy.CuttingParameters);
        var list = (ListView)Assert.Single(env.Editor.Controls.Find("platesListView", true));
        Assert.True(list.Items[1].Selected);
        Assert.NotNull(list.ContextMenuStrip);
    });

    [Fact]
    public void DeletePlateOnTheRowMenuRemovesTheCurrentPlate() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var plate = env.Nest.Plates[0];
        var count = env.Nest.Plates.Count;
        var list = (ListView)Assert.Single(env.Editor.Controls.Find("platesListView", true));
        var item = Assert.IsType<ToolStripMenuItem>(list.ContextMenuStrip!.Items["deletePlateMenuItem"]);
        Assert.Equal("Delete Plate", item.Text);

        item.PerformClick();

        Assert.Equal(count - 1, env.Nest.Plates.Count);
        Assert.DoesNotContain(plate, env.Nest.Plates);
    });

    [Fact]
    public void TheEmptySentinelCannotBeDuplicated() => UndoTestEnv.Run(env =>
    {
        var count = env.Nest.Plates.Count;
        Assert.False(env.Editor.DuplicatePlate(count - 1, 0));
        Assert.Equal(count, env.Nest.Plates.Count);
    });
}
