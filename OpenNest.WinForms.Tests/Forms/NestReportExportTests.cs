using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Reads Application.OpenForms for the busy guards, so it must not overlap fill-lifetime tests
// that transiently open NestProgressForm.
[Collection("Fill operation lifetime")]
public class NestReportExportTests
{
    [Fact]
    public void MenuFollowsActiveDocument() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var item = Menu(host, "mnuFileExportNestReport");
        Assert.False(item.Enabled);

        using var form = new EditNestForm(new Nest("enabled job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        Assert.True(item.Enabled);

        form.Close();
        host.Activate();
        Assert.False(item.Enabled);
    });

    [Fact]
    public void JobBusyRejectsBeforeAnyDialog() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(new Nest("busy job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        SetField(host, "nestingInProgress", true);
        try
        {
            Menu(host, "mnuFileExportNestReport").PerformClick();
        }
        finally
        {
            SetField(host, "nestingInProgress", false);
        }

        Assert.Empty(host.DialogRequests);
        var failure = Assert.Single(host.Failures);
        Assert.Contains("busy job", failure);
        Assert.Contains("Finish or cancel", failure);
    });

    [Fact]
    public void InteractiveFillOnAnyViewOfTheNestRejects() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(new Nest("filling job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        SetBackingField(form.PlateView, "IsFillInProgress", true);
        try
        {
            Menu(host, "mnuFileExportNestReport").PerformClick();
            Assert.Empty(host.DialogRequests);
            Assert.Contains("Finish or cancel", Assert.Single(host.Failures));
        }
        finally
        {
            SetBackingField(form.PlateView, "IsFillInProgress", false);
        }

        // Cleared guard: the command now reaches the (test-intercepted) dialog.
        Menu(host, "mnuFileExportNestReport").PerformClick();
        Assert.Single(host.DialogRequests);
    });

    [Fact]
    public void BusyPlateActionOnAnyViewOfTheNestRejects() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(CreateJob("action job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        // The default ActionSelect reports busy while parts are selected.
        form.PlateView.SelectAll();
        Assert.True(form.PlateView.Actions.CurrentAction!.IsBusy());
        try
        {
            Menu(host, "mnuFileExportNestReport").PerformClick();
            Assert.Empty(host.DialogRequests);
            Assert.Contains("Finish or cancel", Assert.Single(host.Failures));
        }
        finally
        {
            form.PlateView.DeselectAll();
        }
        Assert.False(form.PlateView.Actions.CurrentAction!.IsBusy());
    });

