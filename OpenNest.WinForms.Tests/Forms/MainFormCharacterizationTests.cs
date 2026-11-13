using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.CutOffs;
using OpenNest.Forms;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.TestReflection;

namespace OpenNest.WinForms.Tests.Forms;

// Pins MainForm behavior that its refactor must preserve: the deliberately different
// busy guards. Reads Application.OpenForms, so it must not overlap fill-lifetime tests.
[Collection("Fill operation lifetime")]
public class MainFormCharacterizationTests
{
    [Fact]
    public void BusyGuardsDifferInWhichOperationsTheyCount() => StaTestThread.Run(() =>
    {
        using var host = new Host();
        host.Show();
        var nest = new Nest("guards");
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        plate.Parts.Add(new Part(
            new Drawing("left", TestPrograms.ClockwiseSquare(10)), new Vector(1, 1)));
        plate.Parts.Add(new Part(
            new Drawing("right", TestPrograms.ClockwiseSquare(10)), new Vector(12, 1)));
        using var editor = new EditNestForm(nest) { MdiParent = host };
        editor.PlateView.SetOverlapAutoCheck(null);
        editor.Show();
        editor.Activate();
        editor.PlateManager.LoadAt(0);
        var other = new Nest("other");
        other.CreatePlate().Size = new Size(100, 100);
        using var otherEditor = new EditNestForm(other) { MdiParent = host };
        otherEditor.PlateView.SetOverlapAutoCheck(null);
        otherEditor.Show();
        otherEditor.PlateManager.LoadAt(0);
        var views = new[] { editor.PlateView };

        Assert.Equal(new Guards(false, false, false, false), Read(host, editor, views));

        // Whole-job nesting blocks every guard.
        SetField(host, "nestingInProgress", true);
        Assert.Equal(new Guards(true, true, true, true), Read(host, editor, views));
        SetField(host, "nestingInProgress", false);

        // A database save only blocks the report snapshot.
        var storage = GetField(host, "storage");
        SetBackingField(storage, "IsSaving", true);
        Assert.Equal(new Guards(false, false, false, true), Read(host, editor, views));
        SetBackingField(storage, "IsSaving", false);

        // A fill on a view of the nest blocks the plate guards but not the report.
        SetBackingField(editor.PlateView, "IsFillInProgress", true);
        try
        {
            Assert.Equal(new Guards(true, true, true, false), Read(host, editor, views));
        }
        finally
        {
            SetBackingField(editor.PlateView, "IsFillInProgress", false);
        }

        // A fill on another nest's view blocks nothing for this nest.
        SetBackingField(otherEditor.PlateView, "IsFillInProgress", true);
        try
        {
            Assert.Equal(new Guards(false, false, false, false), Read(host, editor, views));
        }
        finally
        {
            SetBackingField(otherEditor.PlateView, "IsFillInProgress", false);
        }

        // A busy plate action (selection) blocks only the cutting-plan guard.
        editor.PlateView.SelectAll();
        Assert.True(editor.PlateView.Actions.CurrentAction?.IsBusy() == true);
        Assert.Equal(new Guards(false, true, false, false), Read(host, editor, views));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ToolbarFollowsTheActiveDocumentAndItsPlate() => UndoTestEnv.Run(env =>
    {
        var host = env.Host;
        bool Enabled(string button) => Field<ToolStripItem>(typeof(MainForm), host, button).Enabled;
        void AddCutOff()
        {
            using var change = env.View.BeginChange("Add Cut-Off");
            change.MarkChanged();
            env.View.Plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
            env.View.Plate.RegenerateCutOffs(env.View.CutOffSettings);
            change.Complete();
        }

        // The first plate holds two parts and no cut-offs.
        env.Editor.PlateManager.LoadAt(0);
        Assert.True(Enabled("btnCutOff"));
        Assert.True(Enabled("btnSequenceByPath"));
        Assert.False(Enabled("btnTrimExtendCutOff"));
        Assert.False(UndoTestEnv.MenuItem(host, "mnuPlateTrimExtendCutOff").Enabled);

        AddCutOff();
        Assert.True(Enabled("btnTrimExtendCutOff"));
        Assert.True(UndoTestEnv.MenuItem(host, "mnuPlateTrimExtendCutOff").Enabled);

        Assert.True(host.TryUndo(env.Editor));
        Assert.False(Enabled("btnTrimExtendCutOff"));
        Assert.True(host.TryRedo(env.Editor));
        Assert.True(Enabled("btnTrimExtendCutOff"));

        // Another plate has its own cut-offs; the third one is empty.
        env.Editor.PlateManager.LoadAt(2);
        Assert.False(Enabled("btnTrimExtendCutOff"));
        Assert.False(Enabled("btnSequenceByPath"));
        env.View.Plate.Parts.Add(new Part(env.Nest.Drawings.First(), new Vector(5, 5)));
        env.View.Plate.Parts.Add(new Part(env.Nest.Drawings.First(), new Vector(25, 5)));
        Assert.True(Enabled("btnSequenceByPath"));

        // A running job locks the plate commands, as it does the Nest and Plate menus.
        Invoke(host, "SetNestingLockout", true);
        Assert.False(Enabled("btnAutoNest"));
        Assert.False(Enabled("btnCutOff"));
        Assert.False(Enabled("postButton"));
        Invoke(host, "SetNestingLockout", false);
        Assert.True(Enabled("btnAutoNest"));

        env.Editor.Close();
        foreach (var button in ToolbarPlateCommands)
            Assert.False(Enabled(button), button);
        Assert.True(Enabled("btnNew"));
        Assert.True(Enabled("btnOpen"));
    });

    private static readonly string[] ToolbarPlateCommands =
    {
        "btnSave", "btnSaveAs", "btnAutoNest", "btnShowRemnants", "btnCutOff", "btnTrimExtendCutOff",
        "btnCutPlanner", "btnSimulateCutting", "btnSequenceByPath", "postButton",
    };

    private sealed record Guards(bool ExpandSpacing, bool CuttingPlan, bool Clamps, bool NestJob);

    private static Guards Read(MainForm host, EditNestForm editor, PlateView[] views) => new(
        host.IsExpandSpacingBusy(editor),
        host.IsCuttingPlanBusy(views),
        (bool)Invoke(host, "ClampsInUse", editor.Nest),
        (bool)Invoke(host, "IsNestJobBusy"));

    private static object Invoke(MainForm host, string name, params object[] args) =>
        typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, args)!;

    private static object GetField(MainForm host, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static void SetField(MainForm host, string name, object value) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);

    private sealed class Host : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}
