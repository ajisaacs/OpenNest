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
}
