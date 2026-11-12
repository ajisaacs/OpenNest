using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.TestReflection;

namespace OpenNest.WinForms.Tests.Forms;

// Reads Application.OpenForms for the busy guards, so it must not overlap fill-lifetime tests
// that transiently open NestProgressForm.
[Collection("Fill operation lifetime")]
public class NestReportExportTests
{
    [Fact]
    public void CuttingToolbarExposesExistingActionsAndOnlyAvailablePosts() => RunSta(() =>
    {
        using var host = new TestMainForm();
        var toolbar = (ToolStrip)typeof(MainForm)
            .GetField("toolStrip1", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host)!;
        foreach (var name in new[] { "btnCutPlanner", "btnSequenceByPath", "btnPost" })
        {
            var item = Assert.Single(toolbar.Items.Find(name, false));
            Assert.Equal(24, item.Image!.Width);
            Assert.Equal(24, item.Image.Height);
        }

        var post = Assert.IsType<ToolStripDropDownButton>(toolbar.Items["btnPost"]);
        var menu = Menu(host, "mnuNestPost");
        Assert.Equal(menu.Visible, post.Visible);
        Assert.Equal(menu.DropDownItems.Count, post.DropDownItems.Count);
        // Without a document these toolbar shortcuts must be harmless.
        ((ToolStripButton)toolbar.Items["btnCutPlanner"]!).PerformClick();
        ((ToolStripButton)toolbar.Items["btnSequenceByPath"]!).PerformClick();
        foreach (ToolStripMenuItem item in menu.DropDownItems)
        {
            var toolbarItem = Assert.Single(post.DropDownItems.OfType<ToolStripMenuItem>(),
                candidate => candidate.Text == item.Text);
            Assert.Same(item.Tag, toolbarItem.Tag);
        }
    });

    [Fact]
    public void MenuFollowsActiveDocument() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var view = Menu(host, "mnuFileViewNestReport");
        Assert.False(view.Enabled);

        using var form = new EditNestForm(new Nest("enabled job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        Assert.True(view.Enabled);

        form.Close();
        host.Activate();
        Assert.False(view.Enabled);
    });

    [Fact]
    public void JobBusyRejectsBeforeRendering() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(new Nest("busy job")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        SetField(host, "nestingInProgress", true);
        try
        {
            Menu(host, "mnuFileViewNestReport").PerformClick();
        }
        finally
        {
            SetField(host, "nestingInProgress", false);
        }

        Assert.Empty(host.Viewed);
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
            Menu(host, "mnuFileViewNestReport").PerformClick();
            Assert.Empty(host.Viewed);
            Assert.Contains("Finish or cancel", Assert.Single(host.Failures));
        }
        finally
        {
            SetBackingField(form.PlateView, "IsFillInProgress", false);
        }

