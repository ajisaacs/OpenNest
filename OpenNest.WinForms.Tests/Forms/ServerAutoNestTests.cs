using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.Forms;
using OpenNest.Forms.Commands;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// A server Auto Nest's progress window: the streamed stage text and the worker's best plan so
/// far in the same strip a local run fills, and the notice shown when a job ends while
/// OpenNest is not in front.
/// </summary>
public class ServerAutoNestTests
{
    private sealed class Collect : IProgress<NestJobProgress>
    {
        public readonly List<NestJobProgress> Reports = new();

        public void Report(NestJobProgress value)
        {
            lock (Reports)
                Reports.Add(value);
        }
    }

    private static NestPipelineRequest Request()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 20);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        return new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = new Drawing("bracket", program), Quantity = 9 } },
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 5));
    }

    [Fact]
    public void TheProgressWindowShowsTheStreamedStageAndPlan()
    {
        var request = Request();
        var tap = new Collect();
        NestPipeline.Run(new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName)) { Progress = tap },
            request.EngineName, request);
        var plan = tap.Reports.Last(r => r.Stage == NestJobStage.PlanImproved).Plan;
        var job = new NestJobRecord
        {
            Id = Guid.NewGuid(),
            EngineName = "Rectangles",
            State = NestJobState.Running,
            Revision = 3,
            Progress = RemoteNestProgress.From(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s0", 1, 1, 9)).ToJson(),
        };

        StaTestThread.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var cts = new CancellationTokenSource();
            using var form = new NestProgressForm(cts, showPlateRow: false) { AllowAccept = false, HoldOpenUntilCompleted = true };
            form.ShowPlanStrip(new ColorScheme());
            _ = form.Handle;
            var view = new ServerAutoNest.LiveView(form, request, cts.Token);
            var description = form.Controls.Find("descriptionValue", true).Single();

            view.ShowJob(job);
            Assert.Equal("On the nest server: Rectangles: evaluating plate 2 on stock s0", description.Text);

            view.ShowProblem("Waiting for the nest server: connection refused");
            Assert.StartsWith("Waiting for the nest server", description.Text);
            view.ShowProblem("");
            Assert.StartsWith("On the nest server", description.Text);

            view.ShowPlan(RemoteNestPlan.From(plan).ToJson());
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!view.Drawn.IsCompleted && DateTime.UtcNow < deadline)
                Application.DoEvents();
            Assert.True(view.Drawn.IsCompleted);
            var strip = Descendants(form).OfType<OpenNest.Controls.SheetStripControl>().Single();
            Assert.Equal(9, strip.PartCount);

            // Updates posted after the run ended change nothing.
            view.Stop();
            view.ShowJob(new NestJobRecord { State = NestJobState.Queued });
            Assert.StartsWith("On the nest server", description.Text);
        }, TimeSpan.FromSeconds(30), "The STA test did not complete.");
    }

    [Fact]
    public void TheNoticeShowsOnlyWhileOpenNestIsAwayAndGoesOnceItIsBack()
    {
        StaTestThread.Run(() =>
        {
            using var main = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
            _ = main.Handle;
            // Minimized counts as away even when one of OpenNest's windows is active.
            Assert.True(ServerNestNotice.IsAway(main));

            using var notice = ServerNestNotice.ShowIfAway(main, "Server nest finished", "N26-0042 is ready to review.");
            Assert.NotNull(notice);
            Assert.True(notice!.IsShown);

            // Clicking it restores the main window and removes the icon.
            notice.BringForward();
            Assert.Equal(FormWindowState.Normal, main.WindowState);
            Assert.False(notice.IsShown);
        }, TimeSpan.FromSeconds(15), "The STA test did not complete.");
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));
}
