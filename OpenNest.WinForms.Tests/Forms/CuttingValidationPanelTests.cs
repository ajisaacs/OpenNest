using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class CuttingValidationPanelTests
{
    [Fact]
    public void ValidateMenuUsesSharedPanelAndRealAllPlateFindingsRestoreOriginalView() => RunSta(() =>
    {
        using var host = new MenuHost();
        using var editor = RapidEditor();
        host.Show();
        editor.MdiParent = host;
        editor.Show();
        editor.Activate();
        editor.PlateManager.LoadAt(0);
        var view = editor.PlateView;
        view.OverlapDisplay = OverlapDisplayMode.Centroids;
        view.ZoomToArea(-10, -10, 50, 50);
        var viewport = view.CaptureViewport();
        var original = view.Plate;
        var programs = editor.Nest.Plates.SelectMany(p => p.Parts).Select(p => p.Program).ToArray();
        Invoke(host, "ValidateNest_Click", host, EventArgs.Empty);
        var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
        Pump(() => panel.ValidationReport != null && !panel.IsPlanning);
        Assert.Equal(CuttingInspectionMode.Validate, panel.Mode);
        Assert.False(Field<Button>(panel, "applyButton").Visible);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        Assert.False(view.HasProposalDisplay);
        var grid = Field<DataGridView>(panel, "warningGrid");
        Assert.Equal(panel.ValidationReport!.Findings.Count, grid.Rows.Count);
        var row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is PostVerificationFinding
        { Kind: PostVerificationKind.RapidCrossing, PlateNumber: 2, PartNumber: 3 });
        grid.ClearSelection();
        row.Selected = true; // Real SelectionChanged precedes CurrentCell assignment.
        Assert.Same(editor.Nest.Plates[1], view.Plate);
        Assert.Equal(new[] { new Vector(0, 2), new Vector(4, 2) }, view.ValidationOverlay.Finding.ContactPoints);
        Assert.True(view.ValidationOverlay.IsAnimating);
        var contactPoint = view.PointWorldToControl(new Vector(0, 2));
        using var bitmap = new Bitmap(view.Width, view.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        var origin = view.CaptureViewport().Origin;
        graphics.TranslateTransform(origin.X, origin.Y);
        view.ValidationOverlay.Draw(graphics);
        var cx = contactPoint.X;
        var cy = contactPoint.Y;
        Assert.InRange(cx, 15, bitmap.Width - 16);
        Assert.InRange(cy, 15, bitmap.Height - 16);
        Assert.Contains(Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(3, 7)
            .Select(dy => bitmap.GetPixel(cx + dx, cy + dy))), pixel =>
            pixel.R > 150 && pixel.B > 50 && pixel.G < 100);
        Assert.Contains("Crosshairs", Field<TextBox>(panel, "resultLabel").Text);
        Assert.Contains("Confirm minimum spacing, plate boundaries and final post output separately", Field<Label>(panel, "validationScopeLabel").Text);
        Assert.True(Field<Label>(panel, "validationScopeLabel").Visible);
        Assert.Contains("Nest validation (live programs)", Field<TextBox>(panel, "resultLabel").Text);
        panel.RequestClose();
        Assert.Same(original, view.Plate);
        Assert.Equal(viewport, view.CaptureViewport());
        Assert.Equal(OverlapDisplayMode.Centroids, view.OverlapDisplay);
        Assert.Null(view.ValidationOverlay.Finding);
        Assert.False(view.InspectionOnly);
        Assert.Equal(programs, editor.Nest.Plates.SelectMany(p => p.Parts).Select(p => p.Program));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShownValidationDistinguishesIncompleteFromClear(bool incomplete) => RunSta(() =>
    {
        var nest = new Nest();
        if (incomplete) nest.CreatePlate().Parts.Add(new Part(new Drawing("unsupported", new OpenNest.CNC.Program())));
        using var editor = new EditNestForm(nest);
        editor.Show();
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => panel.ValidationReport != null && !panel.IsPlanning);
        Assert.Contains(incomplete ? "Validation incomplete" : "No warnings found", Field<Label>(panel, "previewLabel").Text);
        Assert.True(Field<Label>(panel, "validationScopeLabel").Visible);
        Assert.Contains("LIVE NEST only", Field<Label>(panel, "validationScopeLabel").Text);
        Assert.False(Field<Button>(panel, "applyButton").Enabled);
        panel.RequestClose();
    });

    [Theory]
    [InlineData("PanelDispose")]
    [InlineData("EditorClose")]
    [InlineData("HostClose")]
    public void ExternalCloseJoinsValidationBeforeReleasingMdiGuard(string close) => RunSta(() =>
    {
        using var host = new MenuHost();
        using var editor = RapidEditor();
        using var other = new EditNestForm(editor.Nest);
        host.Show();
        other.MdiParent = host; other.Show();
        editor.MdiParent = host; editor.Show(); editor.Activate();
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => !panel.IsPlanning);
        var pending = new TaskCompletionSource<PostVerificationReport>();
        CancellationToken token = default;
        panel.Analyze = (_, cancellation) => { token = cancellation; return pending.Task; };
        Field<Button>(panel, "planButton").PerformClick();
        Assert.True(panel.IsPlanning);
        Assert.False(other.Enabled);
        Assert.False(host.AllowDrop);
        if (close == "PanelDispose") panel.Dispose();
        else if (close == "EditorClose") editor.Close();
        else host.Close();
        Assert.True(token.IsCancellationRequested);
        Assert.False(panel.IsDisposed);
        Assert.True(editor.PlateView.InspectionOnly);
        Assert.False(other.Enabled);
        pending.SetCanceled();
        Pump(() => panel.IsDisposed);
        if (close == "HostClose") Pump(() => host.IsDisposed);
        else if (close == "EditorClose") Pump(() => editor.IsDisposed);
        else
        {
            Assert.True(other.Enabled);
            Assert.False(editor.PlateView.InspectionOnly);
        }
    });

    [Fact]
    public void ModeSwitchJoinsBothWorkersAndDiscardsOldResultsWithoutUnlocking() => RunSta(() =>
    {
        using var editor = RapidEditor();
        editor.Show();
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => !panel.IsPlanning);
        var pending = new TaskCompletionSource<PostVerificationReport>();
        CancellationToken token = default;
        panel.Analyze = (_, cancellation) => { token = cancellation; return pending.Task; };
        Field<Button>(panel, "planButton").PerformClick();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        panel.BeforePlan = cancellation => { entered.Set(); release.Wait(TimeSpan.FromSeconds(20)); cancellation.ThrowIfCancellationRequested(); };
        try
        {
            Field<ComboBox>(panel, "modeSelector").SelectedIndex = 0;
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(CuttingInspectionMode.Validate, panel.Mode);
            Assert.True(editor.PlateView.InspectionOnly);
            pending.SetResult(PostVerificationAnalyzer.Analyze(new Nest()));
            Pump(() => entered.IsSet);
            Assert.Equal(CuttingInspectionMode.Plan, panel.Mode);
            Assert.Null(panel.ValidationReport);
            Assert.True(editor.PlateView.InspectionOnly);
            panel.Analyze = (nest, cancellation) => Task.FromResult(PostVerificationAnalyzer.Analyze(nest, cancellation));
            Field<ComboBox>(panel, "modeSelector").SelectedIndex = 1;
            Assert.Equal(CuttingInspectionMode.Plan, panel.Mode);
            Assert.True(editor.PlateView.InspectionOnly);
            release.Set();
            Pump(() => panel.Mode == CuttingInspectionMode.Validate && !panel.IsPlanning);
            Assert.NotNull(panel.ValidationReport);
            Assert.Null(panel.Proposal);
            Assert.False(editor.PlateView.HasProposalDisplay);
            Assert.False(Field<CheckBox>(panel, "acceptWarningsCheckBox").Checked);
            panel.RequestClose();
        }
        finally { release.Set(); }
    });

    [Fact]
    public void RecheckFailureNeverRetainsOldClearOrOverlay() => RunSta(() =>
    {
        using var editor = RapidEditor();
        editor.Show();
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => !panel.IsPlanning);
        panel.Analyze = (_, _) => throw new InvalidOperationException("synthetic validation failure");
        Field<Button>(panel, "planButton").PerformClick();
        Pump(() => !panel.IsPlanning);
        Assert.Null(panel.ValidationReport);
        Assert.Null(editor.PlateView.ValidationOverlay.Finding);
        Assert.Empty(Field<DataGridView>(panel, "warningGrid").Rows.Cast<DataGridViewRow>());
        Assert.Contains("Validation failed", Field<TextBox>(panel, "resultLabel").Text);
        Assert.DoesNotContain("No warnings found", Field<Label>(panel, "previewLabel").Text);
        panel.RequestClose();
    });

    [Fact]
    public void RestoreObserverFailureStillReleasesPanelAndInspectionLock() => RunSta(() =>
    {
        using var editor = RapidEditor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => panel.ValidationReport != null && !panel.IsPlanning);
        var row = Field<DataGridView>(panel, "warningGrid").Rows.Cast<DataGridViewRow>()
            .Single(r => r.Tag is PostVerificationFinding
            { Kind: PostVerificationKind.RapidCrossing, PlateNumber: 2, PartNumber: 3 });
        row.Selected = true;
        Assert.Equal(1, editor.PlateManager.CurrentIndex);
        Exception? reported = null;
        panel.OnRestoreError = ex => reported = ex;
        void ThrowOnRestore(object? _, PlateChangedEventArgs e)
        {
            if (e.Index == 0)
                throw new InvalidOperationException("synthetic plate-change observer");
        }
        editor.PlateManager.CurrentPlateChanged += ThrowOnRestore;
        try
        {
            panel.RequestClose();
            Assert.IsType<InvalidOperationException>(reported);
            Assert.Null(editor.CuttingInspection);
            Assert.True(panel.IsDisposed);
            Assert.False(editor.PlateView.InspectionOnly);
        }
        finally
        {
            editor.PlateManager.CurrentPlateChanged -= ThrowOnRestore;
        }
    });

    [Fact]
    public void OverlapNavigationKeepsAllPlateProgramsAndPosesStable() => RunSta(() =>
    {
        using var editor = RapidEditor();
        var plate = editor.Nest.Plates[1];
        plate.Parts.Add(new Part(plate.Parts[0].BaseDrawing) { Location = new Vector(2, 2) });
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var parts = editor.Nest.Plates.SelectMany(p => p.Parts).ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var codes = programs.Select(p => p.Codes.ToArray()).ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var rotations = parts.Select(p => p.Rotation).ToArray();
        editor.OpenValidationInspection();
        var panel = editor.CuttingInspection!;
        Pump(() => panel.ValidationReport != null && !panel.IsPlanning);
        var grid = Field<DataGridView>(panel, "warningGrid");
        Assert.True(grid.ReadOnly);
        Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
        Assert.All(grid.Columns.Cast<DataGridViewColumn>(), c => Assert.Equal(DataGridViewColumnSortMode.NotSortable, c.SortMode));
        Assert.True(Field<TextBox>(panel, "resultLabel").ReadOnly);
        var row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is PostVerificationFinding { Kind: PostVerificationKind.Overlap });
        row.Selected = true;
        var view = editor.PlateView;
        Assert.Same(plate, view.Plate);
        Assert.NotNull(view.ValidationOverlay.Finding.Overlap);
        Assert.Contains("centroid", Field<TextBox>(panel, "resultLabel").Text);
        var scale = view.ViewScale;
        InvokeView(view, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120));
        Assert.True(view.ViewScale > scale);
        var viewport = view.CaptureViewport();
        InvokeView(view, "OnMouseMove", new MouseEventArgs(MouseButtons.Middle, 0, 120, 110, 0));
        Assert.NotEqual(viewport.Origin, view.CaptureViewport().Origin);
        view.SelectAll();
        InvokeView(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));
        InvokeView(view, "ProcessDialogKey", Keys.Right);
        panel.RequestClose();
        Assert.Equal(parts, editor.Nest.Plates.SelectMany(p => p.Parts));
        Assert.Equal(programs, parts.Select(p => p.Program));
        for (var i = 0; i < programs.Length; i++) Assert.Equal(codes[i], programs[i].Codes.ToArray());
        Assert.Equal(locations, parts.Select(p => p.Location));
        Assert.Equal(rotations, parts.Select(p => p.Rotation));
        Assert.Null(view.ValidationOverlay.Finding);
        Assert.False(view.ValidationOverlay.IsAnimating);
    });

    private static void InvokeView(Control target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static EditNestForm RapidEditor()
    {
        var nest = new Nest();
        nest.CreatePlate().Size = new OpenNest.Geometry.Size(100, 100);
        var plate = nest.CreatePlate();
        plate.Size = new OpenNest.Geometry.Size(100, 100);
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0); program.LineTo(4, 0); program.LineTo(4, 4); program.LineTo(0, 4); program.LineTo(0, 0);
        plate.Parts.Add(new Part(new Drawing("square", program)));
        foreach (var x in new[] { -2d, 6d })
        {
            var scribe = new OpenNest.CNC.Program();
            scribe.MoveTo(x, 2);
            scribe.Codes.Add(new LinearMove(x, 2) { Layer = LayerType.Scribe });
            plate.Parts.Add(new Part(new Drawing("scribe", scribe)));
        }
        return new EditNestForm(nest);
    }

    private sealed class MenuHost : MainForm
    {
        protected override void OnLoad(EventArgs e) { }
    }

    private static T Field<T>(object target, string name) => Assert.IsType<T>(target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target));
    private static void Invoke(Control target, string name, params object[] args) => typeof(MainForm)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Pump(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.True(condition(), "Timed out waiting for validation panel.");
    }
    private static void RunSta(System.Action action) => StaTestThread.Run(action, TimeSpan.FromSeconds(45), "Validation panel STA timed out.");
}
