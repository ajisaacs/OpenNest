using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class CuttingInspectionPanelTests
{
    [Fact]
    public void OnePlatePreviewUsesMainCanvasAndCancelRestoresLiveView() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        var view = editor.PlateView;
        var plate = view.Plate;
        var programs = plate.Parts.Select(part => part.Program).ToArray();
        view.ZoomToArea(-5, -5, 60, 60);
        var viewport = view.CaptureViewport();

        editor.OpenCuttingInspection();
        var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        Assert.True(panel.Proposal.CanApply);
        Assert.True(view.InspectionOnly);
        Assert.True(view.HasProposalDisplay);
        Assert.Same(plate, view.Plate);
        Assert.NotSame(plate, view.DisplayPlate);
        Assert.Equal(new[] { 1, 0 }, panel.Proposal.Plates[0].Result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(new[] { "near", "far" }, view.DisplayPlate.Parts.Select(p => p.BaseDrawing.Name));
        Assert.Equal(programs, plate.Parts.Select(part => part.Program));
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));

        var key = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        Assert.True((bool)typeof(OpenNest.Controls.PlateView)
            .GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [key, Keys.Escape])!);
        Assert.Null(editor.CuttingInspection);
        Assert.False(view.HasProposalDisplay);
        Assert.False(view.InspectionOnly);
        Assert.Same(plate, view.Plate);
        Assert.Equal(viewport, view.CaptureViewport());
        Assert.Equal(programs, plate.Parts.Select(part => part.Program));
    });

    [Fact]
    public void CloseDuringWorkerRetainsPanelAndInspectionLockUntilCompletion() => RunSta(() =>
    {
        using var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        editor.CuttingBeforePlan = token =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Assert.True(token.IsCancellationRequested);
        };
        try
        {
            editor.OpenCuttingInspection();
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            panel.RequestClose();
            Assert.True(panel.IsPlanning);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.True(editor.IsSidePanelVisible);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => panel.IsDisposed);
            Assert.False(editor.PlateView.InspectionOnly);
            Assert.False(editor.PlateView.HasProposalDisplay);
            Assert.Null(editor.CuttingInspection);
        }
        finally
        {
            release.Set();
        }
    });

    [Fact]
    public void DirectEditorDisposalWaitsForRunningWorker() => RunSta(() =>
    {
        var editor = Editor();
        editor.Show();
        editor.PlateManager.LoadAt(0);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        editor.CuttingBeforePlan = token =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Assert.True(token.IsCancellationRequested);
        };
        try
        {
            editor.OpenCuttingInspection();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            editor.Dispose();
            Assert.False(editor.IsDisposed);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => editor.IsDisposed);
            Assert.True(panel.IsDisposed);
        }
        finally
        {
            release.Set();
            editor.Dispose();
        }
    });

    [Fact]
    public void DirectMdiHostDisposalWaitsForRunningWorker() => RunSta(() =>
    {
        var host = new MainForm();
        var editor = Editor();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            host.Show();
            editor.MdiParent = host;
            editor.Show();
            editor.PlateManager.LoadAt(0);
            editor.CuttingBeforePlan = token =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                Assert.True(token.IsCancellationRequested);
            };
            editor.OpenCuttingInspection();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            var panel = Assert.IsType<CuttingInspectionPanel>(editor.CuttingInspection);
            host.Dispose();
            Assert.False(host.IsDisposed);
            Assert.False(editor.IsDisposed);
            Assert.True(editor.PlateView.InspectionOnly);
            Assert.False(panel.IsDisposed);
            release.Set();
            PumpUntil(() => host.IsDisposed);
            Assert.True(panel.IsDisposed);
        }
        finally
        {
            release.Set();
            host.Dispose();
        }
    });

    private static EditNestForm Editor()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new OpenNest.Geometry.Size(100, 100);
        plate.CuttingParameters = Parameters();
        plate.Parts.Add(Square("far", 12));
        plate.Parts.Add(Square("near", 1));
        return new EditNestForm(nest);
    }

    private static Part Square(string name, double x)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return new Part(new Drawing(name, program), new Vector(x, 1));
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        InternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 45 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        ArcCircleLeadOut = new NoLeadOut(),
    };

    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "Timed out waiting for cutting inspection.");
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(45), "STA cutting inspection test timed out.");
}