        // Cleared guard: the command now reaches the (test-intercepted) viewer.
        Menu(host, "mnuFileViewNestReport").PerformClick();
        NestReportViewerForm.DeleteReportFolder(Assert.Single(host.Viewed));
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
            Menu(host, "mnuFileViewNestReport").PerformClick();
            Assert.Empty(host.Viewed);
            Assert.Contains("Finish or cancel", Assert.Single(host.Failures));
        }
        finally
        {
            form.PlateView.DeselectAll();
        }
        Assert.False(form.PlateView.Actions.CurrentAction!.IsBusy());
    });

    [Fact]
    public void InvalidNestNameRejectsBeforeRendering() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        using var form = new EditNestForm(new Nest("bad/name")) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();

        Menu(host, "mnuFileViewNestReport").PerformClick();

        Assert.Empty(host.Viewed);
        Assert.Contains("bad/name", Assert.Single(host.Failures));
    });

    [Fact]
    public void ViewLeavesJobStateUntouched() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var nest = CreateJob("state job");
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        form.PlateManager.LoadAt(1);
        // EnsureSentinel already appended a trailing empty plate behind the two loaded here.
        var plateCountBeforeView = nest.Plates.Count;

        Menu(host, "mnuFileViewNestReport").PerformClick();

        var path = Assert.Single(host.Viewed);
        try
        {
            // Selected plate and job contents are untouched by reporting.
            Assert.Equal(1, form.PlateManager.CurrentIndex);
            Assert.Equal(plateCountBeforeView, nest.Plates.Count);
            Assert.Single(nest.Plates[0].Parts);
            Assert.Equal(2, nest.Drawings.Count);
        }
        finally
        {
            NestReportViewerForm.DeleteReportFolder(path);
        }
    });

    [Fact]
    public void RenderFailureLeavesExistingDestinationUnchanged() => RunSta(() =>
    {
        var nest = CreateJob("failure job");
        // Outside the report's supported text contract: fails font validation before layout.
        nest.Notes = "em\u2014dash";
        using var form = new EditNestForm(nest);
        form.PlateView.SetOverlapAutoCheck(null);
        var targets = form.CaptureReportTargets(() => false);
        var path = Path.Combine(Path.GetTempPath(), $"opennest-report-fail-{Guid.NewGuid():N}.pdf");
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(path, original);
        try
        {
            Assert.ThrowsAny<Exception>(() => form.WriteNestReport(targets, path, () => false));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".*report-fail*.tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void ViewRendersATemporaryPdfForTheViewer() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var nest = CreateJob("view job");
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();

        Menu(host, "mnuFileViewNestReport").PerformClick();

        Assert.Empty(host.Failures);
        var path = Assert.Single(host.Viewed);
        try
        {
            Assert.Equal("view job.report.pdf", Path.GetFileName(path));
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.Equal(2, nest.Drawings.Count);
        }
        finally
        {
            NestReportViewerForm.DeleteReportFolder(path);
        }
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    });

    [Fact]
    public void ViewRenderFailureOpensNoViewerAndLeavesNoTempFolder() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var nest = CreateJob("view failure");
        nest.Notes = "em—dash"; // Fails font validation before layout.
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        var reports = Path.Combine(Path.GetTempPath(), "OpenNest", "Reports");
        var before = Directory.Exists(reports) ? Directory.GetDirectories(reports).Length : 0;

        Menu(host, "mnuFileViewNestReport").PerformClick();

        Assert.Empty(host.Viewed);
        Assert.Contains("view failure", Assert.Single(host.Failures));
        Assert.Equal(before, Directory.Exists(reports) ? Directory.GetDirectories(reports).Length : 0);
    });

    [Fact]
    public void PartPrintoutsRenderEveryPartDrawingWithoutTouchingTheJob() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var nest = CreateJob("printout job");
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        var plateCount = nest.Plates.Count;
        var menu = Menu(host, "mnuFileViewPartPrintouts");
        Assert.True(menu.Enabled);

        menu.PerformClick();

        Assert.Empty(host.Failures);
        var path = Assert.Single(host.Viewed);
        try
        {
            Assert.Equal("printout job.printout.pdf", Path.GetFileName(path));
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.Equal(nest.Drawings.Count, pdf.PageCount);
            Assert.Equal(plateCount, nest.Plates.Count);
            Assert.Equal(2, nest.Drawings.Count);
        }
        finally
        {
            NestReportViewerForm.DeleteReportFolder(path);
        }
    });

    [Fact]
    public void DrawingListPrintoutRendersOnlyThatDrawing() => RunSta(() =>
    {
        using var host = new TestMainForm();
        host.Show();
        var nest = CreateJob("list job");
        var drawing = nest.Drawings.First();
        // Opened as BOM import does, without MainForm.LoadNest's wiring.
        using var form = new EditNestForm(nest) { MdiParent = host };
        form.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        form.Show();
        var list = (DrawingListBox)typeof(EditNestForm)
            .GetField("drawingListBox1", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        list.SelectedItem = drawing;

        list.ContextMenuStrip!.Items["mnuPartPrintout"]!.PerformClick();

        var path = Assert.Single(host.Viewed);
        try
        {
            Assert.Equal($"{drawing.Name}.printout.pdf", Path.GetFileName(path));
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.Equal(1, pdf.PageCount);
        }
        finally
        {
            NestReportViewerForm.DeleteReportFolder(path);
        }
    });

    [Fact]
    public void ViewerCentersOnItsOwnerInsideTheScreen()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        var owner = new System.Drawing.Rectangle(area.Left + 100, area.Top + 50, 600, 400);

        var small = NestReportViewerForm.CenteredBounds(owner, new System.Drawing.Size(200, 100));
        Assert.Equal(owner.Left + 200, small.Left);
        Assert.Equal(owner.Top + 150, small.Top);

        // Larger than the owner: still centered on it, then shrunk and kept on screen.
        var large = NestReportViewerForm.CenteredBounds(owner,
            new System.Drawing.Size(area.Width + 500, area.Height + 500));
        Assert.Equal(area, large);
    }

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
        var drawing = new Drawing("rect", TestPrograms.Rectangle(4, 2));
        drawing.Quantity.Required = 2;
        nest.Drawings.Add(drawing);
        nest.Drawings.Add(new Drawing("spare", TestPrograms.Rectangle(4, 2)));
        var first = new Plate(24, 48) { Quantity = 1 };
        first.Parts.Add(new Part(drawing, new Vector(2, 2)));
        var second = new Plate(24, 48) { Quantity = 2 };
        second.Parts.Add(new Part(drawing, new Vector(4, 4)));
        nest.Plates.Add(first);
        nest.Plates.Add(second);
        return nest;
    }

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    private static void SetField(object target, string name, object value) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    // Same reflection precedent PlateViewFillLifetimeTests uses for fill-lifetime state.
    private sealed class TestMainForm : MainForm
    {
        public List<string> Failures { get; } = [];
        public List<string> Viewed { get; } = [];

        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }

        internal override void ReportFailure(EditNestForm.NestReportTargets? targets, Exception error) =>
            Failures.Add($"{targets?.Nest.Name}|{error.Message}");

        internal override void ShowReportViewer(string pdfPath, EditNestForm.NestReportTargets targets) =>
            Viewed.Add(pdfPath);

        internal override void ShowPrintoutViewer(string pdfPath, string nestName, string suggestedFileName) =>
            Viewed.Add(pdfPath);

        internal override void PrintoutFailure(Exception error) => Failures.Add(error.Message);
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(90), "The STA test did not complete.");
}