    [Fact]
    public void InvalidNestNameRejectsBeforeAnyDialog() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(new Nest("bad/name")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();

        Menu(host, "mnuFileExportNestReport").PerformClick();

        Assert.Empty(host.DialogRequests);
        Assert.Contains("bad/name", Assert.Single(host.Failures));
    });

    [Fact]
    public void CancelWritesNothingAndNotifiesNothing() => RunSta(() =>
    {
        using var host = new TestMainForm { NextDialogPath = null };
        host.Show();
        using var form = new EditNestForm(CreateJob("cancel job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        var path = Path.Combine(Path.GetTempPath(), $"opennest-report-cancel-{Guid.NewGuid():N}.pdf");

        host.NextDialogPath = path;
        host.NextDialogResult = DialogResult.Cancel;
        Menu(host, "mnuFileExportNestReport").PerformClick();

        Assert.False(File.Exists(path));
        Assert.Empty(host.Failures);
        Assert.Empty(host.Successes);
    });

    [Fact]
    public void SuccessfulExportWritesPdfAndLeavesJobStateUntouched() => RunSta(() =>
    {
        using var host = new TestMainForm { NextDialogResult = DialogResult.OK };
        host.Show();
        var nest = CreateJob("export job");
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        form.PlateManager.LoadAt(1);
        // EnsureSentinel already appended a trailing empty plate behind the two loaded here.
        var plateCountBeforeExport = nest.Plates.Count;
        var path = Path.Combine(Path.GetTempPath(), $"opennest-report-ok-{Guid.NewGuid():N}.pdf");
        try
        {
            host.NextDialogPath = path;
            Menu(host, "mnuFileExportNestReport").PerformClick();

            Assert.Equal(path, Assert.Single(host.Successes));
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.True(bytes.Length > 1000);
            // Selected plate and job contents are untouched by reporting.
            Assert.Equal(1, form.PlateManager.CurrentIndex);
            Assert.Equal(plateCountBeforeExport, nest.Plates.Count);
            Assert.Single(nest.Plates[0].Parts);
            Assert.Equal(2, nest.Drawings.Count);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void RenderFailureLeavesExistingDestinationUnchanged() => RunSta(() =>
    {
        using var host = new TestMainForm { NextDialogResult = DialogResult.OK };
        host.Show();
        var nest = CreateJob("failure job");
        // Outside the report's supported text contract: fails font validation before layout.
        nest.Notes = "em\u2014dash";
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        var path = Path.Combine(Path.GetTempPath(), $"opennest-report-fail-{Guid.NewGuid():N}.pdf");
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(path, original);
        try
        {
            host.NextDialogPath = path;
            Menu(host, "mnuFileExportNestReport").PerformClick();

            Assert.Empty(host.Successes);
            var failure = Assert.Single(host.Failures);
            Assert.Contains("failure job", failure);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".*report-fail*.tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void BusyAfterDialogRejectsWriteWithoutTouchingDestination() => RunSta(() =>
    {
        using var form = new EditNestForm(CreateJob("late busy"));
        form.PlateView.SetOverlapAutoCheck(null);
        var targets = form.CaptureReportTargets(() => false);
        var path = Path.Combine(Path.GetTempPath(), $"opennest-report-late-{Guid.NewGuid():N}.pdf");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => form.WriteNestReport(targets, path, () => true));
            Assert.Contains("started while saving", error.Message);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void WriteRejectsAStaleTargetFromAnotherDocument() => RunSta(() =>
    {
        using var form = new EditNestForm(CreateJob("current job"));
        form.PlateView.SetOverlapAutoCheck(null);
        var stale = new EditNestForm.NestReportTargets(new Nest("other job"), "other.report.pdf");

        var error = Assert.Throws<InvalidOperationException>(
            () => form.WriteNestReport(stale, "ignored.pdf", () => false));

        Assert.Contains("active document changed", error.Message);
    });

    private static Nest CreateJob(string name)
    {
        var nest = new Nest(name) { Units = Units.Inches };
        var drawing = new Drawing("rect", Rectangle());
        drawing.Quantity.Required = 2;
        nest.Drawings.Add(drawing);
        nest.Drawings.Add(new Drawing("spare", Rectangle()));
        var first = new Plate(24, 48) { Quantity = 1 };
        first.Parts.Add(new Part(drawing, new Vector(2, 2)));
        var second = new Plate(24, 48) { Quantity = 2 };
        second.Parts.Add(new Part(drawing, new Vector(4, 4)));
        nest.Plates.Add(first);
        nest.Plates.Add(second);
        return nest;
    }

    private static Program Rectangle()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 2));
        program.Codes.Add(new LinearMove(0, 2));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    private static void SetField(object target, string name, object value) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    // Same reflection precedent PlateViewFillLifetimeTests uses for fill-lifetime state.
    private static void SetBackingField(object target, string property, object value) =>
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private sealed class TestMainForm : MainForm
    {
        public List<string> DialogRequests { get; } = [];
        public string? NextDialogPath { get; set; }
        public DialogResult NextDialogResult { get; set; } = DialogResult.OK;
        public List<string> Failures { get; } = [];
        public List<string> Successes { get; } = [];

        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }

        internal override string? ShowReportSaveDialog(string suggestedFileName)
        {
            DialogRequests.Add(suggestedFileName);
            return NextDialogResult == DialogResult.OK ? NextDialogPath : null;
        }

        internal override void ReportFailure(EditNestForm.NestReportTargets? targets, Exception error) =>
            Failures.Add($"{targets?.Nest.Name}|{error.Message}");

        internal override void ReportSuccess(string destination) => Successes.Add(destination);
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(90), "The STA test did not complete.");
}
