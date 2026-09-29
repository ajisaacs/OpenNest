using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

[CollectionDefinition("Overlap overlay", DisableParallelization = true)]
public class OverlapOverlayCollection;

[Collection("Overlap overlay")]
public class PlateOverlapOverlayTests
{
    [Fact]
    public void RestartAndCancelRejectLateCompletionsOnTheUiThread() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var uiThread = Environment.CurrentManagedThreadId;
        run.View.OverlapStateChanged += (_, _) => Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
        var first = run.Start();
        var old = run.Next();
        var second = run.Start();
        var current = run.Next();
        Assert.True(old.Token.IsCancellationRequested);
        old.Complete();
        run.Pump(first);
        Assert.Equal(OverlapCheckStatus.Checking, run.View.OverlapStatus);
        current.Complete();
        run.Pump(second);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Single(run.View.OverlapReport.Pairs);

        var canceled = run.Start();
        var late = run.Next();
        run.View.CancelOverlapCheck();
        Assert.True(late.Token.IsCancellationRequested);
        late.Complete();
        run.Pump(canceled);
        Assert.Equal(OverlapCheckStatus.Canceled, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
    });

    [Theory]
    [InlineData("move")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("reorder")]
    [InlineData("editor")]
    [InlineData("plate")]
    public void EditOrPlateSwitchRejectsPendingReport(string edit) => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        var work = run.Next();
        var plate = run.View.Plate;
        switch (edit)
        {
            case "move": plate.Parts[0].Offset(1e-10, 0); break;
            case "add": plate.Parts.Add(Rectangle()); break;
            case "remove": plate.Parts.RemoveAt(0); break;
            case "replace": plate.Parts[0] = Rectangle(); break;
            case "reorder": (plate.Parts[0], plate.Parts[1]) = (plate.Parts[1], plate.Parts[0]); break;
            case "editor": run.View.InvalidateOverlapCheck(); break;
            case "plate": run.View.Plate = new Plate(); break;
        }
        if (edit != "move")
            Assert.True(work.Token.IsCancellationRequested);
        work.Complete();
        run.Pump(task);
        Assert.True(work.Token.IsCancellationRequested);
        Assert.Null(run.View.OverlapReport);
        Assert.Equal(edit == "plate" ? OverlapCheckStatus.NotChecked : OverlapCheckStatus.Stale,
            run.View.OverlapStatus);
    });

    [Fact]
    public void PaintChecksFreshnessEvenWhenDisplayIsOff() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(240, 240);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.View.Plate.Parts[0].Offset(1e-10, 0);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
    });

    [Fact]
    public void DisplayPanZoomAndPreviewDoNotAnalyzeOrChangeCommittedParts() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var parts = run.View.Plate.Parts.ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var quantities = parts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        run.View.SelectAll();
        var selection = run.View.SelectedParts.ToArray();
        run.View.SetActiveParts(new List<Part> { Rectangle() });
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.Finish();
        Assert.Equal(OverlapDisplayMode.Areas, run.View.OverlapDisplay);
        Assert.Single(run.View.OverlapReport.Pairs); // Preview is not a third input.
        var report = run.View.OverlapReport;
        using var image = new Bitmap(240, 240);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        var path = run.View.OverlapOverlay.CachedPath;
        Assert.Equal(FillMode.Winding, path.FillMode);
        run.View.Pan(35, -20);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Same(path, run.View.OverlapOverlay.CachedPath);
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.View.OverlapDisplay = OverlapDisplayMode.Areas;
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Same(path, run.View.OverlapOverlay.CachedPath);
        run.View.ZoomToPoint(new Vector(), 2);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotSame(path, run.View.OverlapOverlay.CachedPath);
        Assert.Same(report, run.View.OverlapReport);
        Assert.Equal(1, run.Calls);
        Assert.Equal(parts, run.View.Plate.Parts.ToArray());
        Assert.Equal(locations, parts.Select(p => p.Location));
        Assert.Equal(programs, parts.Select(p => p.Program));
        Assert.Equal(quantities, parts.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.Equal(selection, run.View.SelectedParts);
    });

    [Fact]
    public void TripleOverlapUsesOneFillAndGraphCoordinatesDoNotDoublePan() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Plate.Parts.Clear();
        run.View.Plate.Parts.Add(Rectangle());
        run.View.Plate.Parts.Add(Rectangle(2));
        run.View.Plate.Parts.Add(Rectangle(3));
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 20);
        run.View.SetOrigin(60, 220);
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.TranslateTransform(60, 220);
        var transform = graphics.Transform.Elements;
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Equal(transform, graphics.Transform.Elements); // Label restores graph space.
        Assert.Equal(image.GetPixel(110, 180), image.GetPixel(130, 180)); // Double/triple material, same alpha.
        Assert.NotEqual(Color.White.ToArgb(), image.GetPixel(130, 180).ToArgb());
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(70, 180).ToArgb());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleDestructionOrDisposalNeverResurrectsRequests(bool dispose) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        var task = run.Start();
        var work = run.Next();
        if (dispose)
            run.View.Dispose();
        else
            run.View.Recreate();
        Assert.True(work.Token.IsCancellationRequested);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        work.Complete();
        run.Pump(task);
        Assert.Null(run.View.OverlapReport);
        Assert.False(run.View.IsOverlapCheckRunning);
    });

    [Fact]
    public void CompletedPathIsReleasedOnHandleRecreationAndCannotReappear() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        run.View.Recreate();
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        Assert.Null(run.View.OverlapReport);
        Assert.Equal(1, run.Calls);
    });

    [Fact]
    public void FailureAndIncompleteAreVisibleAndNeverReportedAsClear() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        run.Next().Fail();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Failed, run.View.OverlapStatus);
        Assert.Contains("failed", run.View.Status);
        Assert.Null(run.View.OverlapReport);
        run.View.Plate.Parts.Add(new Part(new Drawing("invalid", new Program())));
        run.Finish();
        Assert.Equal(OverlapCheckStatus.Incomplete, run.View.OverlapStatus);
        Assert.Contains("incomplete: 1 overlapping pairs; 1 parts", run.View.Status);
    });

    [Fact]
    public void SnapshotKeepsOriginalLabelsWhenLiveNamesChange() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        var work = run.Next();
        run.View.Plate.Parts[0].BaseDrawing.Name = "new label";
        work.Complete();
        run.Pump(task);
        Assert.Equal("same name", Assert.Single(run.View.OverlapReport.Pairs).PartAName);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
    });

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    public void CollectionEventsImmediatelyDiscardCurrentReportAndPath(string edit) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        switch (edit)
        {
            case "add": run.View.Plate.Parts.Add(Rectangle()); break;
            case "remove": run.View.Plate.Parts.RemoveAt(0); break;
            case "replace": run.View.Plate.Parts[0] = Rectangle(); break;
            case "clear": run.View.Plate.Parts.Clear(); break;
        }
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
    });

    [Fact]
    public void MenusFollowMdiActivationDocumentDisplayAndRunningState() => RunSta(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var check = Menu(host, "mnuOverlapCheckActive");
        var cancel = Menu(host, "mnuOverlapCancel");
        var off = Menu(host, "mnuOverlapOff");
        var areas = Menu(host, "mnuOverlapAreas");
        var centroids = Menu(host, "mnuOverlapCentroids");
        var both = Menu(host, "mnuOverlapBoth");
        Assert.False(check.Enabled);
        Assert.False(cancel.Enabled);
        Assert.False(off.Enabled);
        Assert.False(areas.Enabled);
        Assert.False(centroids.Enabled);
        Assert.False(both.Enabled);
        Assert.All(new[] { check, cancel, off, areas, centroids, both }, item => Assert.Equal(Keys.None, item.ShortcutKeys));

        using var first = new EditNestForm(new Nest("first")) { MdiParent = host };
        // Manual-command test: an auto-check timer pumped during a wait would race its requests.
        first.PlateView.SetOverlapAutoCheck(null);
        first.Show();
        Assert.True(check.Enabled);
        Assert.True(areas.Checked);
        off.PerformClick();
        Assert.Equal(OverlapDisplayMode.Off, first.OverlapDisplay);
        using var second = new EditNestForm(new Nest("second")) { MdiParent = host };
        second.PlateView.SetOverlapAutoCheck(null);
        second.Show();
        Assert.True(areas.Checked);
        Assert.False(off.Checked);
        first.Activate();
        Assert.True(off.Checked);
        Assert.False(areas.Checked);
        Assert.Equal(OverlapDisplayMode.Areas, second.OverlapDisplay);
        centroids.PerformClick();
        Assert.True(centroids.Checked);
        Assert.False(off.Checked);
        Assert.Equal(OverlapDisplayMode.Centroids, first.OverlapDisplay);
        second.Activate();
        both.PerformClick();
        Assert.True(both.Checked);
        Assert.False(centroids.Checked);
        Assert.Equal(OverlapDisplayMode.Both, second.OverlapDisplay);
        first.Activate();
        Assert.True(centroids.Checked);
        Assert.False(both.Checked);
        off.PerformClick();

        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        using var pending = new BlockingCollection<Work>();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            first.PlateView.OverlapOverlay.Analyze = (snapshot, token) =>
            {
                var request = new Work(snapshot, token);
                pending.Add(request);
                return request.WaitForResult();
            };
            var task = first.CheckOverlapsAsync();
            Assert.True(pending.TryTake(out var request, TimeSpan.FromSeconds(15)));
            Assert.True(cancel.Enabled);
            Assert.True(areas.Checked); // Checking from Off selects Areas.
            cancel.PerformClick();
            Assert.False(cancel.Enabled);
            Assert.True(request!.Token.IsCancellationRequested);
            request.Complete();
            context.Pump(task);
            Assert.Equal(OverlapCheckStatus.Canceled, first.PlateView.OverlapStatus);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        first.Dispose();
        second.Dispose();
        Assert.False(check.Enabled);
        Assert.False(cancel.Enabled);
    });

    [Fact]
    public void CheckCommandCapturesUnitsAndNamesUntilTheNextCheck() => RunSta(() =>
    {
        using var form = new EditNestForm(new Nest("units") { Units = Units.Millimeters });
        form.PlateView.SetOverlapAutoCheck(null); // Manual-command test; see the menu test.
        form.Show();
        form.PlateView.Plate.Parts.Add(Rectangle());
        form.PlateView.Plate.Parts.Add(Rectangle(1));
        form.OverlapDisplay = OverlapDisplayMode.Both;
        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        var started = new TaskCompletionSource<PlateOverlapSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<PlateOverlapReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? task = null;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            form.PlateView.OverlapOverlay.Analyze = (snapshot, _) =>
            {
                started.TrySetResult(snapshot);
                return WaitForWorkerResult(result.Task);
            };
            task = form.CheckOverlapsAsync();
            var snapshot = WaitForSnapshot(started.Task);
            form.Nest.Units = Units.Inches;
            form.PlateView.Plate.Parts[0].BaseDrawing.Name = "changed";
            result.SetResult(PlateOverlapAnalyzer.Analyze(snapshot));
            context.Pump(task);
            var center = form.PlateView.PointWorldToControl(form.PlateView.OverlapReport.Pairs[0].Centroid);
            form.PlateView.OverlapOverlay.UpdateHover(center);
            Assert.Contains(" mm²", form.PlateView.OverlapOverlay.HoverText);
            Assert.Contains("same name / same name", form.PlateView.OverlapOverlay.HoverText);
            Assert.DoesNotContain("changed", form.PlateView.OverlapOverlay.HoverText);
            form.PlateView.OverlapOverlay.Analyze = PlateOverlapAnalyzer.Analyze;
            task = form.CheckOverlapsAsync();
            Assert.Null(form.PlateView.OverlapOverlay.HoverText);
            context.Pump(task);
            form.PlateView.OverlapOverlay.UpdateHover(center);
            Assert.Contains(" in²", form.PlateView.OverlapOverlay.HoverText);
            Assert.Contains("changed / same name", form.PlateView.OverlapOverlay.HoverText);
            Assert.Equal(OverlapDisplayMode.Both, form.OverlapDisplay);
        }
        finally
        {
            form.CancelOverlapCheck();
            result.TrySetCanceled();
            try { if (task != null) context.Pump(task); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    });

    [Fact]
    public void CleanupReleasesDequeuedWorkersEvenWithoutCompleteOrFail() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var first = run.Start();
        var old = run.Next();
        var second = run.Start();
        var current = run.Next();
        run.Dispose();
        Assert.True(old.Result.Task.IsCompleted);
        Assert.True(current.Result.Task.IsCompleted);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
    });

    [Theory]
    [InlineData(OverlapDisplayMode.Areas)]
    [InlineData(OverlapDisplayMode.Centroids)]
    [InlineData(OverlapDisplayMode.Both)]
    public void RecheckPreservesEachVisibleModeAndDisplayOnlyRepaints(OverlapDisplayMode mode) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.OverlapDisplay = mode;
        run.Finish();
        Assert.Equal(mode, run.View.OverlapDisplay);
        run.Finish();
        Assert.Equal(mode, run.View.OverlapDisplay);
        var report = run.View.OverlapReport;
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        foreach (var display in Enum.GetValues<OverlapDisplayMode>())
        {
            run.View.OverlapDisplay = display;
            run.View.OverlapOverlay.Draw(graphics);
            Assert.Same(report, run.View.OverlapReport);
        }
        Assert.Equal(2, run.Calls);
    });

    [Fact]
    public void CentroidMarkerUsesGraphCoordinatesAndFixedScreenSizeAcrossZoom() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.OverlapDisplay = OverlapDisplayMode.Centroids;
        run.Finish();
        var pair = Assert.Single(run.View.OverlapReport.Pairs);
        using var image = new Bitmap(500, 500);
        using var graphics = Graphics.FromImage(image);
        foreach (var zoom in new[] { 20f, 2f })
        {
            run.View.ZoomToPoint(new Vector(), zoom);
            run.View.SetOrigin(60, 320);
            graphics.ResetTransform();
            graphics.Clear(Color.White);
            graphics.TranslateTransform(60, 320);
            run.View.OverlapOverlay.Draw(graphics);
            Assert.Null(run.View.OverlapOverlay.CachedPath);
            var center = run.View.PointWorldToControl(pair.Centroid);
            var radius = (int)OverlapPairPresentation.MarkerHalfSize(run.View.DeviceDpi);
            Assert.NotEqual(Color.White.ToArgb(), image.GetPixel(center.X - radius + 1, center.Y).ToArgb());
            Assert.Equal(Color.White.ToArgb(), image.GetPixel(center.X - radius - 4, center.Y).ToArgb());
            Assert.Equal(Color.White.ToArgb(), image.GetPixel(center.X - 60, center.Y).ToArgb());
            run.View.MoveTo(new Point(center.X + (int)OverlapPairPresentation.HitRadius(run.View.DeviceDpi), center.Y));
            Assert.Contains("Pair 1/2", run.View.OverlapOverlay.HoverText);
            run.View.MoveTo(new Point(center.X + (int)OverlapPairPresentation.HitRadius(run.View.DeviceDpi) + 2, center.Y));
            Assert.Null(run.View.OverlapOverlay.HoverText);
        }
        Assert.Equal(1, run.Calls);
    });

    [Fact]
    public void MarkerAndHoverUsePairMaterialCentroidRatherThanBoundsCenter() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Plate.Parts.Clear();
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(6, 0));
        program.Codes.Add(new LinearMove(0, 3));
        program.Codes.Add(new LinearMove(0, 0));
        var drawing = new Drawing("triangle", program);
        run.View.Plate.Parts.Add(new Part(drawing));
        run.View.Plate.Parts.Add(new Part(drawing));
        run.View.OverlapDisplay = OverlapDisplayMode.Centroids;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 30);
        run.View.SetOrigin(60, 220);
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.TranslateTransform(60, 220);
        run.View.OverlapOverlay.Draw(graphics);
        var materialCenter = run.View.PointWorldToControl(new Vector(6.0 / 3, 3.0 / 3));
        var boundsCenter = run.View.PointWorldToControl(new Vector(6.0 / 2, 3.0 / 2));
        Assert.NotEqual(Color.White.ToArgb(), image.GetPixel(materialCenter.X, materialCenter.Y).ToArgb());
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(boundsCenter.X, boundsCenter.Y).ToArgb());
        run.View.MoveTo(materialCenter);
        Assert.Contains("Pair 1/2", run.View.OverlapOverlay.HoverText);
        run.View.MoveTo(boundsCenter);
        Assert.Null(run.View.OverlapOverlay.HoverText);
    });

    [Fact]
    public void BothRetainsAreaPathAndCoincidentHoverListsEveryPairWithoutChangingSelection() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Plate.Parts.Clear();
        run.View.Plate.Parts.Add(Rectangle());
        run.View.Plate.Parts.Add(Rectangle());
        run.View.Plate.Parts.Add(Rectangle());
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 20);
        run.View.SetOrigin(60, 220);
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.TranslateTransform(60, 220);
        run.View.OverlapOverlay.Draw(graphics);
        var path = run.View.OverlapOverlay.CachedPath;
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Same(path, run.View.OverlapOverlay.CachedPath);
        var center = run.View.PointWorldToControl(new Vector(2, 2));
        run.View.MoveTo(center);
        var text = run.View.OverlapOverlay.HoverText!;
        Assert.True(text.IndexOf("Pair 1/2", StringComparison.Ordinal) < text.IndexOf("Pair 1/3", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Pair 1/3", StringComparison.Ordinal) < text.IndexOf("Pair 2/3", StringComparison.Ordinal));
        Assert.Empty(run.View.SelectedParts);
        run.View.ClickAt(center);
        Assert.NotEmpty(run.View.SelectedParts); // Informational hover does not consume the selection click.
        Assert.Null(run.View.OverlapOverlay.HoverText);
        Assert.Equal(1, run.Calls);
    });

    [Fact]
    public void PairDetailsOnlySuppressNormalTooltipWhileVisible() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 20);
        run.View.SetOrigin(60, 220);
        run.View.MoveTo(run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var hoverCheck = typeof(PlateView).GetMethod("HoverCheck", flags)!;
        var normalVisible = typeof(PlateView).GetField("showTooltip", flags)!;
        hoverCheck.Invoke(run.View, null); // Fire the existing normal-tooltip timer deterministically.
        Assert.True((bool)normalVisible.GetValue(run.View)!);
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        Assert.True(run.View.OverlapOverlay.DrawHover(graphics));
        Assert.True((bool)normalVisible.GetValue(run.View)!);
        run.View.OverlapDisplay = OverlapDisplayMode.Areas;
        Assert.False(run.View.OverlapOverlay.DrawHover(graphics));
        Assert.True((bool)normalVisible.GetValue(run.View)!); // PlateView now draws the original tooltip.
        run.View.LeaveView();
        hoverCheck.Invoke(run.View, null); // A queued timer cannot resurrect it after leave.
        Assert.False((bool)normalVisible.GetValue(run.View)!);
    });

    [Theory]
    [InlineData("leave")]
    [InlineData("stale-move")]
    [InlineData("stale-paint")]
    [InlineData("invalidate")]
    [InlineData("mode")]
    [InlineData("zoom")]
    [InlineData("pan")]
    [InlineData("resize")]
    [InlineData("plate")]
    [InlineData("restart-cancel")]
    [InlineData("recreate")]
    public void HoverClearsBeforeStaleDetailsCanBeShown(string change) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 20);
        run.View.SetOrigin(60, 220);
        var center = run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid);
        run.View.MoveTo(center);
        Assert.NotNull(run.View.OverlapOverlay.HoverText);
        switch (change)
        {
            case "leave": run.View.LeaveView(); break;
            case "stale-move":
                run.View.Plate.Parts[0].Offset(1e-10, 0);
                run.View.MoveTo(center);
                break;
            case "stale-paint":
                run.View.Plate.Parts[0].Offset(1e-10, 0);
                using (var image = new Bitmap(300, 300))
                using (var graphics = Graphics.FromImage(image))
                    Assert.False(run.View.OverlapOverlay.DrawHover(graphics));
                break;
            case "invalidate": run.View.InvalidateOverlapCheck(); break;
            case "mode": run.View.OverlapDisplay = OverlapDisplayMode.Centroids; break;
            case "zoom": run.View.ZoomToPoint(new Vector(), 2); break;
            case "pan": run.View.Pan(20, 10); break;
            case "resize": run.View.Size = new System.Drawing.Size(300, 200); break;
            case "plate": run.View.Plate = new Plate(); break;
            case "restart-cancel":
                var task = run.Start();
                var work = run.Next();
                Assert.Null(run.View.OverlapOverlay.HoverText);
                run.View.CancelOverlapCheck();
                work.Complete();
                run.Pump(task);
                break;
            case "recreate": run.View.Recreate(); break;
        }
        Assert.Null(run.View.OverlapOverlay.HoverText);
    });

    [Fact]
    public void CrowdedHoverPagesStayInViewportAndReachLastPairWithoutSelectionOrWheelCapture() => RunSta(() =>
    {
        using var run = new OverlayRun();
        PrepareCrowdedHover(run);
        var report = run.View.OverlapReport;
        var positions = run.View.Plate.Parts.Select(p => p.Location).ToArray();
        run.View.SelectAll();
        var selection = run.View.SelectedParts.ToArray();
        using var image = new Bitmap(run.View.Width, run.View.Height);
        using var graphics = Graphics.FromImage(image);
        var overlay = run.View.OverlapOverlay;
        Assert.True(overlay.DrawHover(graphics));
        var pages = overlay.HoverPages;
        Assert.True(pages.PageCount > 1);
        var content = new List<string>();
        for (var page = 0; page < pages.PageCount; page++)
        {
            Assert.True(overlay.DrawHover(graphics));
            Assert.True(new RectangleF(PointF.Empty, run.View.ClientSize).Contains(overlay.HoverBounds));
            Assert.Equal(page, pages.PageIndex);
            content.AddRange(pages.Lines);
            Assert.Contains("PgUp/PgDn", string.Join("", pages.NavigationLines));
            Assert.True(run.View.Command(Keys.PageDown));
        }
        Assert.Contains("Pair 11/12", string.Concat(content));
        Assert.Contains(" in²", string.Concat(content));
        Assert.Equal(overlay.HoverText.Replace("\n", ""), string.Concat(content));
        Assert.Equal(pages.PageCount - 1, pages.PageIndex);
        Assert.True(run.View.Command(Keys.PageUp));
        Assert.Equal(pages.PageCount - 2, pages.PageIndex);
        var lastIndex = pages.PageIndex;
        Assert.False(run.View.Command(Keys.Control | Keys.PageDown));
        Assert.Equal(lastIndex, pages.PageIndex);
        run.View.MoveTo(run.View.PointWorldToControl(report.Pairs[0].Centroid));
        Assert.Equal(lastIndex, pages.PageIndex); // Moving within the same hit group retains the page.
        Assert.Equal(selection, run.View.SelectedParts);
        Assert.Equal(positions, run.View.Plate.Parts.Select(p => p.Location));
        Assert.Same(report, run.View.OverlapReport);
        Assert.Equal(1, run.Calls);

        var scale = run.View.ViewScale;
        run.View.WheelAt(run.View.PointWorldToControl(report.Pairs[0].Centroid));
        Assert.True(run.View.ViewScale > scale); // Wheel remains zoom, never paging.
        Assert.Null(overlay.HoverText);
        Assert.Null(overlay.HoverPages);
        Assert.False(run.View.Command(Keys.PageDown));
        Assert.Equal(selection, run.View.SelectedParts);
        Assert.Equal(1, run.Calls);
    });

    [Fact]
    public void MovingDirectlyToAnotherCoincidentGroupStartsItsFirstPage() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Size = new System.Drawing.Size(300, 160);
        run.View.Plate.Parts.Clear();
        for (var i = 0; i < 12; i++)
            run.View.Plate.Parts.Add(Rectangle(i < 6 ? 0 : 8));
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 15);
        run.View.SetOrigin(60, 120);
        run.View.MoveTo(run.View.PointWorldToControl(new Vector(2, 2)));
        using var image = new Bitmap(300, 160);
        using var graphics = Graphics.FromImage(image);
        var overlay = run.View.OverlapOverlay;
        Assert.True(overlay.DrawHover(graphics));
        Assert.True(run.View.Command(Keys.PageDown));
        Assert.Equal(1, overlay.HoverPages.PageIndex);
        run.View.MoveTo(run.View.PointWorldToControl(new Vector(10, 2)));
        Assert.Null(overlay.HoverPages);
        Assert.True(overlay.DrawHover(graphics));
        Assert.NotNull(overlay.HoverPages);
        Assert.Equal(0, overlay.HoverPages.PageIndex);
        Assert.Contains("Pair 7/8", string.Concat(overlay.HoverPages.Lines));
        Assert.Equal(1, run.Calls);
    });

    [Theory]
    [InlineData("hover")]
    [InlineData("leave")]
    [InlineData("mode")]
    [InlineData("stale-key")]
    [InlineData("report")]
    [InlineData("resize")]
    [InlineData("font")]
    [InlineData("click")]
    [InlineData("drag")]
    public void PagingResetsWithHoverLifetimeAndInactiveKeysAreNotConsumed(string change) => RunSta(() =>
    {
        using var run = new OverlayRun();
        Assert.False(run.View.Command(Keys.PageDown));
        PrepareCrowdedHover(run);
        using var image = new Bitmap(run.View.Width, run.View.Height);
        using var graphics = Graphics.FromImage(image);
        var overlay = run.View.OverlapOverlay;
        Assert.True(overlay.DrawHover(graphics));
        Assert.True(run.View.Command(Keys.PageDown));
        Assert.Equal(1, overlay.HoverPages.PageIndex);
        var center = run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid);
        using var largerFont = new Font(run.View.Font.FontFamily, run.View.Font.Size + 1);
        switch (change)
        {
            case "hover": run.View.MoveTo(new Point(5, 5)); break;
            case "leave": run.View.LeaveView(); break;
            case "mode": run.View.OverlapDisplay = OverlapDisplayMode.Areas; break;
            case "stale-key":
                run.View.Plate.Parts[0].Offset(1e-10, 0);
                Assert.False(run.View.Command(Keys.PageDown));
                break;
            case "report": run.Finish(); break;
            case "resize": run.View.Width += 10; break;
            case "font": run.View.Font = largerFont; break;
            case "click":
                run.View.ClickAt(center);
                Assert.NotEmpty(run.View.SelectedParts);
                break;
            case "drag": run.View.DragAt(center); break;
        }
        Assert.Null(overlay.HoverText);
        Assert.Null(overlay.HoverPages);
        Assert.False(run.View.Command(Keys.PageDown));
        if (change == "stale-key")
            run.Finish();
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.View.MoveTo(center);
        Assert.True(overlay.DrawHover(graphics));
        Assert.NotNull(overlay.HoverPages);
        Assert.Equal(0, overlay.HoverPages.PageIndex);
    });

    [Fact]
    public void LongNameAndTinyViewNeverSilentlyElideTheOnlyPair() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Size = new System.Drawing.Size(150, 120);
        run.View.Plate.Parts[0].BaseDrawing.Name = new string('W', 250);
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 10);
        run.View.SetOrigin(30, 90);
        var center = run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid);
        run.View.MoveTo(center);
        using var image = new Bitmap(150, 120);
        using var graphics = Graphics.FromImage(image);
        var overlay = run.View.OverlapOverlay;
        Assert.True(overlay.DrawHover(graphics));
        Assert.False(overlay.HoverPages.NeedsLargerViewport);
        Assert.True(overlay.HoverPages.PageCount > 1);
        var content = new List<string>();
        for (var i = 0; i < overlay.HoverPages.PageCount; i++)
        {
            Assert.True(overlay.DrawHover(graphics));
            Assert.True(new RectangleF(PointF.Empty, run.View.ClientSize).Contains(overlay.HoverBounds));
            content.AddRange(overlay.HoverPages.Lines);
            run.View.Command(Keys.PageDown);
        }
        Assert.Equal(overlay.HoverText.Replace("\n", ""), string.Concat(content));
        run.View.Size = new System.Drawing.Size(40, 20);
        overlay.UpdateHover(run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid));
        Assert.True(overlay.DrawHover(graphics));
        Assert.True(overlay.HoverPages.NeedsLargerViewport);
        Assert.False(run.View.Command(Keys.PageDown));
    });

    [Fact]
    public void AutoCheckRunsOnceTheLayoutSettlesAndRechecksAfterADrag() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Status = "Fill: 2 parts in 5 ms";
        run.EnableAutoCheck();
        var overlay = run.View.OverlapOverlay;
        Assert.True(overlay.IsAutoCheckScheduled);
        Assert.Equal("Overlaps: check pending…", overlay.Message);

        var task = run.AutoTick();
        run.Next().Complete();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.False(overlay.IsAutoCheckScheduled);
        Assert.Equal(1, run.Calls);
        Assert.Equal("Fill: 2 parts in 5 ms", run.View.Status); // canvas label only
        Assert.Equal("Overlaps: 1 pairs", overlay.Message);

        // A drag changes only Part.Location: no collection event, so paint must notice it.
        run.View.Plate.Parts[1].Offset(0.5, 0);
        run.Paint();
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.True(overlay.IsAutoCheckScheduled);
        run.View.Plate.Parts[1].Offset(0.5, 0);      // still moving when the timer fires
        Assert.Same(Task.CompletedTask, run.AutoTick());
        Assert.True(overlay.IsAutoCheckScheduled);    // waited another quiet period
        Assert.Equal(1, run.Calls);
        task = run.AutoTick();
        run.Next().Complete();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Equal(2, run.Calls);
    });

    [Fact]
    public void AutoCheckWaitsForInteractionsAndRespectsCancel() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var busy = true;
        run.EnableAutoCheck(() => busy);
        Assert.Same(Task.CompletedTask, run.AutoTick());
        Assert.True(run.View.OverlapOverlay.IsAutoCheckScheduled);
        Assert.Equal(0, run.Calls);
        busy = false;
        var task = run.AutoTick();
        var work = run.Next();
        run.View.CancelOverlapCheck();
        work.Complete();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Canceled, run.View.OverlapStatus);
        run.Paint();
        Assert.False(run.View.OverlapOverlay.IsAutoCheckScheduled); // no retry of a canceled layout

        run.View.Plate.Parts.Add(Rectangle(2));                     // but a new edit rechecks
        Assert.True(run.View.OverlapOverlay.IsAutoCheckScheduled);
        task = run.AutoTick();
        run.Next().Complete();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Equal(3, run.View.OverlapReport.Pairs.Count);
    });

    [Fact]
    public void AutoCheckSupersedesARunningCheckWhenTheLayoutChanges() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.EnableAutoCheck();
        var first = run.AutoTick();
        var old = run.Next();
        run.View.Plate.Parts[0].Offset(-10, 0);   // user keeps editing while the check runs
        run.Paint();
        Assert.True(old.Token.IsCancellationRequested);
        Assert.True(run.View.OverlapOverlay.IsAutoCheckScheduled);
        old.Complete();
        run.Pump(first);
        Assert.Null(run.View.OverlapReport);      // the old layout's result is never shown
        var second = run.AutoTick();
        run.Next().Complete();
        run.Pump(second);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Empty(run.View.OverlapReport!.Pairs);
    });

    [Fact]
    public void AutoCheckKeepsDisplayOffButManualCheckStillShowsAreasAndReportsToStatusBar() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.EnableAutoCheck();
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        var task = run.AutoTick();
        run.Next().Complete();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Equal(OverlapDisplayMode.Off, run.View.OverlapDisplay);

        run.View.Plate.Parts[1].Offset(0.25, 0);
        task = run.Start();                       // manual Check Active Plate
        run.Next().Complete();
        run.Pump(task);
        Assert.Equal(OverlapDisplayMode.Areas, run.View.OverlapDisplay);
        Assert.Equal("Overlaps: 1 pairs", run.View.Status);
        Assert.False(run.View.OverlapOverlay.IsAutoCheckScheduled);
    });

    [Fact]
    public void DefaultAnalyzerRechecksIncrementallyAndEditorsDropTheCache() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.OverlapOverlay.Analyze = null;   // production path: incremental real analyzer
        run.View.Plate.Parts.Add(Rectangle(20));
        run.View.Plate.Parts.Add(Rectangle(21));
        var task = run.Start();
        run.Pump(task);
        var untouched = run.View.OverlapReport.Pairs.Single(pair => pair.PartAId == 2);
        run.View.Plate.Parts[1].Offset(1, 0);
        task = run.Start();
        run.Pump(task);
        Assert.Same(untouched.Regions, run.View.OverlapReport.Pairs.Single(pair => pair.PartAId == 2).Regions);
        Assert.Equal(8, run.View.OverlapReport.Pairs.Single(pair => pair.PartAId == 0).Area, 9);

        run.View.InvalidateOverlapCheck();        // converter may edit programs in place
        task = run.Start();
        run.Pump(task);
        Assert.NotSame(untouched.Regions, run.View.OverlapReport.Pairs.Single(pair => pair.PartAId == 2).Regions);
    });

    [Fact]
    public void EditNestFormEnablesAutoCheck() => RunSta(() =>
    {
        using var form = new EditNestForm(new Nest("auto"));
        Assert.True(form.PlateView.OverlapOverlay.IsAutoCheckEnabled);
        using var standalone = new PlateView();
        Assert.False(standalone.OverlapOverlay.IsAutoCheckEnabled);
    });

    private static void PrepareCrowdedHover(OverlayRun run)
    {
        run.View.Size = new System.Drawing.Size(260, 160);
        run.View.Plate.Parts.Clear();
        for (var i = 0; i < 12; i++)
            run.View.Plate.Parts.Add(Rectangle());
        run.View.OverlapDisplay = OverlapDisplayMode.Both;
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 15);
        run.View.SetOrigin(60, 120);
        run.View.MoveTo(run.View.PointWorldToControl(run.View.OverlapReport.Pairs[0].Centroid));
    }

    // These waits are deliberate: only Task.Run workers use the result gate; the test's
    // STA waits for capture delivery, then pumps every UI continuation explicitly.
    private static PlateOverlapReport WaitForWorkerResult(Task<PlateOverlapReport> task) => task.GetAwaiter().GetResult();

    private static PlateOverlapSnapshot WaitForSnapshot(Task<PlateOverlapSnapshot> task)
    {
        Assert.True(task.Wait(TimeSpan.FromSeconds(15)));
        return task.GetAwaiter().GetResult();
    }

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    private sealed class MenuHost : MainForm
    {
        // Do not run startup migrations/automatic new-document creation or save test window settings.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }

    private sealed class TestView : PlateView
    {
        public void Pan(float x, float y) { origin.X += x; origin.Y += y; }
        public void SetOrigin(float x, float y) => origin = new PointF(x, y);
        public void Recreate() => RecreateHandle();
        public void MoveTo(Point point) => OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));
        public void LeaveView() => OnMouseLeave(EventArgs.Empty);
        public bool Command(Keys keys)
        {
            var message = new Message();
            return ProcessCmdKey(ref message, keys);
        }
        public void WheelAt(Point point) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 120));
        public void DragAt(Point point) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0));
        public void ClickAt(Point point)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        }
    }

    private sealed class OverlayRun : IDisposable
    {
        private readonly PumpContext context = new();
        private readonly SynchronizationContext? previous;
        private readonly BlockingCollection<Work> work = new();
        private readonly List<Work> outstanding = new();
        private readonly List<Task> tasks = new();
        private bool disposed;
        public TestView View { get; } = new();
        public int Calls;

        public OverlayRun()
        {
            View.CreateControl();
            previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            View.Plate.Parts.Add(Rectangle());
            View.Plate.Parts.Add(Rectangle(1));
            View.OverlapOverlay.Analyze = (snapshot, token) =>
            {
                Interlocked.Increment(ref Calls);
                var request = new Work(snapshot, token);
                lock (outstanding)
                {
                    outstanding.Add(request);
                    if (disposed)
                        request.Release();
                    else
                        work.Add(request);
                }
                return request.WaitForResult();
            };
        }

        public Task Start()
        {
            var task = View.CheckOverlapsAsync(Units.Inches);
            tasks.Add(task);
            return task;
        }
        public Work Next()
        {
            Assert.True(work.TryTake(out var result, TimeSpan.FromSeconds(15)), "Worker did not start.");
            return result!;
        }
        public void Pump(Task task) => context.Pump(task);
        public Task AutoTick()
        {
            var task = View.OverlapOverlay.RunAutoCheckTimer();
            tasks.Add(task);
            return task;
        }
        public void EnableAutoCheck(Func<bool>? busy = null)
        {
            View.OverlapOverlay.IsInteractionActive = busy ?? (() => false);
            View.SetOverlapAutoCheck(() => Units.Inches);
        }
        public void Paint()
        {
            using var image = new Bitmap(64, 64);
            using var graphics = Graphics.FromImage(image);
            View.OverlapOverlay.Draw(graphics);
        }
        public void Finish()
        {
            var task = Start();
            Next().Complete();
            Pump(task);
        }
        public void Dispose()
        {
            if (disposed)
                return;
            try
            {
                View.Dispose();
            }
            finally
            {
                lock (outstanding)
                {
                    disposed = true;
                    foreach (var request in outstanding)
                        request.Release();
                }
                try { context.Pump(Task.WhenAll(tasks)); }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                    work.Dispose();
                }
            }
        }
    }

    private sealed class Work(PlateOverlapSnapshot snapshot, CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<PlateOverlapReport> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only called by Task.Run workers; the STA supplies the result and pumps continuations.
        public PlateOverlapReport WaitForResult() => Result.Task.GetAwaiter().GetResult();
        public void Complete() => Result.SetResult(PlateOverlapAnalyzer.Analyze(snapshot));
        public void Fail() => Result.SetException(new InvalidOperationException("test failure"));
        // Cancellation is deliberately ignored while testing late completions. Cleanup alone
        // releases every gate, including requests already taken from the pending queue.
        public void Release() => Result.TrySetCanceled();
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<System.Action> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add(() => callback(state));
        public void Pump(Task task)
        {
            while (!task.IsCompleted)
            {
                Assert.True(callbacks.TryTake(out var callback, TimeSpan.FromSeconds(15)), "UI continuation did not arrive.");
                callback!();
            }
            task.GetAwaiter().GetResult();
        }
    }

    private static Part Rectangle(double x = 0)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("same name", program), new Vector(x, 0));
    }

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
