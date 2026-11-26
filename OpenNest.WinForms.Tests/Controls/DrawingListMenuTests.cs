using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class DrawingListMenuTests
{
    [Fact]
    public void RemoveUnusedOpensWithoutASelectedDrawing() => RunSta(() =>
    {
        using var list = new DrawingListBox();
        list.DeleteRequested += (_, _) => { };
        list.RemoveUnusedRequested += (_, _) => { };

        Assert.False(Open(list));

        var shown = Shown(list);
        Assert.Equal(new[] { "mnuRemoveUnusedDrawings" }, shown.Select(item => item.Name));
    });

    [Fact]
    public void RemoveUnusedFollowsTheSelectedDrawingsCommandsBehindASeparator() => RunSta(() =>
    {
        using var list = new DrawingListBox();
        var drawing = new Drawing("a", new CNC.Program());
        list.Items.Add(drawing);
        list.SelectedItem = drawing;
        list.DeleteRequested += (_, _) => { };
        list.RemoveUnusedRequested += (_, _) => { };

        Assert.False(Open(list));

        var shown = Shown(list);
        Assert.Equal(3, shown.Length);
        Assert.Equal("Delete", shown[0].Text);
        Assert.IsType<ToolStripSeparator>(shown[1]);
        Assert.Equal("mnuRemoveUnusedDrawings", shown[2].Name);
    });

    [Fact]
    public void MenuStaysClosedWithNothingToOffer() => RunSta(() =>
    {
        using var list = new DrawingListBox();

        Assert.True(Open(list));
    });

    [Fact]
    public void ChoosingRemoveUnusedRaisesItsRequest() => RunSta(() =>
    {
        using var list = new DrawingListBox();
        var raised = 0;
        list.RemoveUnusedRequested += (_, _) => raised++;

        list.ContextMenuStrip!.Items["mnuRemoveUnusedDrawings"]!.PerformClick();

        Assert.Equal(1, raised);
    });

    /// <summary>Runs the menu's Opening handlers and returns whether they cancelled it.</summary>
    private static bool Open(DrawingListBox list)
    {
        var args = new CancelEventArgs();
        typeof(ToolStripDropDown)
            .GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list.ContextMenuStrip, new object[] { args });
        return args.Cancel;
    }

    private static ToolStripItem[] Shown(DrawingListBox list) =>
        list.ContextMenuStrip!.Items.Cast<ToolStripItem>().Where(item => item.Available).ToArray();

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");
}
