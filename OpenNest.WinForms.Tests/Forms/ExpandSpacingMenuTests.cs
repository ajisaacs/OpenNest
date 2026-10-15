using System.Reflection;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Reads Application.OpenForms for the busy guard, so it must not overlap fill-lifetime tests
// that transiently open NestProgressForm.
[Collection("Fill operation lifetime")]
public class ExpandSpacingMenuTests
{
    [Fact]
    public void RunningFillOnTheNestRefusesExpandWithoutMovingParts() => StaTestThread.Run(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var nest = new Nest("expand job");
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        plate.Parts.Add(new Part(new Drawing("left", Square()), new Vector(1, 1)));
        plate.Parts.Add(new Part(new Drawing("right", Square()), new Vector(12, 1)));
        using var editor = new EditNestForm(nest) { MdiParent = host };
        editor.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        editor.Show();
        editor.Activate();
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        var before = plate.Parts.Select(p => p.Location).ToArray();
        var notices = new List<string>();
        host.ExpandSpacingNotice = notices.Add;

        SetBackingField(editor.PlateView, "IsFillInProgress", true);
        try
        {
            Assert.True(host.IsExpandSpacingBusy(editor));
            Menu(host, "mnuPlateExpandSpacing").PerformClick();

            Assert.Contains("Finish or cancel", Assert.Single(notices));
            Assert.Equal(before, plate.Parts.Select(p => p.Location).ToArray());
        }
        finally
        {
            SetBackingField(editor.PlateView, "IsFillInProgress", false);
        }

        // Selected parts keep ActionSelect busy; that alone must not refuse Expand.
        Assert.True(editor.PlateView.Actions.CurrentAction?.IsBusy() == true);
        Assert.False(host.IsExpandSpacingBusy(editor));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static CNC.Program Square()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    // Same reflection precedent PlateViewFillLifetimeTests uses for fill-lifetime state.
    private static void SetBackingField(object target, string property, object value) =>
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private sealed class MenuHost : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}
