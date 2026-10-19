using System.Reflection;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Pins MainForm behaviour that its refactor must preserve: the deliberately different
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
        plate.Parts.Add(new Part(new Drawing("left", Square()), new Vector(1, 1)));
        plate.Parts.Add(new Part(new Drawing("right", Square()), new Vector(12, 1)));
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

    private static void SetBackingField(object target, string property, object value) =>
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

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

    private sealed class Host : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}
